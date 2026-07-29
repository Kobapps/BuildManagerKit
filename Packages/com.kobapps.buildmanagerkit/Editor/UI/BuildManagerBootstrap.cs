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
