using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>Writes a line into the build log. Useful as a marker while authoring a pipeline.</summary>
    [Serializable]
    [BuildStepMenu("Utility/Log Message", Tooltip = "Writes a message into the build log.", Order = 100)]
    public sealed class LogMessageStep : BuildStep
    {
        [TextArea(1, 4)]
        [SerializeField] private string m_Message = "Building {productName} {version} for {targetShort}";

        [SerializeField] private BuildLogLevel m_Level = BuildLogLevel.Info;

        /// <inheritdoc />
        public override string Summary => m_Message;

        /// <inheritdoc />
        public override void Execute(BuildContext context) =>
            context.Log.Write(m_Level, context.Resolve(m_Message));
    }

    /// <summary>Opens the output folder in Finder or Explorer once the build is done.</summary>
    [Serializable]
    [BuildStepMenu("Utility/Reveal Output Folder",
        Tooltip = "Opens the build output in the OS file browser.",
        Scope = BuildStepScope.PostBuild,
        Order = 110)]
    public sealed class RevealOutputStep : BuildStep
    {
        [Tooltip("Skip this action when Unity runs in batch mode, where there is no desktop.")]
        [SerializeField] private bool m_SkipInBatchMode = true;

        /// <inheritdoc />
        public override string Summary => "Open {outputDir}";

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            if (context.DryRun)
                return;

            if (m_SkipInBatchMode && context.IsBatchMode)
            {
                context.Log.Info("Batch mode: not opening the output folder.");
                return;
            }

            if (!Directory.Exists(context.OutputDirectory))
            {
                context.Log.Warning($"'{context.OutputDirectory}' does not exist.");
                return;
            }

            EditorUtility.RevealInFinder(context.OutputPath);
        }
    }

    /// <summary>
    /// Writes a text file into the build output — a version stamp, release notes, a launcher
    /// script. The content is token substituted, so it can embed anything about the run.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Files/Write Text File",
        Tooltip = "Writes a token substituted text file into the build output.",
        Order = 15)]
    public sealed class WriteTextFileStep : BuildStep
    {
        [Tooltip("Destination. Relative paths resolve against the build output folder.")]
        [SerializeField] private string m_Path = "version.txt";

        [TextArea(3, 12)]
        [SerializeField] private string m_Content =
            "{productName} {version}+{buildNumber}\n{envName} · {target}\n{branch}@{commit}\nBuilt {datetime}";

        [Tooltip("Append instead of overwriting when the file already exists.")]
        [SerializeField] private bool m_Append;

        /// <inheritdoc />
        public override string Summary => m_Path;

        /// <inheritdoc />
        public override void Validate(BuildContext context, BuildValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(m_Path))
                report.AddError("Destination path is empty.");
        }

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var resolved = context.Resolve(m_Path);
            var target = Path.IsPathRooted(resolved)
                ? resolved
                : Path.Combine(
                    string.IsNullOrEmpty(context.OutputDirectory) ? ProjectPaths.ProjectRoot : context.OutputDirectory,
                    resolved);

            var content = context.Resolve(m_Content).Replace("\\n", "\n");

            if (context.DryRun)
            {
                context.Log.Info($"[dry run] Would write {content.Length} character(s) to '{target}'.");
                return;
            }

            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (m_Append)
                File.AppendAllText(target, content + System.Environment.NewLine);
            else
                File.WriteAllText(target, content);

            context.AddArtifact(target);
            context.Log.Info($"Wrote '{target}'.");
        }
    }
}
