using UnityEditor;

namespace BirichStudio.ProceduralSpace.Editor
{
    /// <summary>Tiered inspector for the profile asset itself — the same panel the component embeds.</summary>
    [CustomEditor(typeof(SpaceBackgroundProfile))]
    [CanEditMultipleObjects]
    internal class SpaceBackgroundProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => ProfilePanel.Draw(serializedObject);
    }
}
