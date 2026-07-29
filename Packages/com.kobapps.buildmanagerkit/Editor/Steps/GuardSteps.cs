using System;
using System.Linq;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Refuses to build from a dirty working copy. Add it to the production environment so a
    /// release can always be traced back to an exact commit.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Guards/Require Clean Working Copy",
        Tooltip = "Fails the build when git reports uncommitted changes.",
        Scope = BuildStepScope.PreBuild,
        Order = -200)]
    public sealed class RequireCleanWorkingCopyStep : BuildStep
    {
        [Tooltip("Also fail when the project is not a git repository at all.")]
        [SerializeField] private bool m_RequireRepository = true;

        [Tooltip("Restrict the build to these branches. Empty allows any branch.")]
        [SerializeField] private string[] m_AllowedBranches = Array.Empty<string>();

        /// <inheritdoc />
        public override string Summary => m_AllowedBranches.Length > 0
            ? "Clean copy on " + string.Join(", ", m_AllowedBranches)
            : "Clean working copy";

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var git = context.Git ?? GitInfo.None;

            if (!git.IsRepository)
            {
                if (m_RequireRepository)
                    throw new BuildStepException("The project is not a git repository.");

                context.Log.Warning("Not a git repository; skipping the clean working copy check.");
                return;
            }

            if (git.IsDirty)
                throw new BuildStepException(
                    "The working copy has uncommitted changes. Commit or stash them before building.");

            if (m_AllowedBranches.Length > 0 &&
                !m_AllowedBranches.Any(branch =>
                    string.Equals(branch?.Trim(), git.Branch, StringComparison.OrdinalIgnoreCase)))
                throw new BuildStepException(
                    $"Branch '{git.Branch}' is not allowed. Allowed: {string.Join(", ", m_AllowedBranches)}.");

            context.Log.Success($"Working copy is clean on '{git.Branch}' at {git.ShortCommit}.");
        }
    }

    /// <summary>
    /// Fails early when required environment variables are missing, so a two hour IL2CPP build
    /// does not die at the upload step because a token was not exported.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Guards/Require Environment Variables",
        Tooltip = "Fails the build when required environment variables are missing.",
        Scope = BuildStepScope.PreBuild,
        Order = -190)]
    public sealed class RequireEnvironmentVariablesStep : BuildStep
    {
        [Tooltip("Names of the environment variables that must be present and non-empty.")]
        [SerializeField] private string[] m_Variables = { "BMK_WEBHOOK_URL" };

        /// <inheritdoc />
        public override string Summary => string.Join(", ", m_Variables);

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var missing = m_Variables
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Where(name => string.IsNullOrEmpty(context.GetVariable(name)))
                .ToArray();

            if (missing.Length > 0)
                throw new BuildStepException($"Missing environment variable(s): {string.Join(", ", missing)}.");

            context.Log.Info($"All {m_Variables.Length} required environment variable(s) are present.");
        }
    }
}
