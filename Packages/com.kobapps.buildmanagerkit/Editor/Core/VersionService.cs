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
    /// profile's <see cref="VersionSource"/> and <see cref="BuildNumberPolicy"/>.
    /// </summary>
    public static class VersionService
    {
        private static readonly Regex k_SemVer =
            new Regex(@"^(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?<suffix>[-+].*)?$",
                RegexOptions.Compiled);

        /// <summary>
        /// Works out the version string for a run. Falls back to the current
        /// <c>PlayerSettings.bundleVersion</c> whenever the configured source yields nothing.
        /// </summary>
        public static string Resolve(BuildTargetProfile profile, GitInfo git, IBuildLog log)
        {
            var fallback = string.IsNullOrWhiteSpace(PlayerSettings.bundleVersion)
                ? "0.1.0"
                : PlayerSettings.bundleVersion;

            if (profile == null)
                return fallback;

            switch (profile.VersionSource)
            {
                case VersionSource.Profile:
                    return string.IsNullOrWhiteSpace(profile.Version) ? fallback : profile.Version.Trim();

                case VersionSource.VersionFile:
                {
                    var path = ProjectPaths.MakeAbsolute(profile.VersionFilePath);
                    if (!File.Exists(path))
                    {
                        log?.Warning($"Version file '{path}' not found, falling back to PlayerSettings ({fallback}).");
                        return fallback;
                    }

                    var line = File.ReadLines(path).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
                    return string.IsNullOrWhiteSpace(line) ? fallback : line.Trim();
                }

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

        /// <summary>Works out the numeric build counter for a run.</summary>
        public static int ResolveBuildNumber(BuildTargetProfile profile, GitInfo git)
        {
            if (profile == null)
                return PlayerSettings.Android.bundleVersionCode;

            switch (profile.BuildNumberPolicy)
            {
                case BuildNumberPolicy.GitCommitCount:
                    return git != null && git.CommitCount > 0 ? git.CommitCount : profile.BuildNumber;

                case BuildNumberPolicy.Timestamp:
                    // Minutes since 2020-01-01 UTC: monotonic, fits comfortably in an int and
                    // stays below the Google Play versionCode ceiling of 2100000000.
                    return (int)(DateTime.UtcNow - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                        .TotalMinutes;

                case BuildNumberPolicy.Manual:
                case BuildNumberPolicy.AutoIncrementOnSuccess:
                default:
                    return Math.Max(1, profile.BuildNumber);
            }
        }

        /// <summary>Writes the resolved version and build number into the player settings.</summary>
        public static void Apply(BuildContext context)
        {
            if (context.DryRun)
            {
                context.Log.Info(
                    $"[dry run] Would set version {context.Version} and build number {context.BuildNumber}.");
                return;
            }

            PlayerSettings.bundleVersion = context.Version;
            PlayerSettings.Android.bundleVersionCode = context.BuildNumber;
            PlayerSettings.iOS.buildNumber = context.BuildNumber.ToString(CultureInfo.InvariantCulture);
            PlayerSettings.macOS.buildNumber = context.BuildNumber.ToString(CultureInfo.InvariantCulture);

            context.Log.Info($"Version {context.Version} (build {context.BuildNumber}).");
        }

        /// <summary>
        /// Bumps the stored counter of a profile after a successful build, when its policy asks
        /// for it. Returns the new value, or the unchanged one.
        /// </summary>
        public static int CommitBuildNumber(BuildTargetProfile profile)
        {
            if (profile == null || profile.BuildNumberPolicy != BuildNumberPolicy.AutoIncrementOnSuccess)
                return profile != null ? profile.BuildNumber : 0;

            profile.BuildNumber += 1;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);

            return profile.BuildNumber;
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

        /// <summary>Writes a version string back to the profile's version file.</summary>
        public static void WriteVersionFile(BuildTargetProfile profile, string version)
        {
            if (profile == null || profile.VersionSource != VersionSource.VersionFile)
                return;

            var path = ProjectPaths.MakeAbsolute(profile.VersionFilePath);
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
