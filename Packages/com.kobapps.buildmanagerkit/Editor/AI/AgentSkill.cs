using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>Where an installed copy of the agent skill lives.</summary>
    public enum AgentSkillScope
    {
        /// <summary>
        /// <c>&lt;project&gt;/.claude/skills/</c> — commit it and the whole team's agents get it.
        /// </summary>
        Project = 0,

        /// <summary>
        /// <c>~/.claude/skills/</c> — available in every project on this machine, not shared.
        /// </summary>
        User = 1
    }

    /// <summary>What an install target currently holds.</summary>
    public enum AgentSkillState
    {
        /// <summary>Nothing is installed there.</summary>
        NotInstalled = 0,

        /// <summary>An installed copy matching the one shipped with this package version.</summary>
        UpToDate = 1,

        /// <summary>An installed copy that differs — an older package version, or local edits.</summary>
        Outdated = 2,

        /// <summary>The package's own copy of the skill could not be found.</summary>
        SourceMissing = 3
    }

    /// <summary>
    /// Installs the agent skill that ships in <c>Skills~/buildmanagerkit</c> into a location where
    /// a coding agent will find it.
    ///
    /// The skill teaches an agent to drive <see cref="ConfigCLI"/> and <see cref="BuildCLI"/>
    /// rather than hand editing the <c>.asset</c> YAML — which silently drops
    /// <c>[SerializeReference]</c> action lists and object references. It ships inside a <c>~</c>
    /// suffixed folder so Unity does not import it as assets, which also means it is a plain file
    /// copy rather than an <c>AssetDatabase</c> operation.
    /// </summary>
    public static class AgentSkill
    {
        /// <summary>Folder name used both in the package and at every install target.</summary>
        public const string SkillName = "buildmanagerkit";

        private const string k_SourceFolder = "Skills~/" + SkillName;
        private const string k_SkillFile = "SKILL.md";

        /// <summary>Absolute path of the skill shipped inside the package, or null when missing.</summary>
        public static string SourcePath
        {
            get
            {
                var packageRoot = ResolvePackageRoot();
                if (string.IsNullOrEmpty(packageRoot))
                    return null;

                var path = Path.Combine(packageRoot, k_SourceFolder);
                return File.Exists(Path.Combine(path, k_SkillFile)) ? path : null;
            }
        }

        /// <summary>Absolute path this scope installs to, whether or not anything is there.</summary>
        /// <param name="scope">Project or user level.</param>
        public static string GetInstallPath(AgentSkillScope scope)
        {
            var root = scope == AgentSkillScope.User
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : ProjectPaths.ProjectRoot;

            return Path.Combine(root, ".claude", "skills", SkillName);
        }

        /// <summary>What is installed at <paramref name="scope"/> right now.</summary>
        /// <param name="scope">Project or user level.</param>
        public static AgentSkillState GetState(AgentSkillScope scope)
        {
            var source = SourcePath;
            if (source == null)
                return AgentSkillState.SourceMissing;

            var destination = GetInstallPath(scope);
            if (!File.Exists(Path.Combine(destination, k_SkillFile)))
                return AgentSkillState.NotInstalled;

            return Fingerprint(source) == Fingerprint(destination)
                ? AgentSkillState.UpToDate
                : AgentSkillState.Outdated;
        }

        /// <summary>Version of the package the skill ships with, for display.</summary>
        public static string PackageVersion
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(AgentSkill).Assembly);

                return info != null ? info.version : "local";
            }
        }

        /// <summary>Files that would be written, relative to the install folder.</summary>
        public static IReadOnlyList<string> GetFileList()
        {
            var source = SourcePath;
            return source == null ? Array.Empty<string>() : RelativeFiles(source);
        }

        /// <summary>
        /// Copies the shipped skill over the target, replacing whatever is there.
        /// </summary>
        /// <param name="scope">Project or user level.</param>
        /// <param name="error">Set when the install failed.</param>
        public static bool Install(AgentSkillScope scope, out string error)
        {
            var source = SourcePath;

            if (source == null)
            {
                error = $"The package does not contain {k_SourceFolder}/{k_SkillFile}.";
                return false;
            }

            var destination = GetInstallPath(scope);

            try
            {
                // Clear first so a file removed from a later package version does not linger.
                if (Directory.Exists(destination) && IsOurSkillFolder(destination))
                    Directory.Delete(destination, true);

                foreach (var relative in RelativeFiles(source))
                {
                    var target = Path.Combine(destination, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                    File.Copy(Path.Combine(source, relative), target, true);
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Deletes an installed copy. Refuses a folder that does not look like this skill, so a
        /// mistyped path or a hand-written skill of the user's own is never removed.
        /// </summary>
        /// <param name="scope">Project or user level.</param>
        /// <param name="error">Set when the removal failed or was refused.</param>
        public static bool Uninstall(AgentSkillScope scope, out string error)
        {
            var destination = GetInstallPath(scope);

            if (!Directory.Exists(destination))
            {
                error = null;
                return true;
            }

            if (!IsOurSkillFolder(destination))
            {
                error = $"{destination} does not look like the Build Manager Kit skill, so it was "
                        + "left alone. Delete it by hand if that is really what you want.";
                return false;
            }

            try
            {
                Directory.Delete(destination, true);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// True when a folder holds this package's skill rather than something else. Matched on
        /// the skill's declared name in the front matter, not on the folder name, so a folder that
        /// merely shares the name is not treated as ours.
        /// </summary>
        /// <param name="folder">Folder to inspect.</param>
        internal static bool IsOurSkillFolder(string folder)
        {
            var file = Path.Combine(folder, k_SkillFile);

            if (!File.Exists(file))
                return false;

            try
            {
                return DeclaresSkillName(File.ReadAllText(file), SkillName);
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// True when the front matter of <paramref name="content"/> declares
        /// <paramref name="expectedName"/>. Kept separate from the file system so it is testable.
        /// </summary>
        /// <param name="content">Whole SKILL.md text.</param>
        /// <param name="expectedName">Name the skill must declare.</param>
        internal static bool DeclaresSkillName(string content, string expectedName)
        {
            if (string.IsNullOrEmpty(content) || !content.StartsWith("---", StringComparison.Ordinal))
                return false;

            using (var reader = new StringReader(content))
            {
                reader.ReadLine();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("---", StringComparison.Ordinal))
                        return false;

                    if (!line.StartsWith("name:", StringComparison.Ordinal))
                        continue;

                    var value = line.Substring("name:".Length).Trim().Trim('"', '\'');
                    return string.Equals(value, expectedName, StringComparison.Ordinal);
                }
            }

            return false;
        }

        /// <summary>
        /// Content hash of every file in a skill folder, used to tell "up to date" from "changed".
        /// Line endings are normalised so a checkout with different settings does not read as an
        /// endless stream of pending updates.
        /// </summary>
        /// <param name="folder">Folder to hash.</param>
        internal static string Fingerprint(string folder)
        {
            if (!Directory.Exists(folder))
                return string.Empty;

            using (var sha = SHA256.Create())
            {
                var builder = new StringBuilder();

                foreach (var relative in RelativeFiles(folder))
                {
                    builder.Append(relative).Append('\n');

                    try
                    {
                        builder.Append(File.ReadAllText(Path.Combine(folder, relative))
                            .Replace("\r\n", "\n")).Append('\n');
                    }
                    catch (IOException)
                    {
                        // An unreadable file simply contributes nothing but its name, which still
                        // makes the fingerprint differ from a folder that does not have it.
                    }
                }

                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
            }
        }

        private static IReadOnlyList<string> RelativeFiles(string folder)
        {
            if (!Directory.Exists(folder))
                return Array.Empty<string>();

            return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(path => path.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar))
                .Where(relative => !relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                                   && !Path.GetFileName(relative).StartsWith(".", StringComparison.Ordinal))
                .Select(relative => relative.Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(relative => relative, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Absolute path of the package folder. Works for an embedded, registry or local package;
        /// falls back to the conventional path so a source checkout still resolves.
        /// </summary>
        private static string ResolvePackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AgentSkill).Assembly);

            if (info != null && !string.IsNullOrEmpty(info.resolvedPath) && Directory.Exists(info.resolvedPath))
                return info.resolvedPath;

            var fallback = Path.Combine(ProjectPaths.ProjectRoot, ProjectPaths.PackageRoot);
            return Directory.Exists(fallback) ? fallback : null;
        }
    }
}
