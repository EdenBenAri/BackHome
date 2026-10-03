using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CreatureStats))]
public class CreatureStatsEditor : Editor
{
    const string SnarlfangHelp =
        "On spotting the player, Snarlfang fades out and cannot be targeted. " +
        "The fade lasts until Ability Duration ends or the player is inside Special Attack Range, " +
        "then one Special Attack plays and Basic Attacks take over. " +
        "Losing the player ends the fade with no Special Attack.";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty specialAbility = serializedObject.FindProperty("specialAbility");
        SerializedProperty abilityDuration = serializedObject.FindProperty("abilityDuration");
        SerializedProperty stealthOpacity = serializedObject.FindProperty("stealthOpacity");
        SerializedProperty specialAttackDamage = serializedObject.FindProperty("specialAttackDamage");
        SerializedProperty specialAttackSpeed = serializedObject.FindProperty("specialAttackSpeed");
        SerializedProperty specialAttackRange = serializedObject.FindProperty("specialAttackRange");

        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (property.name == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(property, true);
                continue;
            }

            if (IsAbilityField(property.name))
                continue;

            EditorGUILayout.PropertyField(property, true);

            if (property.name == "specialAbility")
                DrawAbilityFields(specialAbility, abilityDuration, stealthOpacity, specialAttackDamage, specialAttackSpeed, specialAttackRange);
        }

        serializedObject.ApplyModifiedProperties();
    }

    static void DrawAbilityFields(
        SerializedProperty specialAbility,
        SerializedProperty abilityDuration,
        SerializedProperty stealthOpacity,
        SerializedProperty specialAttackDamage,
        SerializedProperty specialAttackSpeed,
        SerializedProperty specialAttackRange)
    {
        switch ((CreatureSpecialAbility)specialAbility.intValue)
        {
            case CreatureSpecialAbility.Stealth:
                EditorGUILayout.HelpBox(SnarlfangHelp, MessageType.Info);
                EditorGUILayout.PropertyField(abilityDuration, true);
                EditorGUILayout.PropertyField(stealthOpacity, true);
                EditorGUILayout.PropertyField(specialAttackDamage, true);
                EditorGUILayout.PropertyField(specialAttackSpeed, true);
                EditorGUILayout.PropertyField(specialAttackRange, true);
                break;
        }
    }

    /// <summary>
    /// Fields owned by a specific ability. Add new ones to the matching case in <see cref="DrawAbilityFields"/>.
    /// </summary>
    static bool IsAbilityField(string propertyName)
    {
        switch (propertyName)
        {
            case "abilityDuration":
            case "stealthOpacity":
            case "specialAttackDamage":
            case "specialAttackSpeed":
            case "specialAttackRange":
                return true;
            default:
                return false;
        }
    }
}
