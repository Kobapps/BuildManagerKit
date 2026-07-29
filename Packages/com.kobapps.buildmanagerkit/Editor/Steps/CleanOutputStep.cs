using System;
using System.IO;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Deletes the output folder before building so no files from a previous build survive into
    /// the new one. Refuses to delete anything outside the project unless explicitly allowed.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Files/Clean Output Folder",
        Tooltip = "Deletes the build output folder before building.",
        Scope = BuildStepScope.PreBuild,
        Order = -80)]
    public sealed class CleanOutputStep : BuildStep
    {
        [Tooltip("Folder to delete. Leave empty to use the resolved output folder of this build.")]
        [SerializeField] private string m_FolderOverride = string.Empty;

        [Tooltip("Allow deleting folders outside the project root. Off by default for safety.")]
        [SerializeField] private bool m_AllowOutsideProject;

        /// <inheritdoc />
        public override string Summary =>
            string.IsNullOrWhiteSpace(m_FolderOverride) ? "{outputDir}" : m_FolderOverride;

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var folder = string.IsNullOrWhiteSpace(m_FolderOverride)
                ? context.OutputDirectory
                : context.ResolvePath(m_FolderOverride);

            if (string.IsNullOrEmpty(folder))
                throw new BuildStepException("No folder to clean was resolved.");

            var normalized = ProjectPaths.MakeAbsolute(folder);

            // Protected paths are refused unconditionally: no toggle makes deleting Assets/ or the
            // project root a reasonable thing for a build step to do.
            if (ProjectPaths.IsProtectedOutputPath(normalized, out var reason))
                throw new BuildStepException($"Refusing to delete '{normalized}' because {reason}.");

            if (!m_AllowOutsideProject && !ProjectPaths.IsSameOrUnder(normalized, ProjectPaths.ProjectRoot))
                throw new BuildStepException(
                    $"Refusing to delete '{normalized}' because it is outside the project. "
                    + "Enable 'Allow Outside Project' if this is intentional.");

            if (!Directory.Exists(normalized))
            {
                context.Log.Info($"Nothing to clean, '{normalized}' does not exist.");
                return;
            }

            if (context.DryRun)
            {
                context.Log.Info($"[dry run] Would delete '{normalized}'.");
                return;
            }

            try
            {
                Directory.Delete(normalized, true);
                context.Log.Info($"Deleted '{normalized}'.");
            }
            catch (Exception exception)
            {
                throw new BuildStepException($"Could not delete '{normalized}': {exception.Message}", exception);
            }
        }
    }
}
