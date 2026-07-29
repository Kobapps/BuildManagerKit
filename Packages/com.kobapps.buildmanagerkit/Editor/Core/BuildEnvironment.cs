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
    public sealed class BuildEnvironment : ScriptableObject
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
        [SerializeField] private bool m_OverrideProductName;
        [SerializeField] private string m_ProductName = string.Empty;

        [SerializeField] private bool m_OverrideCompanyName;
        [SerializeField] private string m_CompanyName = string.Empty;

        [Tooltip("Overrides the bundle/package identifier for the target being built, e.g. com.studio.game.dev.")]
        [SerializeField] private bool m_OverrideApplicationIdentifier;
        [SerializeField] private string m_ApplicationIdentifier = string.Empty;

        [Tooltip("Forces development builds on or off regardless of what the profile asks for.")]
        [SerializeField] private OptionalBool m_ForceDevelopmentBuild = OptionalBool.Inherit;

        [Tooltip("Replace the application icon while this environment is active — a badged or tinted icon "
                 + "makes it obvious which flavour is installed on a device. Restored with the rest of the "
                 + "player settings when a build finishes.")]
        [SerializeField] private bool m_OverrideApplicationIcon;

        [Tooltip("Texture used for every application icon slot of the target being built.")]
        [SerializeField] private Texture2D m_ApplicationIcon;

        [Header("Runtime Variables")]
        [Tooltip("Baked into BuildInfo and readable at runtime through BuildInfo.Current.GetVariable(key).")]
        [SerializeField] private List<BuildVariable> m_Variables = new List<BuildVariable>();

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

        /// <summary>Override for <c>PlayerSettings.productName</c>, or null when not overridden.</summary>
        public string ProductNameOverride => m_OverrideProductName ? m_ProductName : null;

        /// <summary>Override for <c>PlayerSettings.companyName</c>, or null when not overridden.</summary>
        public string CompanyNameOverride => m_OverrideCompanyName ? m_CompanyName : null;

        /// <summary>Override for the application identifier, or null when not overridden.</summary>
        public string ApplicationIdentifierOverride =>
            m_OverrideApplicationIdentifier ? m_ApplicationIdentifier : null;

        /// <summary>Development build override applied on top of the profile setting.</summary>
        public OptionalBool ForceDevelopmentBuild => m_ForceDevelopmentBuild;

        /// <summary>
        /// Application icon applied while this environment is active, or null when the project
        /// icon is kept.
        /// </summary>
        public Texture2D ApplicationIconOverride => m_OverrideApplicationIcon ? m_ApplicationIcon : null;

        /// <summary>Runtime key/value pairs baked into <see cref="BuildInfo"/>.</summary>
        public IReadOnlyList<BuildVariable> Variables => m_Variables;

        /// <summary>
        /// Assets this environment publishes, read at runtime through
        /// <see cref="EnvironmentAssets"/>. Overrides any project-wide default sharing a key.
        /// </summary>
        public IReadOnlyList<EnvironmentAssetEntry> ConfigAssets => m_ConfigAssets;

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

        internal List<BuildStep> OnActivateStepsMutable => m_OnActivateSteps;
        internal List<BuildStep> PreBuildStepsMutable => m_PreBuildSteps;
        internal List<BuildStep> PostBuildStepsMutable => m_PostBuildSteps;

        private void OnValidate()
        {
            m_Variables ??= new List<BuildVariable>();
            m_ConfigAssets ??= new List<EnvironmentAssetEntry>();
            m_OnActivateSteps ??= new List<BuildStep>();
            m_PreBuildSteps ??= new List<BuildStep>();
            m_PostBuildSteps ??= new List<BuildStep>();

            if (string.IsNullOrWhiteSpace(m_Id))
                m_Id = BuildTokens.Sanitize(name).ToLowerInvariant();
        }
    }
}
