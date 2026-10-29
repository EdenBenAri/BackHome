using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerStats))]
public class PlayerStatsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var stats = (PlayerStats)target;
        CombatStats origin = stats.BaseCombat;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Resolved Combat", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Each weapon floats on its own and fights with Combat + that weapon (up to 3).\n" +
            "The range ring uses the longest weapon range.",
            MessageType.Info);

        EditorGUILayout.LabelField("Base", FormatCombat(origin));
        var weapons = stats.Weapons;
        bool anyWeapon = false;
        int shown = 0;
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponDefinition weapon = weapons[i];
            if (weapon == null)
                continue;
            anyWeapon = true;
            if (shown >= CombatLoadout.MaxWeapons)
            {
                EditorGUILayout.HelpBox($"Only the first {CombatLoadout.MaxWeapons} weapons are used.", MessageType.Warning);
                break;
            }

            CombatStats resolved = stats.CombatFor(weapon);
            EditorGUILayout.LabelField($"  {weapon.DisplayName}", FormatCombat(resolved));
            shown++;
        }

        if (!anyWeapon)
            EditorGUILayout.LabelField("  Weapons", "none — base combat only");

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.FloatField("Range Ring", stats.MaxAttackRange);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("By Account Level", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Level 1 is this asset. Health grows 4% per level. Attack gains a flat +3 per level. " +
            "Oxygen, attack speed, range, and move speed stay as written.",
            MessageType.Info);
        DrawLevelRow(stats, 1);
        DrawLevelRow(stats, 10);
        DrawLevelRow(stats, 20);
        DrawLevelRow(stats, 30);
    }

    static void DrawLevelRow(PlayerStats stats, int level)
    {
        float hp = LevelTable.ScalePower(stats.MaxHealth, level);
        float damage = LevelTable.ScaleAttack(stats.BaseCombat.AttackDamage, level);
        EditorGUILayout.LabelField($"Lv{level}", $"{hp:0} HP    {stats.OxygenTank:0} O2    {damage:0.#} base dmg");
    }

    static string FormatCombat(CombatStats combat)
    {
        return $"{combat.AttackDamage:0.##} dmg  |  {combat.AttackSpeed:0.##} spd  |  {combat.AttackRange:0.##} rng";
    }
}
