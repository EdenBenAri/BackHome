using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CreatureStats))]
public class CreatureStatsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var stats = (CreatureStats)target;
        int level = stats.Level;
        float hp = LevelTable.ScalePower(stats.MaxHealth, level);
        float damage = LevelTable.ScalePower(stats.AttackDamage, level);
        float power = LevelTable.PowerMultiplier(level);

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "Health and attack damage above are the level-1 body. Play mode multiplies them by this level " +
            "on the same curve as the player. A player at this level fights the body as written. " +
            "Attack speed, range, and move speed are not scaled.",
            MessageType.Info);
        EditorGUILayout.LabelField(
            "In play",
            $"Lv{level}   ×{power:0.00}   {hp:0.#} HP   {damage:0.#} dmg");
    }
}
