using System;
using System.IO;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>Common paths, all normalised to forward slashes.</summary>
    public static class ProjectPaths
    {
        /// <summary>Package identifier, useful when loading packaged assets.</summary>
        public const string PackageName = "com.kobapps.buildmanagerkit";

        /// <summary>Root of the package inside the Packages folder.</summary>
        public const string PackageRoot = "Packages/" + PackageName;

        /// <summary>Absolute path of the folder that contains <c>Assets</c>.</summary>
        public static string ProjectRoot { get; } = ResolveProjectRoot();

        /// <summary>Name of the project folder.</summary>
        public static string ProjectName { get; } = new DirectoryInfo(ProjectRoot).Name;

        /// <summary>Folder the generated <see cref="BuildInfo"/> asset is written to.</summary>
        public const string GeneratedResourcesFolder = "Assets/Resources/BuildManagerKit";

        /// <summary>Asset path of the generated <see cref="BuildInfo"/>.</summary>
        public const string BuildInfoAssetPath = GeneratedResourcesFolder + "/BuildInfo.asset";

        /// <summary>Default folder new settings and profile assets are created in.</summary>
        public const string DefaultSettingsFolder = "Assets/BuildManagerKit";

        private static string ResolveProjectRoot()
        {
            var parent = Directory.GetParent(Application.dataPath);
            return Normalize(parent != null ? parent.FullName : Application.dataPath);
        }

        /// <summary>
        /// Folders a build must never write to or delete. Writing a player into <c>Assets</c>
        /// makes Unity import the whole thing; touching <c>Library</c> or <c>ProjectSettings</c>
        /// corrupts the project. These are refused outright.
        /// </summary>
        public static readonly string[] ProtectedFolders =
        {
            "Assets",
            "Library",
            "Packages",
            "ProjectSettings",
            "UserSettings"
        };

        /// <summary>
        /// Folders that work but are a poor choice, because Unity treats them as scratch space and
        /// may clear them. Building here is allowed — CI often does want a throwaway location —
        /// but it is worth a warning.
        /// </summary>
        public static readonly string[] DiscouragedFolders =
        {
            "Temp",
            "obj",
            "Logs"
        };

        /// <summary>
        /// Longest output path the kit accepts before refusing to build. Windows resolves most
        /// APIs against a 260 character limit, and Unity appends its own sub-paths beneath the one
        /// we hand it, so the ceiling here leaves room for those.
        /// </summary>
        public const int MaxRecommendedPathLength = 180;

        /// <summary>Hard ceiling: beyond this a build will fail on Windows.</summary>
        public const int MaxPathLength = 240;

        /// <summary>
        /// True when <paramref name="absolutePath"/> is somewhere a build must never write to or
        /// delete: the project root itself, any ancestor of it, or one of Unity's own folders.
        /// </summary>
        /// <param name="absolutePath">Absolute path to test.</param>
        /// <param name="reason">Human readable explanation when the path is rejected.</param>
        public static bool IsProtectedOutputPath(string absolutePath, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                reason = "the path is empty";
                return true;
            }

            var path = Normalize(absolutePath);
            var root = ProjectRoot;

            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                reason = "it is the project root";
                return true;
            }

            // An ancestor of the project — a clean step here would delete the project itself.
            if (IsSameOrUnder(root, path))
            {
                reason = $"it contains the project ('{path}')";
                return true;
            }

            if (!IsSameOrUnder(path, root))
                return false;

            var folder = GetTopLevelFolder(path);

            foreach (var protectedFolder in ProtectedFolders)
            {
                if (string.Equals(folder, protectedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"it is inside the project's '{protectedFolder}' folder";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the path works but sits in a folder Unity treats as scratch space and may
        /// clear without warning. Callers should warn rather than refuse.
        /// </summary>
        public static bool IsDiscouragedOutputPath(string absolutePath, out string reason)
        {
            reason = string.Empty;

            var folder = GetTopLevelFolder(ProjectPaths.Normalize(absolutePath));
            if (string.IsNullOrEmpty(folder))
                return false;

            foreach (var discouraged in DiscouragedFolders)
            {
                if (string.Equals(folder, discouraged, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"'{discouraged}' is scratch space that Unity may clear";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The first path segment below the project root, or empty when the path is not inside the
        /// project. Compared segment-wise so "AssetsBackup" never matches "Assets".
        /// </summary>
        private static string GetTopLevelFolder(string absolutePath)
        {
            var path = Normalize(absolutePath);
            var root = ProjectRoot;

            if (string.IsNullOrEmpty(path) || !IsSameOrUnder(path, root) ||
                string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return path.Substring(root.Length).TrimStart('/').Split('/')[0];
        }

        /// <summary>True when <paramref name="child"/> is <paramref name="parent"/> or below it.</summary>
        public static bool IsSameOrUnder(string child, string parent)
        {
            var normalizedChild = Normalize(child);
            var normalizedParent = Normalize(parent);

            if (string.IsNullOrEmpty(normalizedChild) || string.IsNullOrEmpty(normalizedParent))
                return false;

            return string.Equals(normalizedChild, normalizedParent, StringComparison.OrdinalIgnoreCase)
                   || normalizedChild.StartsWith(normalizedParent + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Replaces backslashes with forward slashes and trims trailing separators.</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var normalized = path.Replace('\\', '/');
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);

            return normalized;
        }

        /// <summary>
        /// Turns a project relative path into an absolute one, collapsing any <c>..</c> segments.
        /// Collapsing matters for the containment checks: without it
        /// <c>Builds/../../elsewhere</c> would look like it sits under the project.
        /// </summary>
        public static string MakeAbsolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ProjectRoot;

            var normalized = Normalize(path);
            var combined = Path.IsPathRooted(normalized) ? normalized : Path.Combine(ProjectRoot, normalized);

            try
            {
                return Normalize(Path.GetFullPath(combined));
            }
            catch (Exception)
            {
                // Invalid characters for this platform: hand back the uncollapsed path so the
                // caller's own validation reports a useful message instead of throwing here.
                return Normalize(combined);
            }
        }

        /// <summary>Turns an absolute path inside the project into a project relative one.</summary>
        public static string MakeRelative(string absolutePath)
        {
            var normalized = Normalize(absolutePath);
            if (normalized.StartsWith(ProjectRoot + "/", StringComparison.OrdinalIgnoreCase))
                return normalized.Substring(ProjectRoot.Length + 1);

            return normalized;
        }

        /// <summary>Creates every folder in <paramref name="path"/> that does not exist yet.</summary>
        public static void EnsureDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path))
                Directory.CreateDirectory(path);
        }

        /// <summary>
        /// Creates a folder inside <c>Assets</c> including its parents, using the AssetDatabase so
        /// Unity picks it up immediately.
        /// </summary>
        public static void EnsureAssetFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || UnityEditor.AssetDatabase.IsValidFolder(assetFolder))
                return;

            var parts = Normalize(assetFolder).Split('/');
            var current = parts[0];

            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!UnityEditor.AssetDatabase.IsValidFolder(next))
                    UnityEditor.AssetDatabase.CreateFolder(current, parts[i]);

                current = next;
            }
        }
    }
}
