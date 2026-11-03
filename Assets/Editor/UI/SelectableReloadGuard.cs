using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Unity resets <see cref="Selectable"/>'s static list on assembly reload, but leaves
/// each control marked enabled. The next OnDisable then indexes an empty list and
/// throws IndexOutOfRangeException. Clear that flag first so OnDisable returns early.
/// </summary>
[InitializeOnLoad]
static class SelectableReloadGuard
{
    static readonly FieldInfo EnableCalled = typeof(Selectable).GetField(
        "m_EnableCalled",
        BindingFlags.Instance | BindingFlags.NonPublic);

    static SelectableReloadGuard()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ClearEnableFlags;
    }

    static void ClearEnableFlags()
    {
        if (EnableCalled == null)
            return;

        Selectable[] selectables = Resources.FindObjectsOfTypeAll<Selectable>();
        for (int i = 0; i < selectables.Length; i++)
        {
            if (selectables[i] != null)
                EnableCalled.SetValue(selectables[i], false);
        }
    }
}
