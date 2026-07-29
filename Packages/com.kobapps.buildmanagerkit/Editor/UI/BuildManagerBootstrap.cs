using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Creates the assets a new project needs, so the first run of the window is a two click setup
    /// rather than a blank screen. Everything it produces is an ordinary asset the team can edit,
    /// rename or delete.
    /// </summary>
    internal static class BuildManagerBootstrap
    {
        private const string k_ProfilesFolder = ProjectPaths.DefaultSettingsFolder + "/Profiles";
        private const string k_EnvironmentsFolder = ProjectPaths.DefaultSettingsFolder + "/Environments";

        /// <summary>
        /// Deletes a profile asset and every reference the configuration holds to it: the catalogue
        /// entry and any queue entries that built it.
        ///
        /// Leaving the queue entries behind would turn a tidy delete into a queue that silently
        /// skips a slot, which is exactly the sort of half-broken state the health check then has to
        /// report.
        /// </summary>
        /// <param name="profile">Profile to delete.</param>
        /// <returns>False when there was nothing to delete.</returns>
        internal static bool DeleteProfile(BuildTargetProfile profile) =>
            DeleteProfile(BuildManagerSettings.Instance, profile);

        /// <summary>
        /// Deletes a profile from a specific settings asset. Split out so the unregistering can be
        /// exercised without touching the project's own settings.
        /// </summary>
        /// <param name="settings">Settings holding the catalogue and the queues.</param>
        /// <param name="profile">Profile to delete.</param>
        /// <returns>False when there was nothing to delete.</returns>
        internal static bool DeleteProfile(BuildManagerSettings settings, BuildTargetProfile profile)
        {
            if (profile == null || settings == null)
                return false;

            var path = AssetDatabase.GetAssetPath(profile);
            var id = profile.Id;

            var queueEntries = 0;
            foreach (var queue in settings.QueuesMutable)
            {
                if (queue?.entries == null)
                    continue;

                queueEntries += queue.entries.RemoveAll(entry => entry == null || entry.profile == profile);
            }

            settings.ProfilesMutable.RemoveAll(candidate => candidate == profile);
            settings.Save();

            if (!string.IsNullOrEmpty(path))
                AssetDatabase.DeleteAsset(path);

            AssetDatabase.SaveAssets();

            Debug.Log($"[BuildManagerKit] Deleted profile '{id}'"
                      + (string.IsNullOrEmpty(path) ? string.Empty : $" ({path})")
                      + (queueEntries > 0 ? $" and {queueEntries} queue entr(y/ies) that used it." : "."));

            return true;
        }

        /// <summary>
        /// Deletes an environment asset and every reference to it: the catalogue entry, the active
        /// slot, the queues that built it and the profiles that allowed or defaulted to it.
        ///
        /// The profile references have to be rewritten through <see cref="SerializedObject"/> rather
        /// than left dangling — a profile whose default environment is a deleted asset fails every
        /// build that does not name one explicitly.
        /// </summary>
        /// <param name="environment">Environment to delete.</param>
        /// <returns>False when there was nothing to delete.</returns>
        internal static bool DeleteEnvironment(BuildEnvironment environment) =>
            DeleteEnvironment(BuildManagerSettings.Instance, environment);

        /// <summary>Deletes an environment from a specific settings asset.</summary>
        /// <param name="settings">Settings holding the catalogue, the queues and the profiles.</param>
        /// <param name="environment">Environment to delete.</param>
        /// <returns>False when there was nothing to delete.</returns>
        internal static bool DeleteEnvironment(BuildManagerSettings settings, BuildEnvironment environment)
        {
            if (environment == null || settings == null)
                return false;

            var path = AssetDatabase.GetAssetPath(environment);
            var id = environment.Id;
            var wasActive = settings.ActiveEnvironment == environment;

            foreach (var queue in settings.QueuesMutable)
            {
                if (queue == null)
                    continue;

                if (queue.defaultEnvironment == environment)
                    queue.defaultEnvironment = null;

                if (queue.entries == null)
                    continue;

                foreach (var entry in queue.entries)
                {
                    if (entry != null && entry.environmentOverride == environment)
                        entry.environmentOverride = null;
                }
            }

            foreach (var profile in settings.ProfilesMutable)
            {
                if (profile == null)
                    continue;

                ClearEnvironmentReferences(profile, environment);
            }

            settings.EnvironmentsMutable.RemoveAll(candidate => candidate == environment);

            // Clearing the reference is not enough on its own: the Editor still has this
            // environment's defines and player settings applied, so hand it over to the next one.
            // Activation always works through the project's own settings asset, so it is only
            // correct to trigger when that is what is being edited.
            if (wasActive)
            {
                var replacement = settings.GetSortedEnvironments().FirstOrDefault();
                settings.ActiveEnvironment = null;

                if (replacement != null && settings == BuildManagerSettings.InstanceOrNull)
                    EnvironmentManager.Activate(replacement);
            }

            settings.Save();

            if (!string.IsNullOrEmpty(path))
                AssetDatabase.DeleteAsset(path);

            AssetDatabase.SaveAssets();

            Debug.Log($"[BuildManagerKit] Deleted environment '{id}'"
                      + (string.IsNullOrEmpty(path) ? string.Empty : $" ({path})") + ".");

            return true;
        }

        /// <summary>Drops an environment from a profile's allowed list and default slot.</summary>
        private static void ClearEnvironmentReferences(BuildTargetProfile profile, BuildEnvironment environment)
        {
            var allowed = profile.AllowedEnvironments;
            var referenced = profile.DefaultEnvironment == environment
                             || (allowed != null && allowed.Contains(environment));

            if (!referenced)
                return;

            var serialized = new SerializedObject(profile);
            var defaultEnvironment = serialized.FindProperty("m_DefaultEnvironment");

            if (defaultEnvironment.objectReferenceValue == environment)
                defaultEnvironment.objectReferenceValue = null;

            var list = serialized.FindProperty("m_AllowedEnvironments");
            for (var i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == environment)
                    list.DeleteArrayElementAtIndex(i);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }

        /// <summary>Creates a profile asset for a platform and registers it in the settings.</summary>
        /// <param name="target">Platform the profile builds.</param>
        /// <param name="server">Create a dedicated server profile.</param>
        internal static BuildTargetProfile CreateProfile(BuildTarget target, bool server = false)
        {
            ProjectPaths.EnsureAssetFolder(k_ProfilesFolder);

            var shortName = BuildTargetUtility.GetShortName(target);
            var id = (server ? "server-" : string.Empty) + shortName.ToLowerInvariant();
            var displayName = server ? $"Dedicated Server ({shortName})" : shortName;

            var profile = ScriptableObject.CreateInstance<BuildTargetProfile>();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{k_ProfilesFolder}/Profile_{shortName}.asset");
            AssetDatabase.CreateAsset(profile, path);

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_Id").stringValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = displayName;
            // intValue writes the underlying enum value; enumValueIndex would index into the
            // declaration-ordered name list, which does not match BuildTarget's sparse values.
            serialized.FindProperty("m_Target").intValue = (int)target;
            serialized.FindProperty("m_StandaloneSubtarget").intValue =
                server ? (int)StandaloneBuildSubtarget.Server
                       : (int)StandaloneBuildSubtarget.Player;

            if (target == BuildTarget.Android)
                serialized.FindProperty("m_Android").FindPropertyRelative("buildAppBundle").boolValue = true;

            // A new profile has nothing to migrate and starts out inheriting the project's common
            // versioning; without this the migration would read the field defaults and mistake it for
            // a pre-1.2 asset that versions itself.
            serialized.FindProperty("m_VersioningMigrated").boolValue = true;
            serialized.FindProperty("m_OverrideVersioning").boolValue = false;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            var settings = BuildManagerSettings.Instance;
            settings.ProfilesMutable.Add(profile);
            settings.Save();

            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>Creates an environment asset and registers it in the settings.</summary>
        /// <param name="id">Stable identifier, e.g. <c>prod</c>.</param>
        /// <param name="displayName">Name shown in the UI.</param>
        /// <param name="color">Accent colour.</param>
        /// <param name="requireConfirmation">Ask before activating or building.</param>
        internal static BuildEnvironment CreateEnvironment(
            string id,
            string displayName,
            Color color,
            bool requireConfirmation = false)
        {
            ProjectPaths.EnsureAssetFolder(k_EnvironmentsFolder);

            var environment = ScriptableObject.CreateInstance<BuildEnvironment>();
            var path = AssetDatabase.GenerateUniqueAssetPath(
                $"{k_EnvironmentsFolder}/Env_{BuildTokens.Sanitize(displayName)}.asset");

            AssetDatabase.CreateAsset(environment, path);

            var serialized = new SerializedObject(environment);
            serialized.FindProperty("m_Id").stringValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = displayName;
            serialized.FindProperty("m_Color").colorValue = color;
            serialized.FindProperty("m_RequireConfirmation").boolValue = requireConfirmation;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var settings = BuildManagerSettings.Instance;
            settings.EnvironmentsMutable.Add(environment);
            settings.Save();

            AssetDatabase.SaveAssets();
            return environment;
        }

        /// <summary>Creates profiles for every platform whose module is installed.</summary>
        internal static void CreateDefaultProfiles()
        {
            var settings = BuildManagerSettings.Instance;
            var created = 0;

            foreach (var target in BuildTargetUtility.CommonTargets)
            {
                if (!BuildTargetUtility.IsTargetInstalled(target))
                    continue;

                if (settings.Profiles.Any(profile => profile != null && profile.Target == target))
                    continue;

                CreateProfile(target);
                created++;
            }

            if (created == 0)
            {
                // Nothing installed beyond the current platform: at least give them that one.
                CreateProfile(EditorUserBuildSettings.activeBuildTarget);
                created = 1;
            }

            Debug.Log($"[BuildManagerKit] Created {created} build profile(s) in {k_ProfilesFolder}.");
        }

        /// <summary>Creates the conventional dev / stage / prod trio and activates dev.</summary>
        internal static void CreateDefaultEnvironments()
        {
            var settings = BuildManagerSettings.Instance;

            var development = settings.FindEnvironment("dev")
                              ?? CreateEnvironment("dev", "Development", new Color(0.29f, 0.66f, 0.98f));

            if (settings.FindEnvironment("stage") == null)
                CreateEnvironment("stage", "Staging", new Color(0.95f, 0.68f, 0.20f));

            if (settings.FindEnvironment("prod") == null)
                CreateEnvironment("prod", "Production", new Color(0.25f, 0.73f, 0.31f), requireConfirmation: true);

            if (settings.ActiveEnvironment == null)
                EnvironmentManager.Activate(development);

            Debug.Log($"[BuildManagerKit] Environments ready in {k_EnvironmentsFolder}.");
        }

        /// <summary>Creates a settings asset, the environment trio and profiles in one go.</summary>
        internal static void CreateEverything()
        {
            _ = BuildManagerSettings.Instance;
            CreateDefaultEnvironments();
            CreateDefaultProfiles();
        }
    }
}
