using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;

namespace BuildManagerKit.Editor
{
    /// <summary>Which part of a <c>major.minor.patch</c> version to increment.</summary>
    public enum VersionComponent
    {
        Major = 0,
        Minor = 1,
        Patch = 2
    }

    /// <summary>
    /// Resolves and applies the version string and build number of a run, according to the
    /// <see cref="VersioningConfig"/> that won — the profile's, the environment's or the common
    /// configuration's, as decided by <see cref="ConfigResolver.ResolveVersioning"/>.
    ///
    /// A block that does not manage the version, or the build number, is honoured: those player
    /// settings are then left exactly as the project has them.
    /// </summary>
    public static class VersionService
    {
        private static readonly Regex k_SemVer =
            new Regex(@"^(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?<suffix>[-+].*)?$",
                RegexOptions.Compiled);

        /// <summary>
        /// Works out the version string for a run. Falls back to the current
        /// <c>PlayerSettings.bundleVersion</c> whenever the configured source yields nothing, and
        /// returns it unchanged when the block does not manage the version at all.
        /// </summary>
        /// <param name="versioning">The versioning block in effect, may be null.</param>
        /// <param name="git">Git state of the run, used by the git tag source.</param>
        /// <param name="log">Optional log for the fallback warnings.</param>
        public static string Resolve(VersioningConfig versioning, GitInfo git, IBuildLog log)
        {
            var fallback = string.IsNullOrWhiteSpace(PlayerSettings.bundleVersion)
                ? "0.1.0"
                : PlayerSettings.bundleVersion;

            if (versioning == null || !versioning.manageVersion)
                return fallback;

            if (versioning.ReadsVersionFile)
            {
                var path = ProjectPaths.MakeAbsolute(versioning.versionFilePath);
                if (!File.Exists(path))
                {
                    log?.Warning($"Version file '{path}' not found, falling back to PlayerSettings ({fallback}).");
                    return fallback;
                }

                var line = File.ReadLines(path).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
                return string.IsNullOrWhiteSpace(line) ? fallback : line.Trim();
            }

            switch (versioning.source)
            {
                case VersionSource.Profile:
                    return string.IsNullOrWhiteSpace(versioning.version) ? fallback : versioning.version.Trim();

                case VersionSource.GitTag:
                {
                    var tag = git != null ? git.Tag : string.Empty;
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        log?.Warning($"No git tag found, falling back to PlayerSettings ({fallback}).");
                        return fallback;
                    }

                    return tag.TrimStart('v', 'V');
                }

                default:
                    return fallback;
            }
        }

