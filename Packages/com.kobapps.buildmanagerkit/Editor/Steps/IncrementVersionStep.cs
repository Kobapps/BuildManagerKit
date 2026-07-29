using System;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Bumps the semantic version before a build. The new value is applied to the run, to
    /// <c>PlayerSettings.bundleVersion</c> and, when the profile reads its version from a file,
    /// written back to that file so the increment survives.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Versioning/Increment Version",
        Tooltip = "Bumps major, minor or patch before building.",
        Scope = BuildStepScope.PreBuild,
        Order = -100)]
    public sealed class IncrementVersionStep : BuildStep
    {
        [Tooltip("Which component of major.minor.patch to increment.")]
        [SerializeField] private VersionComponent m_Component = VersionComponent.Patch;

        [Tooltip("Write the bumped value back to the run's version file or to PlayerSettings.")]
        [SerializeField] private bool m_Persist = true;

        /// <inheritdoc />
        public override string Summary => $"Bump {m_Component.ToString().ToLowerInvariant()}"
                                          + (m_Persist ? " and persist" : string.Empty);

        /// <inheritdoc />
        public override void Validate(BuildContext context, BuildValidationReport report)
        {
            if (!VersionService.IsValid(context.Version))
                report.AddWarning($"Version '{context.Version}' is not major.minor.patch and cannot be bumped.");

            if (!context.Versioning.manageVersion && !m_Persist)
                report.AddWarning(
                    "Nothing manages the version for this build and this action does not persist, so the bump "
                    + "only affects the {version} tokens of this run and the player keeps its current version.");
        }

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var previous = context.Version;
            var bumped = VersionService.Bump(previous, m_Component);

            if (string.Equals(previous, bumped, StringComparison.Ordinal))
            {
                context.Log.Warning($"Version '{previous}' could not be bumped and was left unchanged.");
                return;
            }

            context.Version = bumped;
            context.RefreshTokens();
            context.Log.Info($"Version {previous} → {bumped}.");

            if (context.DryRun || !m_Persist)
                return;

            PlayerSettings.bundleVersion = bumped;

            // Whichever level owns versioning may back the version with a text file; writing it back
            // there is what stops the next build from resolving the old value again.
            VersionService.WriteVersionFile(context.Versioning, bumped);

            AssetDatabase.SaveAssets();
        }
    }
}
