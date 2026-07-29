using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Copies files or whole folders next to the build output — read-me files, config, licences,
    /// server certificates, launcher scripts. Both paths support tokens.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Files/Copy Files",
        Tooltip = "Copies files or folders into the build output.",
        Order = 10)]
    public sealed class CopyFilesStep : BuildStep
    {
        [Tooltip("File or folder to copy. Relative paths resolve against the project root.")]
        [SerializeField] private string m_Source = "Config/{env}";

        [Tooltip("Destination. Relative paths resolve against the build output folder.")]
        [SerializeField] private string m_Destination = "Config";

        [Tooltip("When the source is a folder, copy its sub folders too.")]
        [SerializeField] private bool m_Recursive = true;

        [Tooltip("Only copy files matching this pattern, e.g. *.json. Empty copies everything.")]
        [SerializeField] private string m_SearchPattern = string.Empty;

        [Tooltip("Overwrite files that already exist at the destination.")]
        [SerializeField] private bool m_Overwrite = true;

        [Tooltip("Log a warning instead of failing when the source does not exist.")]
        [SerializeField] private bool m_SkipWhenMissing;

        /// <inheritdoc />
        public override string Summary => $"{m_Source} → {m_Destination}";

        /// <inheritdoc />
        public override void Validate(BuildContext context, BuildValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(m_Source))
                report.AddError("Source path is empty.");

            if (string.IsNullOrWhiteSpace(m_Destination))
                report.AddError("Destination path is empty.");

            if (string.IsNullOrWhiteSpace(m_Destination))
                return;

            // A relative destination containing ".." would write outside the build folder — into
            // the project, or over something else entirely.
            var destination = ResolveDestination(context);

            if (ProjectPaths.IsProtectedOutputPath(destination, out var reason))
                report.AddError($"Destination '{destination}' is not writable because {reason}.");
            else if (!string.IsNullOrEmpty(context.OutputDirectory)
                     && !Path.IsPathRooted(context.Resolve(m_Destination))
                     && !ProjectPaths.IsSameOrUnder(destination, context.OutputDirectory))
                report.AddError(
                    $"Destination '{destination}' escapes the build output folder "
                    + $"('{context.OutputDirectory}'). Use an absolute path if that is intended.");
        }

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var source = context.ResolvePath(m_Source);
            var destination = ResolveDestination(context);

            if (File.Exists(source))
            {
                CopySingleFile(context, source, destination);
                return;
            }

            if (Directory.Exists(source))
            {
                CopyDirectory(context, source, destination);
                return;
            }

            var message = $"Source '{source}' does not exist.";
            if (m_SkipWhenMissing)
                context.Log.Warning(message + " Skipping.");
            else
                throw new BuildStepException(message);
        }

        private string ResolveDestination(BuildContext context)
        {
            var resolved = context.Resolve(m_Destination);

            if (Path.IsPathRooted(resolved))
                return ProjectPaths.MakeAbsolute(resolved);

            var root = string.IsNullOrEmpty(context.OutputDirectory)
                ? ProjectPaths.ProjectRoot
                : context.OutputDirectory;

            // MakeAbsolute collapses "..", so the containment check below cannot be fooled.
            return ProjectPaths.MakeAbsolute(Path.Combine(root, resolved));
        }

        private void CopySingleFile(BuildContext context, string source, string destination)
        {
            // A destination that looks like a folder receives the file, otherwise it names it.
            var target = Directory.Exists(destination) || string.IsNullOrEmpty(Path.GetExtension(destination))
                ? Path.Combine(destination, Path.GetFileName(source))
                : destination;

            if (context.DryRun)
            {
                context.Log.Info($"[dry run] Would copy '{source}' → '{target}'.");
                return;
            }

            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.Copy(source, target, m_Overwrite);
            context.AddArtifact(target);
            context.Log.Info($"Copied '{Path.GetFileName(source)}' → '{target}'.");
        }

        private void CopyDirectory(BuildContext context, string source, string destination)
        {
            var pattern = string.IsNullOrWhiteSpace(m_SearchPattern) ? "*" : m_SearchPattern.Trim();
            var option = m_Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var normalizedSource = ProjectPaths.Normalize(source);

            // EnumerateFiles streams; GetFiles would materialise an array of every path first,
            // which on a large content folder is a needless multi-hundred-megabyte allocation.
            var files = Directory.EnumerateFiles(source, pattern, option)
                .Where(file => !file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));

            if (context.DryRun)
            {
                context.Log.Info($"[dry run] Would copy {files.Count()} file(s) from '{source}' to '{destination}'.");
                return;
            }

            var copied = 0;
            long bytes = 0;
            var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var relative = ProjectPaths.Normalize(file).Substring(normalizedSource.Length).TrimStart('/');
                var target = Path.Combine(destination, relative);

                var directory = Path.GetDirectoryName(target);

                // Deep trees repeat the same directory thousands of times; CreateDirectory hits
                // the filesystem every call, so remember the ones already made.
                if (!string.IsNullOrEmpty(directory) && createdDirectories.Add(directory))
                    Directory.CreateDirectory(directory);

                File.Copy(file, target, m_Overwrite);

                copied++;
                bytes += SafeLength(file);

                if (copied % 500 == 0)
                    context.Log.Info($"Copied {copied} file(s)…");
            }

            context.AddArtifact(destination);
            context.Log.Info($"Copied {copied} file(s) ({BuildTargetUtility.FormatSize(bytes)}) "
                             + $"from '{source}' to '{destination}'.");
        }

        private static long SafeLength(string file)
        {
            try
            {
                return new FileInfo(file).Length;
            }
            catch (IOException)
            {
                return 0;
            }
        }
    }
}
