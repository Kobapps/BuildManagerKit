using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Resolves the settings that are the same for most environments — company, product name, bundle
    /// identifier, icon, runtime variables, versioning — against the project's
    /// <see cref="CommonBuildConfig"/>.
    ///
    /// The common configuration lives on the settings asset and is edited at the top of the
    /// Environments tab. Every environment inherits it and overrides only what actually differs, so a
    /// company rename is one edit rather than one per flavour. The override switches are the ones each
    /// environment already has: flipping one on means "this environment is different".
    ///
    /// Precedence matches the rest of the kit — the most specific level wins: profile over
    /// environment over common.
    /// </summary>
    public static class ConfigResolver
    {
        /// <summary>The project's common configuration, or null when there is no settings asset.</summary>
        public static CommonBuildConfig Common(BuildManagerSettings settings) =>
            settings != null ? settings.Common : null;

        /// <summary>Product name for a build, or null when neither level sets one.</summary>
        public static string ResolveProductName(BuildManagerSettings settings, BuildEnvironment environment) =>
            First(environment?.ProductNameOverride, Common(settings)?.ProductNameOverride);

        /// <summary>Company name for a build, or null when neither level sets one.</summary>
        public static string ResolveCompanyName(BuildManagerSettings settings, BuildEnvironment environment) =>
            First(environment?.CompanyNameOverride, Common(settings)?.CompanyNameOverride);

        /// <summary>Bundle/package identifier for a build, or null when neither level sets one.</summary>
        public static string ResolveApplicationIdentifier(
            BuildManagerSettings settings,
            BuildEnvironment environment) =>
            First(environment?.ApplicationIdentifierOverride,
                Common(settings)?.ApplicationIdentifierOverride);

        /// <summary>Application icon for a build, or null when neither level replaces it.</summary>
        public static Texture2D ResolveApplicationIcon(BuildManagerSettings settings, BuildEnvironment environment)
        {
            var own = environment != null ? environment.ApplicationIconOverride : null;
            if (own != null)
                return own;

            var common = Common(settings);
            return common != null ? common.ApplicationIconOverride : null;
        }

        /// <summary>
        /// Development build override for a build. An environment that leaves it on
        /// <see cref="OptionalBool.Inherit"/> falls back to the common configuration's answer.
        /// </summary>
        public static OptionalBool ResolveForceDevelopmentBuild(
            BuildManagerSettings settings,
            BuildEnvironment environment)
        {
            if (environment != null && environment.ForceDevelopmentBuild != OptionalBool.Inherit)
                return environment.ForceDevelopmentBuild;

            var common = Common(settings);
            return common != null ? common.forceDevelopmentBuild : OptionalBool.Inherit;
        }

        /// <summary>
        /// Runtime variables for a build: the shared pairs with the environment's own layered on top,
        /// so a common <c>api_url</c> is declared once and only the environments that differ restate
        /// it.
        /// </summary>
        public static List<BuildVariable> ResolveVariables(
            BuildManagerSettings settings,
            BuildEnvironment environment)
        {
            var byKey = new Dictionary<string, BuildVariable>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            void Apply(IReadOnlyList<BuildVariable> variables)
            {
                if (variables == null)
                    return;

                foreach (var variable in variables)
                {
                    if (string.IsNullOrWhiteSpace(variable.key))
                        continue;

                    var key = variable.key.Trim();

                    if (!byKey.ContainsKey(key))
                        order.Add(key);

                    byKey[key] = new BuildVariable(key, variable.value ?? string.Empty);
                }
            }

            Apply(Common(settings)?.variables);
            Apply(environment?.Variables);

            var result = new List<BuildVariable>(order.Count);
            foreach (var key in order)
                result.Add(byKey[key]);

            return result;
        }

        /// <summary>
        /// The versioning block a run uses: the profile's when it versions itself, otherwise the
        /// environment's, otherwise the common one. Falls back to a block that manages nothing only
        /// when there is no settings asset at all.
        /// </summary>
        /// <param name="settings">Settings asset holding the common configuration.</param>
        /// <param name="environment">Environment being built, may be null.</param>
        /// <param name="profile">Profile being built, may be null.</param>
        public static ResolvedVersioning ResolveVersioning(
            BuildManagerSettings settings,
            BuildEnvironment environment,
            BuildTargetProfile profile)
        {
            if (profile != null && profile.OverridesVersioning)
                return new ResolvedVersioning(profile.Versioning, profile, $"profile '{profile.Id}'");

            if (environment != null && environment.OverridesVersioning)
                return new ResolvedVersioning(environment.Versioning, environment,
                    $"environment '{environment.Id}'");

            var common = Common(settings);
            if (common != null)
                return new ResolvedVersioning(common.versioning, settings, "the common configuration");

            return new ResolvedVersioning(VersioningConfig.Unmanaged, null, "nothing");
        }

        /// <summary>
        /// A one line summary of what an environment takes from the common configuration, for the
        /// Environments tab. Returns an empty string when it overrides everything.
        /// </summary>
        public static string DescribeInheritance(BuildManagerSettings settings, BuildEnvironment environment)
        {
            var common = Common(settings);
            if (common == null)
                return string.Empty;

            var inherited = new List<string>();

            if (environment?.ProductNameOverride == null && common.ProductNameOverride != null)
                inherited.Add("product name");

            if (environment?.CompanyNameOverride == null && common.CompanyNameOverride != null)
                inherited.Add("company");

            if (environment?.ApplicationIdentifierOverride == null && common.ApplicationIdentifierOverride != null)
                inherited.Add("bundle id");

            if ((environment == null || environment.ApplicationIconOverride == null) &&
                common.ApplicationIconOverride != null)
                inherited.Add("icon");

            if (environment != null && !environment.OverridesVersioning)
                inherited.Add("versioning");

            var variables = ResolveVariables(settings, environment).Count -
                            (environment != null ? CountKeys(environment.Variables) : 0);

            if (variables > 0)
                inherited.Add($"{variables} runtime variable(s)");

            return inherited.Count == 0
                ? string.Empty
                : "Takes " + string.Join(", ", inherited) + " from the common configuration.";
        }

        private static int CountKeys(IReadOnlyList<BuildVariable> variables)
        {
            if (variables == null)
                return 0;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var variable in variables)
            {
                if (!string.IsNullOrWhiteSpace(variable.key))
                    seen.Add(variable.key.Trim());
            }

            return seen.Count;
        }

        private static string First(string own, string inherited) => own ?? inherited;
    }
}
