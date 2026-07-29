using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Runs an arbitrary shell command with full token replacement — upload to a distribution
    /// service, notarise a macOS build, run a linter, kick off a downstream job.
    ///
    /// Every build variable is also exported to the child process as an environment variable
    /// (<c>BMK_OUTPUT_PATH</c>, <c>BMK_VERSION</c>, <c>BMK_ENV</c>, …) so scripts can read them
    /// without any token wiring.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Automation/Run Shell Command",
        Tooltip = "Executes a shell command with token replacement.",
        Order = 30)]
    public sealed class RunShellCommandStep : BuildStep
    {
        [Tooltip("Command line to execute. Tokens such as {outputPath} and {version} are replaced.")]
        [TextArea(2, 8)]
        [SerializeField] private string m_Command = "echo Built {productName} {version} to {outputPath}";

        [Tooltip("Working directory. Relative paths resolve against the project root.")]
        [SerializeField] private string m_WorkingDirectory = string.Empty;

        [Tooltip("Kill the command after this many seconds.")]
        [SerializeField, Min(1)] private int m_TimeoutSeconds = 600;

        [Tooltip("Fail the build when the command exits with a non-zero code.")]
        [SerializeField] private bool m_FailOnNonZeroExitCode = true;

        [Tooltip("Mirror the command output into the build log.")]
        [SerializeField] private bool m_StreamOutput = true;

        /// <inheritdoc />
        public override string Summary
        {
            get
            {
                if (string.IsNullOrWhiteSpace(m_Command))
                    return string.Empty;

                var firstLine = m_Command.Split('\n')[0].Trim();
                return firstLine.Length <= 70 ? firstLine : firstLine.Substring(0, 67) + "…";
            }
        }

        /// <inheritdoc />
        public override void Validate(BuildContext context, BuildValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(m_Command))
                report.AddError("Command is empty.");
        }

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var command = context.Resolve(m_Command).Trim();
            var workingDirectory = string.IsNullOrWhiteSpace(m_WorkingDirectory)
                ? ProjectPaths.ProjectRoot
                : context.ResolvePath(m_WorkingDirectory);

            if (context.DryRun)
            {
                context.Log.Info($"[dry run] Would run in '{workingDirectory}': {command}");
                return;
            }

            context.Log.Info($"$ {command}");

            var result = ProcessRunner.RunShell(
                command,
                workingDirectory,
                m_TimeoutSeconds * 1000,
                BuildEnvironmentVariables(context),
                m_StreamOutput
                    ? (line, isError) => context.Log.Write(isError ? BuildLogLevel.Warning : BuildLogLevel.Info, line)
                    : (Action<string, bool>)null);

            if (result.TimedOut)
                throw new BuildStepException($"The command timed out after {m_TimeoutSeconds}s.");

            context.SetVariable("lastShellExitCode", result.ExitCode.ToString());
            context.SetVariable("lastShellOutput", result.Trimmed);

            if (result.ExitCode == 0)
            {
                context.Log.Info("Command finished with exit code 0.");
                return;
            }

            var message = $"The command exited with code {result.ExitCode}.";

            if (m_FailOnNonZeroExitCode)
                throw new BuildStepException(message);

            context.Log.Warning(message);
        }

        private static IReadOnlyDictionary<string, string> BuildEnvironmentVariables(BuildContext context)
        {
            var variables = new Dictionary<string, string>
            {
                ["BMK_OUTPUT_PATH"] = context.OutputPath,
                ["BMK_OUTPUT_DIR"] = context.OutputDirectory,
                ["BMK_VERSION"] = context.Version,
                ["BMK_BUILD_NUMBER"] = context.BuildNumber.ToString(),
                ["BMK_ENV"] = context.Environment != null ? context.Environment.Id : string.Empty,
                ["BMK_PROFILE"] = context.Profile != null ? context.Profile.Id : string.Empty,
                ["BMK_TARGET"] = context.Target.ToString(),
                ["BMK_PROJECT_ROOT"] = ProjectPaths.ProjectRoot,
                ["BMK_GIT_BRANCH"] = context.Git != null ? context.Git.Branch : string.Empty,
                ["BMK_GIT_COMMIT"] = context.Git != null ? context.Git.ShortCommit : string.Empty,
                ["BMK_STATUS"] = context.Status.ToString()
            };

            foreach (var pair in context.Variables)
                variables["BMK_" + pair.Key.ToUpperInvariant().Replace(' ', '_')] = pair.Value;

            return variables;
        }
    }
}
