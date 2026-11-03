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
        float hp = LevelTable.ScaleEnemyHealth(stats.MaxHealth, level);
        float damage = LevelTable.ScaleEnemyDamage(stats.AttackDamage, level);

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "Health, damage and XP above are level-1 budgets. A spawn area's levels override this default without changing the asset. " +
            $"Enemy HP grows {LevelTable.AttackGrowthPercent:0.#}% and damage grows {LevelTable.HealthGrowthPercent:0.#}% per threat level. " +
            "Attack speed, range, and move speed are not scaled.",
            MessageType.Info);
        EditorGUILayout.LabelField(
            "In play",
            $"Lv{level}   {hp:0.#} HP   {damage:0.#} dmg   {LevelTable.ScaleExperience(stats.Experience, level)} XP");
    }
}
