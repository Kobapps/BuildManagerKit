using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Helpers that translate between <see cref="BuildTarget"/> and the various derived values
    /// Unity needs (named targets, file extensions, module availability).
    /// </summary>
    public static class BuildTargetUtility
    {
        /// <summary>Targets offered in the profile creation UI, in a sensible order.</summary>
        public static readonly BuildTarget[] CommonTargets =
        {
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneOSX,
            BuildTarget.StandaloneLinux64,
            BuildTarget.Android,
            BuildTarget.iOS,
            BuildTarget.WebGL
        };

        /// <summary>
        /// Resolves the <see cref="NamedBuildTarget"/> used by the modern PlayerSettings API.
        /// Standalone falls back to <see cref="NamedBuildTarget.Server"/> for dedicated servers.
        /// </summary>
        public static NamedBuildTarget GetNamedBuildTarget(BuildTarget target, StandaloneBuildSubtarget subtarget)
        {
            var group = BuildPipeline.GetBuildTargetGroup(target);
            if (group == BuildTargetGroup.Unknown)
                return NamedBuildTarget.Standalone;

            if (group == BuildTargetGroup.Standalone && subtarget == StandaloneBuildSubtarget.Server)
                return NamedBuildTarget.Server;

            try
            {
                return NamedBuildTarget.FromBuildTargetGroup(group);
            }
            catch (ArgumentException)
            {
                return NamedBuildTarget.Standalone;
            }
        }

        /// <summary>True when the platform module for <paramref name="target"/> is installed.</summary>
        public static bool IsTargetInstalled(BuildTarget target) =>
            BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target);

        /// <summary>Compact display name, e.g. <c>Win64</c> or <c>macOS</c>.</summary>
        public static string GetShortName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows: return "Win";
                case BuildTarget.StandaloneWindows64: return "Win64";
                case BuildTarget.StandaloneOSX: return "macOS";
                case BuildTarget.StandaloneLinux64: return "Linux64";
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iOS";
                case BuildTarget.tvOS: return "tvOS";
                case BuildTarget.WebGL: return "WebGL";
                case BuildTarget.WSAPlayer: return "UWP";
                case BuildTarget.PS4: return "PS4";
                case BuildTarget.PS5: return "PS5";
                case BuildTarget.XboxOne: return "XboxOne";
                case BuildTarget.Switch: return "Switch";
                default: return target.ToString();
            }
        }

        /// <summary>
        /// File extension (without the dot) Unity produces for the target, or an empty string
        /// for targets that output a folder such as iOS and WebGL.
        /// </summary>
        public static string GetPlayerExtension(BuildTarget target, bool buildAppBundle)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "exe";
                case BuildTarget.StandaloneOSX:
                    return "app";
                case BuildTarget.StandaloneLinux64:
                    return "x86_64";
                case BuildTarget.Android:
                    return buildAppBundle ? "aab" : "apk";
                default:
                    return string.Empty;
            }
        }

        /// <summary>True when Unity writes a directory rather than a single file.</summary>
        public static bool IsFolderOutput(BuildTarget target, bool buildAppBundle) =>
            string.IsNullOrEmpty(GetPlayerExtension(target, buildAppBundle));

        /// <summary>
        /// Combines <paramref name="baseName"/> with the platform extension. macOS is treated as
        /// a file even though <c>.app</c> is a bundle directory, which matches what
        /// <c>BuildPipeline.BuildPlayer</c> expects.
        /// </summary>
        public static string GetPlayerFileName(BuildTarget target, string baseName, bool buildAppBundle)
        {
            var sanitized = BuildTokens.Sanitize(baseName);
            if (string.IsNullOrEmpty(sanitized))
                sanitized = "Player";

            var extension = GetPlayerExtension(target, buildAppBundle);
            return string.IsNullOrEmpty(extension) ? sanitized : sanitized + "." + extension;
        }

        /// <summary>
        /// Total size in bytes of a build output, transparently handling both single files and
        /// directories. Returns 0 when the path does not exist.
        /// </summary>
        public static long GetOutputSize(string path)
        {
            try
            {
                if (File.Exists(path))
                    return new FileInfo(path).Length;

                if (!Directory.Exists(path))
                    return 0;

                long total = 0;
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                        // Skip files that vanished or cannot be read.
                    }
                }

                return total;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>Formats a byte count as a short human readable string.</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes <= 0)
                return "0 B";

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var order = 0;
            double size = bytes;

            while (size >= 1024 && order < units.Length - 1)
            {
                size /= 1024;
                order++;
            }

            return order == 0
                ? $"{size:0} {units[order]}"
                : $"{size:0.##} {units[order]}";
        }

        /// <summary>Formats a duration the way build servers usually print it.</summary>
        public static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";

            return duration.TotalMinutes >= 1
                ? $"{duration.Minutes}m {duration.Seconds}s"
                : $"{duration.TotalSeconds:0.0}s";
        }

        /// <summary>Every target that currently has its platform module installed.</summary>
        public static IEnumerable<BuildTarget> GetInstalledTargets()
        {
            foreach (BuildTarget target in Enum.GetValues(typeof(BuildTarget)))
            {
                if (IsObsolete(target))
                    continue;

                if (IsTargetInstalled(target))
                    yield return target;
            }
        }

        private static bool IsObsolete(BuildTarget target)
        {
            var field = typeof(BuildTarget).GetField(target.ToString());
            return field != null && Attribute.IsDefined(field, typeof(ObsoleteAttribute));
        }
    }
}
