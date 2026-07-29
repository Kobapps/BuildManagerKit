using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// The settings that are the same in every environment: product and company name, bundle
    /// identifier, application icon, the development build flag, shared runtime variables and
    /// versioning.
    ///
    /// It lives on <see cref="BuildManagerSettings"/> and is edited at the top of the Environments
    /// tab, so a company rename or a version bump is one edit rather than one per flavour. Each
    /// environment overrides only the fields that actually differ — see <see cref="ConfigResolver"/>
    /// for the precedence.
    ///
    /// An empty field means "Build Manager Kit does not manage this": the project's own player
    /// settings are left exactly as they are, which is what a project that sets the value elsewhere
    /// wants. There is no switch beside each field — filling it in *is* the switch.
    /// </summary>
    [Serializable]
    public sealed class CommonBuildConfig
    {
        [Tooltip("PlayerSettings.productName for every environment. Empty leaves it alone.")]
        public string productName = string.Empty;

        [Tooltip("PlayerSettings.companyName for every environment. Empty leaves it alone.")]
        public string companyName = string.Empty;

        [Tooltip("The bundle/package identifier for every environment, e.g. com.studio.game. "
                 + "Empty leaves it alone.")]
        public string applicationIdentifier = string.Empty;

        [Tooltip("Application icon used by every environment. Empty keeps the project icon.")]
        public Texture2D applicationIcon;

        [Tooltip("Forces development builds on or off for every environment, unless one says otherwise.")]
        public OptionalBool forceDevelopmentBuild = OptionalBool.Inherit;

        [Tooltip("Runtime variables baked into BuildInfo for every environment. An environment declaring "
                 + "the same key overrides the value.")]
        public List<BuildVariable> variables = new List<BuildVariable>();

        [Tooltip("Where the version string and the build number come from, for every environment and "
                 + "profile that does not version itself.")]
        public VersioningConfig versioning = new VersioningConfig();

        /// <summary>Product name for every environment, or null when the field is empty.</summary>
        public string ProductNameOverride => Value(productName);

        /// <summary>Company name for every environment, or null when the field is empty.</summary>
        public string CompanyNameOverride => Value(companyName);

        /// <summary>Bundle identifier for every environment, or null when the field is empty.</summary>
        public string ApplicationIdentifierOverride => Value(applicationIdentifier);

        /// <summary>Application icon for every environment, or null when none is assigned.</summary>
        public Texture2D ApplicationIconOverride => applicationIcon;

        /// <summary>True when any field is actually managed here.</summary>
        public bool IsConfigured =>
            ProductNameOverride != null
            || CompanyNameOverride != null
            || ApplicationIdentifierOverride != null
            || applicationIcon != null
            || forceDevelopmentBuild != OptionalBool.Inherit
            || (variables != null && variables.Count > 0)
            || (versioning != null && (versioning.manageVersion || versioning.manageBuildNumber));

        /// <summary>A one line summary for the cards and the CLI description.</summary>
        public string Describe()
        {
            var parts = new List<string>();

            if (ProductNameOverride != null)
                parts.Add($"product '{productName}'");

            if (CompanyNameOverride != null)
                parts.Add($"company '{companyName}'");

            if (ApplicationIdentifierOverride != null)
                parts.Add($"bundle id '{applicationIdentifier}'");

            if (applicationIcon != null)
                parts.Add("a shared icon");

            if (forceDevelopmentBuild != OptionalBool.Inherit)
                parts.Add($"development build {forceDevelopmentBuild}");

            if (variables != null && variables.Count > 0)
                parts.Add($"{variables.Count} runtime variable(s)");

            parts.Add(versioning != null ? versioning.Describe() : "no versioning");

            return string.Join(" · ", parts);
        }

        /// <summary>Reads one of the shared variables.</summary>
        /// <param name="key">Variable name, case-insensitive.</param>
        /// <param name="fallback">Returned when the key is not declared here.</param>
        public string GetVariable(string key, string fallback = "")
        {
            if (variables == null)
                return fallback;

            for (var i = 0; i < variables.Count; i++)
            {
                if (string.Equals(variables[i].key, key, StringComparison.OrdinalIgnoreCase))
                    return variables[i].value;
            }

            return fallback;
        }

        /// <summary>
        /// A field's contribution: the trimmed text, or null when it is blank. Blank is what "not
        /// managed" looks like now that the switches are gone, so it has to be a null rather than an
        /// empty string that would clear the project's own value.
        /// </summary>
        private static string Value(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
