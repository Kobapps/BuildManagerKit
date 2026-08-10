using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// A named configuration flavour such as <c>dev</c>, <c>stage</c> or <c>prod</c>.
    ///
    /// An environment carries scripting defines, player setting overrides and a set of
    /// key/value variables that are baked into <see cref="BuildInfo"/> so runtime code can read
    /// them. Environments can be activated in the Editor (Tools &gt; Build Manager Kit &gt;
    /// Environment) which applies exactly the same changes a build would apply, so what you play
    /// in the Editor matches what you ship.
    /// </summary>
    [CreateAssetMenu(menuName = "Build Manager Kit/Build Environment", fileName = "Env_New", order = 101)]
    public sealed class BuildEnvironment : ScriptableObject, ISerializationCallbackReceiver
    {
        [Header("Identity")]
        [Tooltip("Stable identifier used by the command line (-bmkEnv) and by BuildInfo.Current.EnvironmentId.")]
        [SerializeField] private string m_Id = "dev";

        [Tooltip("Name shown in the Editor UI.")]
        [SerializeField] private string m_DisplayName = "Development";

        [TextArea(1, 4)]
        [SerializeField] private string m_Description = string.Empty;

        [Tooltip("Accent colour used for this environment throughout the UI.")]
        [SerializeField] private Color m_Color = new Color(0.29f, 0.66f, 0.98f);

        [Tooltip("Ask for confirmation before activating or building this environment. Use it for production.")]
        [SerializeField] private bool m_RequireConfirmation;

        [Header("Scripting Defines")]
        [Tooltip("Automatically add ENV_<ID> (upper case) to the scripting defines.")]
        [SerializeField] private bool m_GenerateEnvironmentDefine = true;

        [Tooltip("Defines added while this environment is active.")]
        [SerializeField] private string[] m_ScriptingDefines = Array.Empty<string>();

        [Tooltip("Defines removed while this environment is active. Applied after the additions.")]
        [SerializeField] private string[] m_RemovedScriptingDefines = Array.Empty<string>();

        [Header("Player Setting Overrides")]
        [Tooltip("Product name for this environment. Empty takes the common configuration's value.")]
        [SerializeField] private string m_ProductName = string.Empty;

        [Tooltip("Company name for this environment. Empty takes the common configuration's value.")]
        [SerializeField] private string m_CompanyName = string.Empty;

        [Tooltip("Bundle/package identifier for the target being built, e.g. com.studio.game.dev. "
                 + "Empty takes the common configuration's value.")]
        [SerializeField] private string m_ApplicationIdentifier = string.Empty;

        [Tooltip("Forces development builds on or off regardless of what the profile asks for. "
                 + "Inherit takes the common configuration's answer.")]
        [SerializeField] private OptionalBool m_ForceDevelopmentBuild = OptionalBool.Inherit;

        [Tooltip("Replace the application icon while this environment is active — a badged or tinted icon "
                 + "makes it obvious which flavour is installed on a device. Fills every icon slot the "
                 + "target ships, including Android's adaptive and round icons and iOS's 1024 marketing "
                 + "icon, so the built app matches. Restored with the rest of the player settings when a "
                 + "build finishes. Empty takes the common configuration's icon.")]
        [SerializeField] private Texture2D m_ApplicationIcon;

        // The overrides used to be a checkbox beside each field. They are kept, hidden, only so an
        // asset authored before 1.2 can be folded into the "a value means an override" rule once:
        // a value left behind by an unchecked box would otherwise start overriding on its own.
        [SerializeField, HideInInspector] private bool m_OverridesMigrated;
        [SerializeField, HideInInspector] private bool m_OverrideProductName;
        [SerializeField, HideInInspector] private bool m_OverrideCompanyName;
        [SerializeField, HideInInspector] private bool m_OverrideApplicationIdentifier;
        [SerializeField, HideInInspector] private bool m_OverrideApplicationIcon;

        [Header("Versioning")]
        [Tooltip("Version this environment differently from the project's common configuration. Off inherits "
                 + "the base environment's versioning.")]
        [SerializeField] private bool m_OverrideVersioning;

        [SerializeField] private VersioningConfig m_Versioning = new VersioningConfig();

        [Header("Runtime Variables")]
        [Tooltip("Baked into BuildInfo and readable at runtime through BuildInfo.Current.GetVariable(key). "
                 + "Merged over the base environment's variables, so a shared value is declared once there.")]
        [SerializeField] private List<BuildVariable> m_Variables = new List<BuildVariable>();

        [Header("Configs")]
        [Tooltip("Typed configuration assets this environment publishes. Read them at runtime with "
                 + "EnvironmentConfigs.Get<YourConfig>() — no key, the type is the address. The same asset "
                 + "can be listed by several environments, which is how a shared value is edited in one "
                 + "place.")]
        [SerializeField] private List<EnvironmentConfig> m_Configs = new List<EnvironmentConfig>();

        [Header("Config Assets")]
        [Tooltip("Assets this environment publishes to runtime code — a tuning ScriptableObject, a JSON "
                 + "TextAsset, an image. Read them with EnvironmentAssets.Current.Get<T>(key). Only the "
                 + "environment being built has its assets referenced, so the others are not included in "
                 + "the player.")]
        [SerializeField] private List<EnvironmentAssetEntry> m_ConfigAssets = new List<EnvironmentAssetEntry>();

        [Header("Actions")]
        [Tooltip("Runs when this environment becomes the active Editor environment.")]
        [SerializeReference] private List<BuildStep> m_OnActivateSteps = new List<BuildStep>();

        [Tooltip("Runs before every build made with this environment, after the global steps.")]
        [SerializeReference] private List<BuildStep> m_PreBuildSteps = new List<BuildStep>();

        [Tooltip("Runs after every build made with this environment, before the global steps.")]
        [SerializeReference] private List<BuildStep> m_PostBuildSteps = new List<BuildStep>();

        /// <summary>Stable identifier, e.g. <c>"prod"</c>. Falls back to the asset name.</summary>
        public string Id => string.IsNullOrWhiteSpace(m_Id) ? name : m_Id.Trim();

        /// <summary>Name shown in the UI. Falls back to <see cref="Id"/>.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(m_DisplayName) ? Id : m_DisplayName;

        /// <summary>Free form description shown in the Editor window.</summary>
        public string Description => m_Description;

        /// <summary>Accent colour used to tint the UI while this environment is active.</summary>
        public Color Color => m_Color;

        /// <summary>When true the UI asks for confirmation before activating or building.</summary>
        public bool RequireConfirmation => m_RequireConfirmation;

        /// <summary>
        /// Override for <c>PlayerSettings.productName</c>, or null when this environment leaves it to
        /// the common configuration. A blank field is what "leave it" looks like.
        /// </summary>
        public string ProductNameOverride => Value(m_ProductName);

        /// <summary>Override for <c>PlayerSettings.companyName</c>, or null when left to the common one.</summary>
        public string CompanyNameOverride => Value(m_CompanyName);

        /// <summary>Override for the application identifier, or null when left to the common one.</summary>
        public string ApplicationIdentifierOverride => Value(m_ApplicationIdentifier);

        /// <summary>Development build override applied on top of the profile setting.</summary>
        public OptionalBool ForceDevelopmentBuild => m_ForceDevelopmentBuild;

        /// <summary>
        /// Application icon applied while this environment is active, or null when the common
        /// configuration's icon — or the project icon — is kept.
        /// </summary>
        public Texture2D ApplicationIconOverride => m_ApplicationIcon;

        /// <summary>
        /// True when this environment versions differently from the common configuration held by the
        /// base environment. On the base environment itself this is what switches the project's
        /// common versioning on.
        /// </summary>
        public bool OverridesVersioning => m_OverrideVersioning;

        /// <summary>
        /// This environment's versioning block. Only in effect when
        /// <see cref="OverridesVersioning"/> is true — use
        /// <see cref="ConfigResolver.ResolveVersioning"/> to get the block a run actually uses.
        /// </summary>
        public VersioningConfig Versioning => m_Versioning;

        /// <summary>
        /// Runtime key/value pairs declared by this environment, before the base environment's are
        /// merged in. Baked into <see cref="BuildInfo"/> via
        /// <see cref="ConfigResolver.ResolveVariables"/>.
        /// </summary>
        public IReadOnlyList<BuildVariable> Variables => m_Variables;

        /// <summary>
        /// Typed configs this environment publishes, read at runtime with
        /// <see cref="EnvironmentConfigs.Get{T}()"/>.
        ///
        /// Nothing stops an asset appearing in several environments' lists, and that is the intended
        /// way to share one: a config listed by dev and stage but not prod is edited once and takes
        /// effect in both, while prod publishes its own or none at all.
        /// </summary>
        public IReadOnlyList<EnvironmentConfig> Configs => m_Configs;

        /// <summary>
        /// Assets this environment publishes, read at runtime through
        /// <see cref="EnvironmentAssets"/>. Overrides any project-wide default sharing a key.
        /// </summary>
        public IReadOnlyList<EnvironmentAssetEntry> ConfigAssets => m_ConfigAssets;

        /// <summary>
        /// The config of type <typeparamref name="T"/> this environment publishes, or null.
        ///
        /// The Editor-side mirror of <see cref="EnvironmentConfigs.Get{T}()"/>: it reads the
        /// environment asset directly, so it answers for any environment rather than only the one
        /// baked into the player.
        /// </summary>
        /// <typeparam name="T">The config type.</typeparam>
        public T GetConfig<T>() where T : EnvironmentConfig
        {
            T assignable = null;

            foreach (var config in m_Configs)
            {
                if (config == null)
                    continue;

                if (config.GetType() == typeof(T))
                    return config as T;

                // Remembered rather than returned: an exact match later in the list is the better
                // answer, and the list is short enough that finishing it costs nothing.
                if (assignable == null && config is T candidate)
                    assignable = candidate;
            }

            return assignable;
        }

        /// <summary>The asset published under <paramref name="key"/>, or null.</summary>
        public UnityEngine.Object GetConfigAsset(string key)
        {
            foreach (var entry in m_ConfigAssets)
            {
                if (string.Equals(entry.key, key, StringComparison.OrdinalIgnoreCase))
                    return entry.asset;
            }

            return null;
        }

        /// <summary>Steps executed when this environment is activated in the Editor.</summary>
        public IReadOnlyList<BuildStep> OnActivateSteps => m_OnActivateSteps;

        /// <summary>Steps executed before every build using this environment.</summary>
        public IReadOnlyList<BuildStep> PreBuildSteps => m_PreBuildSteps;

        /// <summary>Steps executed after every build using this environment.</summary>
        public IReadOnlyList<BuildStep> PostBuildSteps => m_PostBuildSteps;

        /// <summary>
        /// The auto generated define for this environment, e.g. <c>ENV_PROD</c>.
        ///
        /// Sanitised as an identifier rather than as a file name: an id like <c>my-env</c> would
        /// otherwise produce <c>ENV_MY-ENV</c>, which is not a legal preprocessor symbol and
        /// breaks compilation the moment it is applied.
        /// </summary>
        public string EnvironmentDefine
        {
            get
            {
                if (!m_GenerateEnvironmentDefine)
                    return null;

                var identifier = BuildTokens.SanitizeIdentifier(Id).ToUpperInvariant();
                return string.IsNullOrEmpty(identifier) ? null : "ENV_" + identifier;
            }
        }

        /// <summary>Every define this environment adds, including the generated one.</summary>
        public IEnumerable<string> GetAddedDefines()
        {
            var generated = EnvironmentDefine;
            if (!string.IsNullOrEmpty(generated))
                yield return generated;

            foreach (var define in m_ScriptingDefines)
            {
                if (!string.IsNullOrWhiteSpace(define))
                    yield return define.Trim();
            }
        }

        /// <summary>Defines this environment strips from the active define set.</summary>
        public IEnumerable<string> GetRemovedDefines() =>
            m_RemovedScriptingDefines.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim());

        /// <summary>Reads one of the environment variables.</summary>
        public string GetVariable(string key, string fallback = "")
        {
            for (var i = 0; i < m_Variables.Count; i++)
            {
                if (string.Equals(m_Variables[i].key, key, StringComparison.OrdinalIgnoreCase))
                    return m_Variables[i].value;
            }

            return fallback;
        }

        internal List<EnvironmentConfig> ConfigsMutable => m_Configs;
        internal List<BuildStep> OnActivateStepsMutable => m_OnActivateSteps;
        internal List<BuildStep> PreBuildStepsMutable => m_PreBuildSteps;
        internal List<BuildStep> PostBuildStepsMutable => m_PostBuildSteps;

        /// <summary>
        /// A fresh environment has nothing to migrate — its fields are empty, which already means
        /// "take the common value".
        /// </summary>
        private void Awake() => m_OverridesMigrated = true;

        /// <inheritdoc />
        public void OnBeforeSerialize()
        {
        }

        /// <summary>Migrates an environment that has just been read from disk.</summary>
        public void OnAfterDeserialize() => MigrateOverrides();

        /// <summary>
        /// Folds a pre-1.2 environment's override checkboxes into the fields themselves, once.
        ///
        /// A field whose box was unchecked contributed nothing, so it is cleared: leaving the text in
        /// place would turn a value someone had abandoned into a live override the moment the boxes
        /// disappeared. The one case that cannot survive is "override with an empty value", which used
        /// to mean "no product name at all" and now means "take the common one".
        ///
        /// Runs during deserialization, so it touches nothing but its own fields.
        /// </summary>
        internal void MigrateOverrides()
        {
            if (m_OverridesMigrated)
                return;

            m_OverridesMigrated = true;

            if (!m_OverrideProductName)
                m_ProductName = string.Empty;

            if (!m_OverrideCompanyName)
                m_CompanyName = string.Empty;

            if (!m_OverrideApplicationIdentifier)
                m_ApplicationIdentifier = string.Empty;

            if (!m_OverrideApplicationIcon)
                m_ApplicationIcon = null;
        }

        private static string Value(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        private void OnValidate()
        {
            m_Versioning ??= new VersioningConfig();
            m_Variables ??= new List<BuildVariable>();
            m_Configs ??= new List<EnvironmentConfig>();
            m_ConfigAssets ??= new List<EnvironmentAssetEntry>();
            m_OnActivateSteps ??= new List<BuildStep>();
            m_PreBuildSteps ??= new List<BuildStep>();
            m_PostBuildSteps ??= new List<BuildStep>();

            if (string.IsNullOrWhiteSpace(m_Id))
                m_Id = BuildTokens.Sanitize(name).ToLowerInvariant();
        }
    }
}
