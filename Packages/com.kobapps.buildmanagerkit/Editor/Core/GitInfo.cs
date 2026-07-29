using System;
using System.IO;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Snapshot of the git working copy taken at the start of a build. Every field degrades to an
    /// empty string when git is unavailable, so a project without git still builds fine.
    /// </summary>
    [Serializable]
    public sealed class GitInfo
    {
        /// <summary>Neutral instance used when the project is not a git repository.</summary>
        public static readonly GitInfo None = new GitInfo();

        /// <summary>Current branch name, e.g. <c>main</c>.</summary>
        public string Branch = string.Empty;

        /// <summary>Abbreviated commit hash.</summary>
        public string ShortCommit = string.Empty;

        /// <summary>Full commit hash.</summary>
        public string Commit = string.Empty;

        /// <summary>Most recent tag reachable from HEAD, empty when there is none.</summary>
        public string Tag = string.Empty;

        /// <summary>Number of commits on the current branch.</summary>
        public int CommitCount;

        /// <summary>True when the working copy contains uncommitted changes.</summary>
        public bool IsDirty;

        /// <summary>True when git ran successfully and the project is a repository.</summary>
        public bool IsRepository;

        /// <summary>Subject line of the HEAD commit.</summary>
        public string CommitSubject = string.Empty;

        /// <summary>Author of the HEAD commit.</summary>
        public string CommitAuthor = string.Empty;

        private static GitInfo s_Cached;
        private static double s_CachedAt = double.NegativeInfinity;

        /// <summary>
        /// Reads the working copy state. Results are cached for a few seconds because the Editor
        /// window polls this while repainting.
        /// </summary>
        /// <param name="workingDirectory">Repository directory, defaults to the project root.</param>
        /// <param name="forceRefresh">Bypass the cache.</param>
        public static GitInfo Read(string workingDirectory = null, bool forceRefresh = false)
        {
            var useCache = string.IsNullOrEmpty(workingDirectory);
            var now = UnityEditor.EditorApplication.timeSinceStartup;

            if (useCache && !forceRefresh && s_Cached != null && now - s_CachedAt < 5.0)
                return s_Cached;

            var directory = string.IsNullOrEmpty(workingDirectory) ? ProjectPaths.ProjectRoot : workingDirectory;
            var info = ReadUncached(directory);

            if (useCache)
            {
                s_Cached = info;
                s_CachedAt = now;
            }

            return info;
        }

        private static GitInfo ReadUncached(string directory)
        {
            var info = new GitInfo();

            if (!HasGitDirectory(directory))
                return info;

            try
            {
                var inside = Git(directory, "rev-parse --is-inside-work-tree");
                if (!inside.Succeeded || !inside.Trimmed.StartsWith("true", StringComparison.OrdinalIgnoreCase))
                    return info;

                info.IsRepository = true;
                info.Branch = Git(directory, "rev-parse --abbrev-ref HEAD").Trimmed;
                info.ShortCommit = Git(directory, "rev-parse --short HEAD").Trimmed;
                info.Commit = Git(directory, "rev-parse HEAD").Trimmed;
                info.Tag = Git(directory, "describe --tags --abbrev=0").Trimmed;
                info.CommitSubject = Git(directory, "log -1 --pretty=%s").Trimmed;
                info.CommitAuthor = Git(directory, "log -1 --pretty=%an").Trimmed;
                info.IsDirty = !string.IsNullOrWhiteSpace(Git(directory, "status --porcelain").Trimmed);

                if (int.TryParse(Git(directory, "rev-list --count HEAD").Trimmed, out var count))
                    info.CommitCount = count;
            }
            catch (Exception)
            {
                // git is not installed or not on PATH — leave the neutral values in place.
            }

            return info;
        }

        private static bool HasGitDirectory(string directory)
        {
            var current = new DirectoryInfo(directory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git"))
                    || File.Exists(Path.Combine(current.FullName, ".git")))
                    return true;

                current = current.Parent;
            }

            return false;
        }

        private static ProcessResult Git(string directory, string arguments) =>
            ProcessRunner.Run("git", arguments, directory, 15000);

        /// <summary>Short human readable summary, e.g. <c>main@a1b2c3d*</c>.</summary>
        public override string ToString() =>
            IsRepository ? $"{Branch}@{ShortCommit}{(IsDirty ? "*" : string.Empty)}" : "no git";
    }
}
