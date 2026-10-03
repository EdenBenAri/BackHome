using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Animator window can keep edges after states are deleted.
/// Edge.WakeUp throws on the next script reload until those edges are dropped.
/// </summary>
[InitializeOnLoad]
static class AnimatorGraphRepair
{
    static AnimatorGraphRepair()
    {
        EditorApplication.delayCall += RepairOnce;
    }

    static void RepairOnce()
    {
        if (SessionState.GetBool("BackHome.AnimatorGraphRepaired", false))
            return;

        SessionState.SetBool("BackHome.AnimatorGraphRepaired", true);
        Repair();
    }

    public static void Repair()
    {
        Assembly graphAsm = null;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "UnityEditor.Graphs")
            {
                graphAsm = assembly;
                break;
            }
        }

        Type graphType = graphAsm?.GetType("UnityEditor.Graphs.Graph");
        Type edgeType = graphAsm?.GetType("UnityEditor.Graphs.Edge");
        Type toolType = graphAsm?.GetType("UnityEditor.Graphs.AnimatorControllerTool");
        if (graphType == null || edgeType == null)
            return;

        MethodInfo wakeUp = edgeType.GetMethod("WakeUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo removeEdge = graphType.GetMethod("RemoveEdge", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        int removed = 0;

        foreach (UnityEngine.Object graph in Resources.FindObjectsOfTypeAll(graphType))
        {
            FieldInfo edgesField = FindField(graph.GetType(), "edges", flags);
            if (edgesField?.GetValue(graph) is not IList edges)
                continue;

            var doomed = new System.Collections.Generic.List<object>();
            foreach (object edge in edges)
            {
                if (edge == null || wakeUp == null)
                {
                    doomed.Add(edge);
                    continue;
                }

                try
                {
                    wakeUp.Invoke(edge, null);
                }
                catch (TargetInvocationException)
                {
                    doomed.Add(edge);
                }
            }

            foreach (object edge in doomed)
            {
                try
                {
                    if (removeEdge != null && edge != null && removeEdge.GetParameters().Length == 1)
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

            FieldInfo invalidField = FindField(graph.GetType(), "m_InvalidEdges", flags);
            if (invalidField?.GetValue(graph) is IList invalidEdges && invalidEdges.Count > 0)
            {
                removed += invalidEdges.Count;
                invalidEdges.Clear();
            }
        }

        MethodInfo rebuild = toolType?.GetMethod(
            "RebuildGraph",
            flags,
            null,
            new[] { typeof(bool) },
            null);
        if (rebuild != null)
        {
            foreach (UnityEngine.Object window in Resources.FindObjectsOfTypeAll(toolType))
            {
                try
                {
                    rebuild.Invoke(window, new object[] { true });
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        if (removed > 0)
            Debug.Log($"[BackHome] Removed {removed} broken animator edges.");
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
