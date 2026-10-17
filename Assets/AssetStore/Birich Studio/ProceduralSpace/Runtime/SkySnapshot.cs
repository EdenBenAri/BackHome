using UnityEngine;

namespace BirichStudio.ProceduralSpace
{
    /// <summary>
    /// One specific sky, frozen: the seed <b>and</b> a complete copy of the settings that seed was
    /// composed with.
    ///
    /// A profile is a style and a seed is a sky inside that style, which works right up until the
    /// style moves. Roll seeds until one is perfect, nudge a slider a week later, and that sky is
    /// gone — the seed still exists but no longer means what it meant, and there is no way back to
    /// it because the thing that changed was not the seed. Saving the seed alone is saving half a
    /// coordinate.
    ///
    /// So a snapshot carries its own copy of every setting. Restoring it does not depend on the
    /// profile still being what it was, or on the profile existing at all.
    ///
    /// The settings are stored as JSON rather than as a nested asset for two reasons: a snapshot
    /// stays a single small text file that source control can diff and merge, and a snapshot taken
    /// by an older version still restores under a newer one — fields it has never heard of simply
    /// keep whatever the target already had, instead of failing to load.
    /// </summary>
    public class SkySnapshot : ScriptableObject
    {
        [Tooltip("Your own label for this sky. The one thing a list of snapshots cannot infer.")]
        [TextArea(2, 4)] public string note = "";

        [Tooltip("The seed this sky was composed from.")]
        public int seed;

        [Tooltip("Whether the sky type was drawn from the seed, or forced. Part of the sky's " +
                 "identity: the same seed composes a different sky if this differs.")]
        public bool randomArchetype = true;

        [Tooltip("The sky type that was forced, when Random Archetype is off.")]
        public SkyArchetype forcedArchetype;

        [Tooltip("The profile this was captured from, recorded so a snapshot can be traced back " +
                 "to where it came from. Restoring does not need it.")]
        public string capturedFrom = "";

        [Tooltip("When it was captured, UTC.")]
        public string capturedUtc = "";

        // Hidden because it is not something to edit by hand — and because a wall of JSON at the
        // top of the inspector would bury the four fields that are.
        [SerializeField, HideInInspector] private string settings = "";

        /// <summary>True when this snapshot actually holds settings to restore.</summary>
        public bool HasSettings => !string.IsNullOrEmpty(settings);

        /// <summary>Freeze a profile and the component state that composes with it.</summary>
        public void Capture(SpaceBackgroundProfile profile, int seedValue,
                            bool randomArchetypeValue, SkyArchetype forcedArchetypeValue)
        {
            seed = seedValue;
            randomArchetype = randomArchetypeValue;
            forcedArchetype = forcedArchetypeValue;
            capturedFrom = profile != null ? profile.name : "";
            capturedUtc = System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm",
                                                          System.Globalization.CultureInfo.InvariantCulture);
            settings = profile != null ? JsonUtility.ToJson(profile) : "";
        }

        /// <summary>
        /// Write the frozen settings back into a profile. Returns false if there is nothing stored.
        ///
        /// This MUTATES the profile it is given — that is what restoring means. Hand it a profile
        /// you are willing to overwrite; if that profile is shared, every background using it
        /// changes, exactly as it would if you had edited the settings by hand.
        /// </summary>
        public bool ApplyTo(SpaceBackgroundProfile profile)
        {
            if (profile == null || !HasSettings) return false;
            JsonUtility.FromJsonOverwrite(settings, profile);
            return true;
        }
    }
}
