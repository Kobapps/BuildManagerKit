using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>One profile inside a queue, optionally pinned to a specific environment.</summary>
    [Serializable]
    public sealed class BuildQueueEntry
    {
        [Tooltip("Profile to build.")]
        public BuildTargetProfile profile;

        [Tooltip("Environment to build it with. Leave empty to use the queue default.")]
        public BuildEnvironment environmentOverride;

        [Tooltip("Disabled entries are skipped.")]
        public bool enabled = true;
    }

    /// <summary>
    /// An ordered list of profiles built back to back, for example "ship everything" or
    /// "nightly". Queues survive the domain reloads caused by switching platforms.
    /// </summary>
    [Serializable]
    public sealed class BuildQueue
    {
        [Tooltip("Identifier used by the command line (-bmkQueue).")]
        public string id = "release-all";

        [Tooltip("Name shown in the UI.")]
        public string displayName = "Release All";

        [Tooltip("Environment used by entries that do not override it.")]
        public BuildEnvironment defaultEnvironment;

        [Tooltip("Abort the remaining entries as soon as one fails.")]
        public bool stopOnFirstFailure = true;

        [Tooltip("Profiles to build, in order.")]
        public List<BuildQueueEntry> entries = new List<BuildQueueEntry>();

        /// <summary>Entries that are enabled and point at an enabled profile.</summary>
        public IEnumerable<BuildQueueEntry> ActiveEntries =>
            entries.Where(entry => entry != null && entry.enabled && entry.profile != null && entry.profile.Enabled);

        /// <summary>Name shown in the UI, falling back to the identifier.</summary>
        public string Title => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
    }

    /// <summary>
    /// The single project-wide configuration asset: the catalogue of profiles, environments and
    /// queues plus the global action lists and behaviour toggles.
    /// </summary>
    [CreateAssetMenu(menuName = "Build Manager Kit/Build Manager Settings", fileName = "BuildManagerSettings",
        order = 99)]
    public sealed class BuildManagerSettings : ScriptableObject
    {
        [Header("Catalogue")]
        [SerializeField] private List<BuildTargetProfile> m_Profiles = new List<BuildTargetProfile>();
        [SerializeField] private List<BuildEnvironment> m_Environments = new List<BuildEnvironment>();
        [SerializeField] private List<BuildQueue> m_Queues = new List<BuildQueue>();

        [Header("Active Environment")]
        [Tooltip("The environment currently applied to the Editor. Change it from the Build Manager window.")]
        [SerializeField] private BuildEnvironment m_ActiveEnvironment;

        [Header("Common Configuration")]
        [Tooltip("The settings that are the same in every environment — company, product name, bundle "
                 + "identifier, icon, shared runtime variables and versioning. Environments override only "
                 + "what differs. Edited at the top of the Environments tab.")]
        [SerializeField] private CommonBuildConfig m_Common = new CommonBuildConfig();

        [Header("Global Config Assets")]
        [Tooltip("Assets published to runtime code for EVERY environment. An environment that lists the "
                 + "same key overrides the default, so shared assets are configured once here.")]
        [SerializeField] private List<EnvironmentAssetEntry> m_DefaultConfigAssets =
            new List<EnvironmentAssetEntry>();

        [Header("Global Actions")]
        [Tooltip("Runs whenever ANY environment is activated, before that environment's own actions. "
                 + "Configure shared activation work here instead of repeating it on every environment.")]
        [SerializeReference] private List<BuildStep> m_GlobalOnActivateSteps = new List<BuildStep>();

        [Tooltip("Runs before every build of every profile, first in the chain.")]
        [SerializeReference] private List<BuildStep> m_GlobalPreBuildSteps = new List<BuildStep>();

        [Tooltip("Runs after every build of every profile, last in the chain.")]
        [SerializeReference] private List<BuildStep> m_GlobalPostBuildSteps = new List<BuildStep>();

        [Header("Behaviour")]
        [Tooltip("Restore PlayerSettings and EditorUserBuildSettings to their pre-build values when a build finishes.")]
        [SerializeField] private bool m_RestoreSettingsAfterBuild = true;

        [Tooltip("Generate Assets/Resources/BuildManagerKit/BuildInfo.asset so runtime code can read the environment.")]
        [SerializeField] private bool m_WriteBuildInfoAsset = true;

        [Tooltip("Generate Assets/Resources/BuildManagerKit/EnvironmentAssets.asset so runtime code can load the "
                 + "active environment's config assets.")]
        [SerializeField] private bool m_WriteEnvironmentAssets = true;

        [Tooltip("Write a build_manifest.json next to every build output.")]
        [SerializeField] private bool m_WriteBuildManifest = true;

        [Tooltip("Write the full text log of every run to the log folder.")]
        [SerializeField] private bool m_WriteLogFiles = true;

        [Tooltip("Project relative folder the run logs are written to.")]
        [SerializeField] private string m_LogFolder = "Logs/BuildManagerKit";

        [Tooltip("How many past runs are kept in the build history.")]
        [SerializeField, Range(10, 500)] private int m_HistoryLimit = 100;

        [Tooltip("Abort the build when the resolved scene list is empty.")]
        [SerializeField] private bool m_FailOnEmptySceneList = true;

        [Tooltip("Ask for confirmation before builds started from the Editor UI.")]
        [SerializeField] private bool m_ConfirmBeforeBuilding;

        [Tooltip("Open the output folder when a build made from the Editor succeeds.")]
        [SerializeField] private bool m_RevealOutputOnSuccess = true;

        private static BuildManagerSettings s_Instance;

        /// <summary>Raised whenever the active environment changes.</summary>
        public static event Action<BuildEnvironment> ActiveEnvironmentChanged;

        /// <summary>
        /// The settings asset for this project. The first asset found is used; when the project
        /// has none, one is created at <c>Assets/BuildManagerKit/BuildManagerSettings.asset</c>.
        /// </summary>
        public static BuildManagerSettings Instance
        {
            get
            {
                if (s_Instance != null)
                    return s_Instance;

                s_Instance = FindExisting();
                if (s_Instance == null)
                    s_Instance = CreateDefault();

                return s_Instance;
            }
        }

        /// <summary>The settings asset if one exists, without creating a new one.</summary>
        public static BuildManagerSettings InstanceOrNull => s_Instance != null ? s_Instance : FindExisting();

        /// <summary>Every configured profile.</summary>
        public IReadOnlyList<BuildTargetProfile> Profiles => m_Profiles;

        /// <summary>Every configured environment, sorted for display.</summary>
        public IReadOnlyList<BuildEnvironment> Environments => m_Environments;

        /// <summary>Every configured queue.</summary>
        public IReadOnlyList<BuildQueue> Queues => m_Queues;

        /// <summary>
        /// Assets published for every environment. An environment listing the same key wins, the
        /// same way its actions override the global ones.
        /// </summary>
        public IReadOnlyList<EnvironmentAssetEntry> DefaultConfigAssets => m_DefaultConfigAssets;

        /// <summary>
        /// Steps that run whenever any environment is activated, ahead of that environment's own
        /// activation steps. Use this for work that is identical across environments.
        /// </summary>
        public IReadOnlyList<BuildStep> GlobalOnActivateSteps => m_GlobalOnActivateSteps;

        /// <summary>Steps that run before every build, ahead of the environment steps.</summary>
        public IReadOnlyList<BuildStep> GlobalPreBuildSteps => m_GlobalPreBuildSteps;

        /// <summary>Steps that run after every build, after the environment steps.</summary>
        public IReadOnlyList<BuildStep> GlobalPostBuildSteps => m_GlobalPostBuildSteps;

        /// <summary>Restore player settings when a build finishes.</summary>
        public bool RestoreSettingsAfterBuild => m_RestoreSettingsAfterBuild;

        /// <summary>Generate the runtime <see cref="BuildInfo"/> asset.</summary>
        public bool WriteBuildInfoAsset => m_WriteBuildInfoAsset;

        /// <summary>Generate the runtime <see cref="EnvironmentAssets"/> asset.</summary>
        public bool WriteEnvironmentAssets => m_WriteEnvironmentAssets;

        /// <summary>Write <c>build_manifest.json</c> next to the output.</summary>
        public bool WriteBuildManifest => m_WriteBuildManifest;

        /// <summary>Persist the text log of every run.</summary>
        public bool WriteLogFiles => m_WriteLogFiles;

        /// <summary>Project relative folder run logs are written to.</summary>
        public string LogFolder => string.IsNullOrWhiteSpace(m_LogFolder) ? "Logs/BuildManagerKit" : m_LogFolder;

        /// <summary>How many runs the history keeps.</summary>
        public int HistoryLimit => m_HistoryLimit;

        /// <summary>Fail the build when no scenes are resolved.</summary>
        public bool FailOnEmptySceneList => m_FailOnEmptySceneList;

        /// <summary>Ask before building from the Editor UI.</summary>
        public bool ConfirmBeforeBuilding => m_ConfirmBeforeBuilding;

        /// <summary>Open the output folder after a successful Editor build.</summary>
        public bool RevealOutputOnSuccess => m_RevealOutputOnSuccess;

        /// <summary>
        /// The environment currently applied to the Editor. Setting it only stores the reference;
        /// use <see cref="EnvironmentManager.Activate"/> to actually apply defines and overrides.
        /// </summary>
        public BuildEnvironment ActiveEnvironment
        {
            get => m_ActiveEnvironment;
            internal set
            {
                if (m_ActiveEnvironment == value)
                    return;

                m_ActiveEnvironment = value;
                Save();
                ActiveEnvironmentChanged?.Invoke(value);
            }
        }

        /// <summary>
        /// The settings shared by every environment: product and company name, bundle identifier,
        /// icon, the development build flag, shared runtime variables and versioning.
        ///
        /// This is the "configure it in one place" half of environments — each environment states
        /// only its differences. See <see cref="ConfigResolver"/> for the precedence rules.
        /// </summary>
        public CommonBuildConfig Common => m_Common;

        internal List<BuildTargetProfile> ProfilesMutable => m_Profiles;
        internal List<BuildEnvironment> EnvironmentsMutable => m_Environments;
        internal List<BuildQueue> QueuesMutable => m_Queues;
        internal List<EnvironmentAssetEntry> DefaultConfigAssetsMutable => m_DefaultConfigAssets;
        internal List<BuildStep> GlobalOnActivateStepsMutable => m_GlobalOnActivateSteps;
        internal List<BuildStep> GlobalPreBuildStepsMutable => m_GlobalPreBuildSteps;
        internal List<BuildStep> GlobalPostBuildStepsMutable => m_GlobalPostBuildSteps;

        /// <summary>Finds a profile by identifier, case-insensitively. Falls back to display name.</summary>
        public BuildTargetProfile FindProfile(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName))
                return null;

            var needle = idOrName.Trim();

            return m_Profiles.FirstOrDefault(profile =>
                       profile != null && string.Equals(profile.Id, needle, StringComparison.OrdinalIgnoreCase))
                   ?? m_Profiles.FirstOrDefault(profile =>
                       profile != null && string.Equals(profile.DisplayName, needle, StringComparison.OrdinalIgnoreCase))
                   ?? m_Profiles.FirstOrDefault(profile =>
                       profile != null && string.Equals(profile.name, needle, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Finds an environment by identifier, case-insensitively. Falls back to display name.</summary>
        public BuildEnvironment FindEnvironment(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName))
                return null;

            var needle = idOrName.Trim();

            return m_Environments.FirstOrDefault(environment =>
                       environment != null &&
                       string.Equals(environment.Id, needle, StringComparison.OrdinalIgnoreCase))
                   ?? m_Environments.FirstOrDefault(environment =>
                       environment != null &&
                       string.Equals(environment.DisplayName, needle, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Finds a queue by identifier, case-insensitively. Falls back to display name.</summary>
        public BuildQueue FindQueue(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName))
                return null;

            var needle = idOrName.Trim();

            return m_Queues.FirstOrDefault(queue =>
                       queue != null && string.Equals(queue.id, needle, StringComparison.OrdinalIgnoreCase))
                   ?? m_Queues.FirstOrDefault(queue =>
                       queue != null && string.Equals(queue.displayName, needle, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Environments in display order.
        ///
        /// The order of <see cref="Environments"/> is authoritative and is what drag and drop in
        /// the Environments tab rearranges. Every consumer — the main toolbar dropdown, the Scene
        /// view overlay, the window header, the dashboard buttons, the CLI listing and
        /// <see cref="EnvironmentManager.ActivateNext"/> — reads this, so one drag reorders them
        /// all.
        /// </summary>
        public IEnumerable<BuildEnvironment> GetSortedEnvironments() =>
            m_Environments.Where(environment => environment != null);

        /// <summary>
        /// Moves an environment within <see cref="Environments"/>, which is what the display order
        /// is taken from.
        /// </summary>
        /// <param name="fromIndex">Current index.</param>
        /// <param name="toIndex">Index to move it to.</param>
        /// <returns>False when either index is out of range or the move is a no-op.</returns>
        public bool MoveEnvironment(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= m_Environments.Count ||
                toIndex < 0 || toIndex >= m_Environments.Count ||
                fromIndex == toIndex)
                return false;

            var environment = m_Environments[fromIndex];
            m_Environments.RemoveAt(fromIndex);
            m_Environments.Insert(toIndex, environment);

            Save();
            return true;
        }

        /// <summary>Profiles that are enabled, in configured order.</summary>
        public IEnumerable<BuildTargetProfile> GetEnabledProfiles() =>
            m_Profiles.Where(profile => profile != null && profile.Enabled);

        /// <summary>
        /// Pulls in every profile and environment asset in the project that is not registered yet.
        /// Called when the window opens so hand-created assets show up without extra clicks.
        /// </summary>
        public int DiscoverAssets()
        {
            var added = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(BuildTargetProfile)))
            {
                var profile = AssetDatabase.LoadAssetAtPath<BuildTargetProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile != null && !m_Profiles.Contains(profile))
                {
                    m_Profiles.Add(profile);
                    added++;
                }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(BuildEnvironment)))
            {
                var environment =
                    AssetDatabase.LoadAssetAtPath<BuildEnvironment>(AssetDatabase.GUIDToAssetPath(guid));
                if (environment != null && !m_Environments.Contains(environment))
                {
                    m_Environments.Add(environment);
                    added++;
                }
            }

            m_Profiles.RemoveAll(profile => profile == null);
            m_Environments.RemoveAll(environment => environment == null);

            if (added > 0)
                Save();

            return added;
        }

        /// <summary>Marks the asset dirty and flushes it to disk.</summary>
        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
        }

        private static BuildManagerSettings FindExisting()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(BuildManagerSettings));
            foreach (var guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<BuildManagerSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                    return asset;
            }

            return null;
        }

        private static BuildManagerSettings CreateDefault()
        {
            ProjectPaths.EnsureAssetFolder(ProjectPaths.DefaultSettingsFolder);

            var asset = CreateInstance<BuildManagerSettings>();
            var path = ProjectPaths.DefaultSettingsFolder + "/BuildManagerSettings.asset";

            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();

            Debug.Log($"[BuildManagerKit] Created settings asset at {path}.");
            return asset;
        }

        private void OnValidate()
        {
            m_Common ??= new CommonBuildConfig();
            m_Common.variables ??= new List<BuildVariable>();
            m_Common.versioning ??= new VersioningConfig();
            m_Profiles ??= new List<BuildTargetProfile>();
            m_Environments ??= new List<BuildEnvironment>();
            m_Queues ??= new List<BuildQueue>();
            m_DefaultConfigAssets ??= new List<EnvironmentAssetEntry>();
            m_GlobalOnActivateSteps ??= new List<BuildStep>();
            m_GlobalPreBuildSteps ??= new List<BuildStep>();
            m_GlobalPostBuildSteps ??= new List<BuildStep>();
        }
    }
}