        /// <summary>
        /// Works out the numeric build counter for a run. Returns the project's current
        /// <c>versionCode</c> when the block does not manage the build number, so applying it is a
        /// no-op.
        /// </summary>
        /// <param name="versioning">The versioning block in effect, may be null.</param>
        /// <param name="git">Git state of the run, used by the commit count policy.</param>
        public static int ResolveBuildNumber(VersioningConfig versioning, GitInfo git)
        {
            if (versioning == null || !versioning.manageBuildNumber)
                return PlayerSettings.Android.bundleVersionCode;

            switch (versioning.buildNumberPolicy)
            {
                case BuildNumberPolicy.GitCommitCount:
                    return git != null && git.CommitCount > 0 ? git.CommitCount : versioning.buildNumber;

                case BuildNumberPolicy.Timestamp:
                    // Minutes since 2020-01-01 UTC: monotonic, fits comfortably in an int and
                    // stays below the Google Play versionCode ceiling of 2100000000.
                    return (int)(DateTime.UtcNow - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                        .TotalMinutes;

                case BuildNumberPolicy.Manual:
                case BuildNumberPolicy.AutoIncrementOnSuccess:
                default:
                    return Math.Max(1, versioning.buildNumber);
            }
        }

        /// <summary>Writes the resolved version and build number into the player settings.</summary>
        public static void Apply(BuildContext context)
        {
            var versioning = context.Versioning;
            var manageVersion = versioning.manageVersion;
            var manageBuildNumber = versioning.manageBuildNumber;

            if (!manageVersion && !manageBuildNumber)
            {
                context.Log.Info("Versioning is not managed by Build Manager Kit; player settings left as they are.");
                return;
            }

            if (context.DryRun)
            {
                context.Log.Info("[dry run] Would set "
                                 + (manageVersion ? $"version {context.Version}" : "no version")
                                 + (manageBuildNumber ? $" and build number {context.BuildNumber}." : "."));
                return;
            }

            if (manageVersion)
                PlayerSettings.bundleVersion = context.Version;

            if (manageBuildNumber)
            {
                PlayerSettings.Android.bundleVersionCode = context.BuildNumber;
                PlayerSettings.iOS.buildNumber = context.BuildNumber.ToString(CultureInfo.InvariantCulture);
                PlayerSettings.macOS.buildNumber = context.BuildNumber.ToString(CultureInfo.InvariantCulture);
            }

            context.Log.Info(
                (manageVersion ? $"Version {context.Version}" : $"Version {context.Version} (not managed)")
                + (manageBuildNumber ? $" (build {context.BuildNumber})." : " (build number not managed).")
                + $" Source: {context.VersioningOwnerLabel}.");
        }

        /// <summary>
        /// Bumps the stored counter after a successful build, when the winning block asks for it.
        /// The counter is written back to whichever asset supplied the block — the settings asset
        /// holding the common configuration, an environment, or a profile.
        ///
        /// A run whose number was supplied explicitly (<c>-bmkBuildNumber</c>) leaves the counter
        /// alone: that number did not come from the counter, so advancing it would make the stored
        /// value drift away from what was actually shipped.
        /// </summary>
        /// <param name="context">The finished run.</param>
        /// <returns>The new counter value, or the unchanged one.</returns>
        public static int CommitBuildNumber(BuildContext context)
        {
            if (context == null)
                return 0;

            var resolved = context.ResolvedVersioning;
            var versioning = resolved.Config;

            if (!resolved.IsOwned || !versioning.IncrementsBuildNumber || context.BuildNumberWasSupplied)
                return versioning.buildNumber;

            versioning.buildNumber += 1;
            resolved.SaveOwner();

            return versioning.buildNumber;
        }

        /// <summary>
        /// Increments one component of a <c>major.minor.patch</c> version, zeroing the components
        /// to its right and preserving any <c>-suffix</c> or <c>+metadata</c>.
        /// </summary>
        /// <param name="version">Version to bump. Non numeric input is returned unchanged.</param>
        /// <param name="component">Which component to increment.</param>
        public static string Bump(string version, VersionComponent component)
        {
            if (string.IsNullOrWhiteSpace(version))
                return "0.0.1";

            var match = k_SemVer.Match(version.Trim());
            if (!match.Success)
                return version;

            var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
            var minor = match.Groups["minor"].Success
                ? int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture)
                : 0;
            var patch = match.Groups["patch"].Success
                ? int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture)
                : 0;

            switch (component)
            {
                case VersionComponent.Major:
                    major++;
                    minor = 0;
                    patch = 0;
                    break;
                case VersionComponent.Minor:
                    minor++;
                    patch = 0;
                    break;
                default:
                    patch++;
                    break;
            }

            return $"{major}.{minor}.{patch}{match.Groups["suffix"].Value}";
        }

        /// <summary>True when <paramref name="version"/> parses as <c>major[.minor[.patch]]</c>.</summary>
        public static bool IsValid(string version) =>
            !string.IsNullOrWhiteSpace(version) && k_SemVer.IsMatch(version.Trim());

        /// <summary>
        /// Writes a version string back to the version file of <paramref name="versioning"/>. Does
        /// nothing when that block does not use a version file.
        /// </summary>
        public static void WriteVersionFile(VersioningConfig versioning, string version)
        {
            if (versioning == null || !versioning.ReadsVersionFile)
                return;

            var path = ProjectPaths.MakeAbsolute(versioning.versionFilePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, version + Environment.NewLine);
        }

        /// <summary>
        /// Applies the profile's scripting backend, IL2CPP configuration and stripping level
        /// overrides to <paramref name="namedTarget"/>.
        /// </summary>
        public static void ApplyPlayerOverrides(BuildTargetProfile profile, NamedBuildTarget namedTarget,
            IBuildLog log)
        {
            if (profile == null)
                return;

            var overrides = profile.Player;

            if (overrides.overrideScriptingBackend)
            {
                PlayerSettings.SetScriptingBackend(namedTarget, overrides.scriptingBackend);
                log?.Info($"Scripting backend: {overrides.scriptingBackend}.");
            }

            if (overrides.overrideIl2CppConfiguration)
            {
                PlayerSettings.SetIl2CppCompilerConfiguration(namedTarget, overrides.il2CppConfiguration);
                log?.Info($"IL2CPP configuration: {overrides.il2CppConfiguration}.");
            }

            if (overrides.overrideStrippingLevel)
            {
                PlayerSettings.SetManagedStrippingLevel(namedTarget, overrides.strippingLevel);
                log?.Info($"Managed stripping: {overrides.strippingLevel}.");
            }
        }
    }
}
