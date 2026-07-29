using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace BuildManagerKit.Editor
{
    /// <summary>Outcome of an external process launched by <see cref="ProcessRunner"/>.</summary>
    public sealed class ProcessResult
    {
        /// <summary>Exit code reported by the process, or -1 when it timed out.</summary>
        public int ExitCode { get; internal set; }

        /// <summary>Everything the process wrote to stdout.</summary>
        public string StandardOutput { get; internal set; } = string.Empty;

        /// <summary>Everything the process wrote to stderr.</summary>
        public string StandardError { get; internal set; } = string.Empty;

        /// <summary>True when the process was killed because it exceeded the timeout.</summary>
        public bool TimedOut { get; internal set; }

        /// <summary>
        /// True when the captured output hit <see cref="ProcessRunner.MaxCapturedCharacters"/> and
        /// was cut short. Streaming callbacks still saw every line.
        /// </summary>
        public bool OutputTruncated { get; internal set; }

        /// <summary>True when the process exited with code 0 and did not time out.</summary>
        public bool Succeeded => !TimedOut && ExitCode == 0;

        /// <summary>stdout with trailing whitespace removed, handy for one-line commands.</summary>
        public string Trimmed => StandardOutput?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Synchronous process helper used by the shell step and the git integration. Everything runs
    /// on the calling thread because builds are synchronous anyway, which keeps ordering
    /// predictable in batch mode.
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>Default timeout applied when a step does not specify one.</summary>
        public const int DefaultTimeoutMs = 10 * 60 * 1000;

        /// <summary>
        /// Ceiling on the text captured per stream. Tools that walk a large project — asset
        /// syncs, symbol uploads, linters — can emit tens of megabytes, and holding all of it just
        /// to hand back a string nobody reads is how an Editor runs out of memory mid-build.
        /// </summary>
        public const int MaxCapturedCharacters = 4 * 1024 * 1024;

        /// <summary>
        /// Runs <paramref name="fileName"/> and waits for it to exit.
        /// </summary>
        /// <param name="fileName">Executable to run.</param>
        /// <param name="arguments">Command line arguments.</param>
        /// <param name="workingDirectory">Working directory, defaults to the project root.</param>
        /// <param name="timeoutMs">Kill the process after this many milliseconds.</param>
        /// <param name="environment">Extra environment variables for the child process.</param>
        /// <param name="onLine">Invoked for every line of stdout/stderr as it arrives.</param>
        public static ProcessResult Run(
            string fileName,
            string arguments,
            string workingDirectory = null,
            int timeoutMs = DefaultTimeoutMs,
            IReadOnlyDictionary<string, string> environment = null,
            Action<string, bool> onLine = null)
        {
            var result = new ProcessResult();
            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            var truncated = false;

            // Appends until the cap, then stops growing. The line still reaches onLine, so the
            // build log (which has its own ring buffer) keeps showing live progress.
            void Capture(StringBuilder buffer, string line)
            {
                if (buffer.Length >= MaxCapturedCharacters)
                {
                    truncated = true;
                    return;
                }

                buffer.AppendLine(line);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = string.IsNullOrEmpty(workingDirectory)
                    ? ProjectPaths.ProjectRoot
                    : workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (environment != null)
            {
                foreach (var pair in environment)
                    startInfo.EnvironmentVariables[pair.Key] = pair.Value;
            }

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                        return;

                    Capture(standardOutput, args.Data);
                    onLine?.Invoke(args.Data, false);
                };

                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                        return;

                    Capture(standardError, args.Data);
                    onLine?.Invoke(args.Data, true);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (process.WaitForExit(timeoutMs))
                {
                    // Give the async readers a moment to flush the final lines.
                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                }
                else
                {
                    result.TimedOut = true;
                    result.ExitCode = -1;

                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited between the timeout and the kill.
                    }
                }
            }

            result.StandardOutput = standardOutput.ToString();
            result.StandardError = standardError.ToString();
            result.OutputTruncated = truncated;
            return result;
        }

        /// <summary>
        /// Runs a command through the platform shell (<c>cmd /c</c> on Windows, <c>/bin/bash -lc</c>
        /// elsewhere) so pipes, globs and shell built-ins work as authored.
        /// </summary>
        public static ProcessResult RunShell(
            string command,
            string workingDirectory = null,
            int timeoutMs = DefaultTimeoutMs,
            IReadOnlyDictionary<string, string> environment = null,
            Action<string, bool> onLine = null)
        {
#if UNITY_EDITOR_WIN
            const string shell = "cmd.exe";
            var arguments = "/c " + command;
#else
            const string shell = "/bin/bash";
            var arguments = "-lc " + Quote(command);
#endif
            return Run(shell, arguments, workingDirectory, timeoutMs, environment, onLine);
        }

        /// <summary>Wraps a value in double quotes, escaping inner quotes and backslashes.</summary>
        public static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
