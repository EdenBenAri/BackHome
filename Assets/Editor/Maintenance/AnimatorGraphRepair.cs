using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Animator window can keep an edge after its node leaves the graph.
/// Edge.WakeUp then calls CanConnect on a null graph and throws on every domain reload.
/// Strip those edges before the editor serializes the window, and again after reload.
/// </summary>
[InitializeOnLoad]
static class AnimatorGraphRepair
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static AnimatorGraphRepair()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Repair;
        EditorApplication.delayCall += Repair;
    }

    public static void Repair()
    {
        Assembly graphAsm;
        try
        {
            graphAsm = Assembly.Load("UnityEditor.Graphs");
        }
        catch (Exception)
        {
            return;
        }

        Type graphType = graphAsm.GetType("UnityEditor.Graphs.Graph");
        Type edgeType = graphAsm.GetType("UnityEditor.Graphs.Edge");
        Type nodeType = graphAsm.GetType("UnityEditor.Graphs.Node");
        if (graphType == null || edgeType == null || nodeType == null)
            return;

        FieldInfo fromNodeField = edgeType.GetField("m_FromNode", Flags);
        FieldInfo toNodeField = edgeType.GetField("m_ToNode", Flags);
        FieldInfo nodeGraphField = nodeType.GetField("m_Graph", Flags);
        MethodInfo removeEdge = graphType.GetMethod("RemoveEdge", Flags);
        if (fromNodeField == null || toNodeField == null)
            return;

        int removed = 0;
        foreach (UnityEngine.Object graph in Resources.FindObjectsOfTypeAll(graphType))
        {
            FieldInfo edgesField = FindField(graph.GetType(), "edges", Flags);
            FieldInfo nodesField = FindField(graph.GetType(), "nodes", Flags);
            if (edgesField?.GetValue(graph) is not IList edges)
                continue;

            IList nodes = nodesField?.GetValue(graph) as IList;
            var doomed = new List<object>();
            foreach (object edge in edges)
            {
                if (IsBroken(edge, fromNodeField, toNodeField, nodeGraphField, nodes))
                    doomed.Add(edge);
            }

            foreach (object edge in doomed)
            {
                try
                {
                    if (edge != null && removeEdge != null && removeEdge.GetParameters().Length == 1)
                        removeEdge.Invoke(graph, new[] { edge });
                    else
                        edges.Remove(edge);
                }
                catch (Exception)
                {
                    edges.Remove(edge);
                }

                removed++;
            }

            FieldInfo invalidField = FindField(graph.GetType(), "m_InvalidEdges", Flags);
            if (invalidField?.GetValue(graph) is IList invalidEdges && invalidEdges.Count > 0)
            {
                removed += invalidEdges.Count;
                invalidEdges.Clear();
            }
        }

        if (removed > 0)
            Debug.Log($"[BackHome] Removed {removed} broken animator edges.");
    }

    static bool IsBroken(
        object edge,
        FieldInfo fromNodeField,
        FieldInfo toNodeField,
        FieldInfo nodeGraphField,
        IList nodes)
    {
        if (edge == null)
            return true;

        return NodeMissing(fromNodeField.GetValue(edge), nodeGraphField, nodes)
            || NodeMissing(toNodeField.GetValue(edge), nodeGraphField, nodes);
    }

    static bool NodeMissing(object node, FieldInfo nodeGraphField, IList nodes)
    {
        if (node is not UnityEngine.Object unityNode || unityNode == null)
            return true;

        // A node that is still in the graph gets its graph reference during WakeUp.
        // An edge whose node is missing from that list is what makes Edge.WakeUp throw.
        if (nodes == null)
            return nodeGraphField?.GetValue(node) is not UnityEngine.Object graphObject || graphObject == null;

        foreach (object candidate in nodes)
        {
            if (ReferenceEquals(candidate, node))
                return false;
        }

        return true;
    }

    static FieldInfo FindField(Type type, string name, BindingFlags flags)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(name, flags);
            if (field != null)
                return field;
        }

        return null;
    }
}
