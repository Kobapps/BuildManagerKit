using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Compresses the build output into a distributable archive. The archive is registered as a
    /// build artifact, so it shows up in the history, the manifest and the JSON result CI reads.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Files/Zip Output",
        Tooltip = "Compresses the build output into a .zip archive.",
        Scope = BuildStepScope.PostBuild,
        Order = 20)]
    public sealed class ZipOutputStep : BuildStep
    {
        [Tooltip("What to compress. Leave empty to zip the whole output folder.")]
        [SerializeField] private string m_SourceOverride = string.Empty;

        [Tooltip("Archive path. Relative paths resolve against the parent of the output folder.")]
        [SerializeField] private string m_ArchivePath = "{productName}_{version}_{targetShort}_{env}.zip";

        // Fully qualified: UnityEngine also declares a CompressionLevel.
        [Tooltip("Trade compression ratio against time.")]
        [SerializeField]
        private System.IO.Compression.CompressionLevel m_CompressionLevel =
            System.IO.Compression.CompressionLevel.Optimal;

        [Tooltip("Include the output folder itself as the root entry of the archive.")]
        [SerializeField] private bool m_IncludeRootFolder = true;

        [Tooltip("Delete the uncompressed output after a successful archive.")]
        [SerializeField] private bool m_DeleteSourceAfterwards;

        /// <inheritdoc />
        public override string Summary => m_ArchivePath;

        /// <inheritdoc />
        public override void Validate(BuildContext context, BuildValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(m_ArchivePath))
                report.AddError("Archive path is empty.");
        }

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var source = string.IsNullOrWhiteSpace(m_SourceOverride)
                ? context.OutputDirectory
                : context.ResolvePath(m_SourceOverride);

            if (!Directory.Exists(source) && !File.Exists(source))
                throw new BuildStepException($"Nothing to compress: '{source}' does not exist.");

            var archive = ResolveArchivePath(context);

            if (context.DryRun)
            {
                context.Log.Info($"[dry run] Would compress '{source}' into '{archive}'.");
                return;
            }

            var directory = Path.GetDirectoryName(archive);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(archive))
                File.Delete(archive);

            try
            {
                if (File.Exists(source))
                {
                    using var stream = ZipFile.Open(archive, ZipArchiveMode.Create);
                    stream.CreateEntryFromFile(source, Path.GetFileName(source), m_CompressionLevel);
                }
                else
                {
                    ZipFile.CreateFromDirectory(source, archive, m_CompressionLevel, m_IncludeRootFolder);
                }
            }
            catch (Exception exception)
            {
                throw new BuildStepException($"Could not create '{archive}': {exception.Message}", exception);
            }

            var size = BuildTargetUtility.FormatSize(new FileInfo(archive).Length);
            context.AddArtifact(archive);
            context.SetVariable("archivePath", archive);
            context.Log.Success($"Created '{archive}' ({size}).");

            if (!m_DeleteSourceAfterwards)
                return;

            try
            {
                if (Directory.Exists(source))
                    Directory.Delete(source, true);
                else
                    File.Delete(source);

                context.Log.Info($"Deleted the uncompressed output at '{source}'.");
            }
            catch (Exception exception)
            {
                context.Log.Warning($"Could not delete '{source}': {exception.Message}");
            }
        }

        private string ResolveArchivePath(BuildContext context)
        {
            var resolved = context.Resolve(m_ArchivePath);

            if (Path.IsPathRooted(resolved))
                return ProjectPaths.Normalize(resolved);

            var parent = Directory.GetParent(context.OutputDirectory);
            var root = parent != null ? parent.FullName : ProjectPaths.ProjectRoot;

            return ProjectPaths.Normalize(Path.Combine(root, resolved));
        }
    }
}
