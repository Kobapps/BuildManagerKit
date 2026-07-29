using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// A reusable recipe for building one platform: target, scenes, output location, player
    /// settings, signing and the pre/post build actions specific to that platform.
    ///
    /// A profile is intentionally environment agnostic — the same <c>Android</c> profile is used
    /// for dev, stage and prod. The environment supplies the defines, identifiers and runtime
    /// variables.
    /// </summary>
    [CreateAssetMenu(menuName = "Build Manager Kit/Build Profile", fileName = "Profile_New", order = 100)]
    public sealed class BuildTargetProfile : ScriptableObject, ISerializationCallbackReceiver
    {
        [Serializable]
        public sealed class AndroidOptions
        {
            [Tooltip("Build an .aab App Bundle instead of an .apk.")]
            public bool buildAppBundle;

            [Tooltip("Split the binary into APK + OBB (or base + asset packs for App Bundles).")]
            public bool splitApplicationBinary;

            public bool overrideArchitectures;
            public AndroidArchitecture architectures = AndroidArchitecture.ARM64;

            [Tooltip("Sign the build with a custom keystore. Passwords are never stored in the asset.")]
            public bool useCustomKeystore;

            [Tooltip("Keystore path relative to the project root, or absolute.")]
            public string keystorePath = string.Empty;

            public string keyaliasName = string.Empty;

            [Tooltip("Environment variable that holds the keystore password (e.g. ANDROID_KEYSTORE_PASS).")]
            public string keystorePasswordEnvVar = "ANDROID_KEYSTORE_PASS";

            [Tooltip("Environment variable that holds the key alias password (e.g. ANDROID_KEYALIAS_PASS).")]
            public string keyaliasPasswordEnvVar = "ANDROID_KEYALIAS_PASS";
        }

        [Serializable]
        public sealed class IosOptions
        {
            [Tooltip("Apple Developer Team ID used for the generated Xcode project.")]
            public bool overrideTeamId;

            public string appleDeveloperTeamId = string.Empty;

            [Tooltip("Append to the existing Xcode project instead of replacing it.")]
            public bool appendProject;
        }

        [Serializable]
        public sealed class PlayerOverrides
        {
            public bool overrideScriptingBackend;
            public ScriptingImplementation scriptingBackend = ScriptingImplementation.IL2CPP;

            public bool overrideIl2CppConfiguration;
            public Il2CppCompilerConfiguration il2CppConfiguration = Il2CppCompilerConfiguration.Release;

            public bool overrideStrippingLevel;
            public ManagedStrippingLevel strippingLevel = ManagedStrippingLevel.Low;
        }

        [Header("Identity")]
        [Tooltip("Stable identifier used by the command line (-bmkProfile).")]
        [SerializeField] private string m_Id = "windows";

        [SerializeField] private string m_DisplayName = "Windows 64-bit";

        [TextArea(1, 4)]
        [SerializeField] private string m_Description = string.Empty;

        [Tooltip("Disabled profiles are skipped by 'Build All' and by queues.")]
        [SerializeField] private bool m_ProfileEnabled = true;

        [Header("Target")]
        [SerializeField] private BuildTarget m_Target = BuildTarget.StandaloneWindows64;

        [Tooltip("Player or dedicated Server. Only used by the Standalone targets.")]
        [SerializeField] private StandaloneBuildSubtarget m_StandaloneSubtarget = StandaloneBuildSubtarget.Player;

        [Header("Scenes")]
        [SerializeField] private SceneSource m_SceneSource = SceneSource.EditorBuildSettings;

        [SerializeField] private List<SceneAsset> m_Scenes = new List<SceneAsset>();

        [Header("Output")]
        [Tooltip("Folder the build is written to. Supports tokens, see the Tokens tab of the Build Manager window.")]
        [SerializeField] private string m_OutputDirectoryTemplate = "{projectRoot}/Builds/{env}/{target}/{version}+{buildNumber}";

        [Tooltip("File or folder name of the player. Leave empty for the platform default.")]
        [SerializeField] private string m_ExecutableNameTemplate = "{productName}";

        [Header("Build Options")]
        [SerializeField] private bool m_DevelopmentBuild;
        [SerializeField] private bool m_AutoConnectProfiler;
        [SerializeField] private bool m_DeepProfiling;
        [SerializeField] private bool m_ScriptDebugging;
        [SerializeField] private bool m_StrictMode = true;
        [SerializeField] private bool m_CleanBuildCache;
        [SerializeField] private bool m_DetailedBuildReport = true;
        [SerializeField] private BuildCompression m_Compression = BuildCompression.Default;

        [Tooltip("Extra scripting defines applied on top of the environment defines.")]
        [SerializeField] private string[] m_ExtraScriptingDefines = Array.Empty<string>();

        [Header("Player Settings")]
        [SerializeField] private PlayerOverrides m_PlayerOverrides = new PlayerOverrides();
        [SerializeField] private AndroidOptions m_Android = new AndroidOptions();
        [SerializeField] private IosOptions m_Ios = new IosOptions();

        [Header("Versioning")]
        [Tooltip("Version this profile differently from the project's common configuration. Off inherits the "
                 + "environment's versioning, which normally comes from the base environment.")]
        [SerializeField] private bool m_OverrideVersioning;

        [SerializeField] private VersioningConfig m_Versioning = new VersioningConfig();

        // Versioning used to live in five flat fields on the profile. They are kept, hidden, purely
        // so an asset authored before 1.2 can be folded into m_Versioning once — dropping them would
        // silently reset a project's build counters to 1.
        [SerializeField, HideInInspector] private bool m_VersioningMigrated;
        [SerializeField, HideInInspector] private VersionSource m_VersionSource = VersionSource.PlayerSettings;
        [SerializeField, HideInInspector] private string m_Version = "1.0.0";
        [SerializeField, HideInInspector] private string m_VersionFilePath = "version.txt";

        [SerializeField, HideInInspector]
        private BuildNumberPolicy m_BuildNumberPolicy = BuildNumberPolicy.AutoIncrementOnSuccess;

        [SerializeField, HideInInspector] private int m_BuildNumber = 1;

        [Header("Environments")]
        [Tooltip("Leave empty to allow every environment.")]
        [SerializeField] private List<BuildEnvironment> m_AllowedEnvironments = new List<BuildEnvironment>();

        [Tooltip("Used when no environment is given on the command line or in the UI.")]
        [SerializeField] private BuildEnvironment m_DefaultEnvironment;

        [Header("Actions")]
        [Tooltip("Runs before this profile builds, after the global and environment steps.")]
        [SerializeReference] private List<BuildStep> m_PreBuildSteps = new List<BuildStep>();

        [Tooltip("Runs after this profile builds, before the environment and global steps.")]
        [SerializeReference] private List<BuildStep> m_PostBuildSteps = new List<BuildStep>();

        /// <summary>Stable identifier, falls back to the asset name.</summary>
        public string Id => string.IsNullOrWhiteSpace(m_Id) ? name : m_Id.Trim();

        /// <summary>Name shown in the UI, falls back to <see cref="Id"/>.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(m_DisplayName) ? Id : m_DisplayName;

        /// <summary>Free form description shown in the Editor window.</summary>
        public string Description => m_Description;

        /// <summary>Disabled profiles are skipped by batch operations.</summary>
        public bool Enabled
        {
            get => m_ProfileEnabled;
            set => m_ProfileEnabled = value;
        }

        /// <summary>The platform this profile builds.</summary>
        public BuildTarget Target => m_Target;

        /// <summary>Player or dedicated server, honoured by the Standalone targets only.</summary>
        public StandaloneBuildSubtarget StandaloneSubtarget => m_StandaloneSubtarget;

        /// <summary>The build target group derived from <see cref="Target"/>.</summary>
        public BuildTargetGroup TargetGroup => BuildPipeline.GetBuildTargetGroup(m_Target);

        /// <summary>The named build target used by the modern PlayerSettings API.</summary>
        public NamedBuildTarget NamedTarget => BuildTargetUtility.GetNamedBuildTarget(m_Target, m_StandaloneSubtarget);

        /// <summary>Where the scene list comes from.</summary>
        public SceneSource SceneSource => m_SceneSource;

        /// <summary>Raw output directory template, before token substitution.</summary>
        public string OutputDirectoryTemplate => m_OutputDirectoryTemplate;

        /// <summary>Raw executable name template, before token substitution.</summary>
        public string ExecutableNameTemplate => m_ExecutableNameTemplate;

        /// <summary>True when the profile requests a development player.</summary>
        public bool DevelopmentBuild => m_DevelopmentBuild;

        /// <summary>Compression mode requested for the player data.</summary>
        public BuildCompression Compression => m_Compression;

        /// <summary>Extra defines applied on top of the environment defines.</summary>
        public IEnumerable<string> ExtraScriptingDefines =>
            m_ExtraScriptingDefines.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim());

        /// <summary>Scripting backend / IL2CPP / stripping overrides.</summary>
        public PlayerOverrides Player => m_PlayerOverrides;

        /// <summary>Android specific options including signing.</summary>
        public AndroidOptions Android => m_Android;

        /// <summary>iOS specific options.</summary>
        public IosOptions Ios => m_Ios;

        /// <summary>
        /// True when this profile versions differently from the project's common configuration.
        /// </summary>
        public bool OverridesVersioning => m_OverrideVersioning;

        /// <summary>
        /// This profile's versioning block. Only in effect when <see cref="OverridesVersioning"/> is
        /// true — use <see cref="ConfigResolver.ResolveVersioning"/> to get the block a run uses.
        /// </summary>
        public VersioningConfig Versioning => m_Versioning;

        /// <summary>Where this profile's version string comes from when it overrides versioning.</summary>
        public VersionSource VersionSource => m_Versioning.source;

        /// <summary>Explicit version used when <see cref="VersionSource"/> is <c>Profile</c>.</summary>
        public string Version => m_Versioning.version;

        /// <summary>Project relative path of the version file.</summary>
        public string VersionFilePath => m_Versioning.versionFilePath;

        /// <summary>How this profile's build number is produced when it overrides versioning.</summary>
        public BuildNumberPolicy BuildNumberPolicy => m_Versioning.buildNumberPolicy;

        /// <summary>The stored build counter.</summary>
        public int BuildNumber
        {
            get => m_Versioning.buildNumber;
            internal set => m_Versioning.buildNumber = value;
        }

        /// <summary>Environments this profile is allowed to build with. Empty means all.</summary>
        public IReadOnlyList<BuildEnvironment> AllowedEnvironments => m_AllowedEnvironments;

        /// <summary>Environment used when none is supplied explicitly.</summary>
        public BuildEnvironment DefaultEnvironment => m_DefaultEnvironment;

        /// <summary>Steps executed right before the player build.</summary>
        public IReadOnlyList<BuildStep> PreBuildSteps => m_PreBuildSteps;

        /// <summary>Steps executed right after the player build.</summary>
        public IReadOnlyList<BuildStep> PostBuildSteps => m_PostBuildSteps;

        internal List<BuildStep> PreBuildStepsMutable => m_PreBuildSteps;
        internal List<BuildStep> PostBuildStepsMutable => m_PostBuildSteps;

        /// <summary>True when <paramref name="environment"/> may be used with this profile.</summary>
        public bool SupportsEnvironment(BuildEnvironment environment)
        {
            if (environment == null)
                return false;

            return m_AllowedEnvironments == null
                   || m_AllowedEnvironments.Count == 0
                   || m_AllowedEnvironments.Contains(environment);
        }

        /// <summary>Resolves the scene paths this profile builds, in order.</summary>
        public string[] ResolveScenePaths()
        {
            if (m_SceneSource == SceneSource.Custom)
            {
                return m_Scenes
                    .Where(scene => scene != null)
                    .Select(AssetDatabase.GetAssetPath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .ToArray();
            }

            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path))
                .Select(scene => scene.path)
                .ToArray();
        }

        /// <summary>Translates the profile flags into Unity's <see cref="BuildOptions"/>.</summary>
        public BuildOptions ResolveBuildOptions(bool developmentBuild)
        {
            var options = BuildOptions.None;

            if (developmentBuild)
            {
                options |= BuildOptions.Development;

                if (m_AutoConnectProfiler)
                    options |= BuildOptions.ConnectWithProfiler;

                if (m_DeepProfiling)
                    options |= BuildOptions.EnableDeepProfilingSupport;

                if (m_ScriptDebugging)
                    options |= BuildOptions.AllowDebugging;
            }

            if (m_StrictMode)
                options |= BuildOptions.StrictMode;

            if (m_CleanBuildCache)
                options |= BuildOptions.CleanBuildCache;

            if (m_DetailedBuildReport)
                options |= BuildOptions.DetailedBuildReport;

            switch (m_Compression)
            {
                case BuildCompression.Lz4:
                    options |= BuildOptions.CompressWithLz4;
                    break;
                case BuildCompression.Lz4HC:
                    options |= BuildOptions.CompressWithLz4HC;
                    break;
            }

            if (m_Target == BuildTarget.iOS && m_Ios.appendProject)
                options |= BuildOptions.AcceptExternalModificationsToPlayer;

            return options;
        }

        /// <summary>
        /// A fresh instance has nothing to migrate: it is a new profile, not a pre-1.2 asset, so it
        /// starts out taking versioning from the common configuration.
        ///
        /// <see cref="Awake"/> runs for an object built by <c>CreateInstance</c> — including the
        /// Assets ▸ Create menu — while a profile loaded from disk arrives through
        /// <see cref="OnAfterDeserialize"/>. That is the only reliable way to tell the two apart:
        /// their field values are identical.
        /// </summary>
        private void Awake() => m_VersioningMigrated = true;

        /// <inheritdoc />
        public void OnBeforeSerialize()
        {
        }

        /// <summary>
        /// Migrates a profile that has just been read from disk. Deserialization is what identifies a
        /// pre-1.2 asset, and it is also the moment before anything can read the block.
        /// </summary>
        public void OnAfterDeserialize() => MigrateVersioning();

        /// <summary>
        /// Folds a pre-1.2 profile's flat versioning fields into <see cref="Versioning"/>, once.
        ///
        /// A profile that used to carry its own version source and counter keeps doing exactly that,
        /// so the migration turns the override on: taking the common configuration instead would
        /// change what the next build stamps. Only brand new profiles start out sharing it.
        ///
        /// Called during deserialization, so it touches nothing but its own fields — no
        /// <c>AssetDatabase</c>, no <c>SetDirty</c>. The flag reaches disk with the next save of the
        /// asset, and until then the migration simply produces the same result again.
        /// </summary>
        internal void MigrateVersioning()
        {
            if (m_VersioningMigrated)
                return;

            m_VersioningMigrated = true;
            m_OverrideVersioning = true;
            m_Versioning ??= new VersioningConfig();

            m_Versioning.manageVersion = true;
            m_Versioning.manageBuildNumber = true;
            m_Versioning.version = m_Version;
            m_Versioning.versionFilePath = m_VersionFilePath;
            m_Versioning.buildNumberPolicy = m_BuildNumberPolicy;
            m_Versioning.buildNumber = Mathf.Max(0, m_BuildNumber);

            // The version file used to be one of the sources; it is a toggle of its own now, so the
            // source keeps its meaning as "where the version comes from when no file is involved".
            if (m_VersionSource == VersionSource.VersionFile)
            {
                m_Versioning.useVersionFile = true;
                m_Versioning.source = VersionSource.PlayerSettings;
            }
            else
            {
                m_Versioning.source = m_VersionSource;
            }
        }

        /// <summary>
        /// Marks a profile as already migrated, so it starts out taking versioning from the common
        /// configuration instead of being treated as a pre-1.2 asset. Used by the code that creates
        /// profiles; <see cref="Awake"/> covers everything else.
        /// </summary>
        internal void SkipVersioningMigration()
        {
            m_VersioningMigrated = true;
            m_OverrideVersioning = false;
        }

        private void OnValidate()
        {
            m_Scenes ??= new List<SceneAsset>();
            m_AllowedEnvironments ??= new List<BuildEnvironment>();
            m_PreBuildSteps ??= new List<BuildStep>();
            m_PostBuildSteps ??= new List<BuildStep>();
            m_Versioning ??= new VersioningConfig();
            m_Versioning.buildNumber = Mathf.Max(0, m_Versioning.buildNumber);

            if (string.IsNullOrWhiteSpace(m_Id))
                m_Id = BuildTokens.Sanitize(name).ToLowerInvariant();
        }
    }
}
