using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>A single line of build output.</summary>
    [Serializable]
    public struct BuildLogEntry
    {
        public BuildLogLevel level;
        public string message;
        public string scope;
        public double elapsedSeconds;

        public string Format() =>
            string.IsNullOrEmpty(scope)
                ? $"[{elapsedSeconds,7:0.00}s] {message}"
                : $"[{elapsedSeconds,7:0.00}s] [{scope}] {message}";
    }

    /// <summary>Sink build steps write their progress to.</summary>
    public interface IBuildLog
    {
        /// <summary>Writes a line at the given severity.</summary>
        void Write(BuildLogLevel level, string message);

        /// <summary>Convenience wrapper for <see cref="BuildLogLevel.Info"/>.</summary>
        void Info(string message);

        /// <summary>Convenience wrapper for <see cref="BuildLogLevel.Success"/>.</summary>
        void Success(string message);

        /// <summary>Convenience wrapper for <see cref="BuildLogLevel.Warning"/>.</summary>
        void Warning(string message);

        /// <summary>Convenience wrapper for <see cref="BuildLogLevel.Error"/>.</summary>
        void Error(string message);

        /// <summary>Prefix applied to subsequent lines, usually the running step name.</summary>
        string Scope { get; set; }
    }

    /// <summary>
    /// Collects the output of a build run. Lines are mirrored to the Unity console (and to
    /// stdout in batch mode) so CI logs stay readable, and kept in memory so the Editor window
    /// can show a live console.
    /// </summary>
    public sealed class BuildLog : IBuildLog
    {
        /// <summary>
        /// How many lines are kept in memory. A shell step streaming the output of a large
        /// asset sync can emit hundreds of thousands of lines; without a ceiling the Editor's
        /// memory grows with them and the JSON result becomes unusable.
        /// </summary>
        public const int MaxEntries = 20000;

        /// <summary>Lines dropped from the front once <see cref="MaxEntries"/> is reached.</summary>
        private const int k_TrimBlock = 2000;

        private readonly List<BuildLogEntry> m_Entries = new List<BuildLogEntry>();
        private readonly System.Diagnostics.Stopwatch m_Stopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int m_DroppedEntries;

        /// <summary>Raised on the main thread every time a line is appended.</summary>
        public event Action<BuildLogEntry> EntryAdded;

        /// <summary>Every line written so far.</summary>
        public IReadOnlyList<BuildLogEntry> Entries => m_Entries;

        /// <summary>Number of warnings written so far.</summary>
        public int WarningCount { get; private set; }

        /// <summary>Number of errors written so far.</summary>
        public int ErrorCount { get; private set; }

        /// <summary>
        /// Lines discarded because the run exceeded <see cref="MaxEntries"/>. The persisted log
        /// file records that they were dropped.
        /// </summary>
        public int DroppedEntryCount => m_DroppedEntries;

        /// <summary>Mirror every line to the Unity console. Disabled during dry runs.</summary>
        public bool MirrorToConsole { get; set; } = true;

        /// <inheritdoc />
        public string Scope { get; set; }

        /// <inheritdoc />
        public void Write(BuildLogLevel level, string message)
        {
            var entry = new BuildLogEntry
            {
                level = level,
                message = message ?? string.Empty,
                scope = Scope,
                elapsedSeconds = m_Stopwatch.Elapsed.TotalSeconds
            };

            m_Entries.Add(entry);

            // Drop in blocks rather than one line at a time: RemoveRange on a List is O(n), so
            // trimming on every append would turn a chatty step into an O(n²) stall.
            if (m_Entries.Count > MaxEntries)
            {
                m_Entries.RemoveRange(0, k_TrimBlock);
                m_DroppedEntries += k_TrimBlock;
            }

            if (level == BuildLogLevel.Warning)
                WarningCount++;
            else if (level == BuildLogLevel.Error)
                ErrorCount++;

            if (MirrorToConsole)
            {
                var line = "[BuildManagerKit] " + entry.Format();
                switch (level)
                {
                    case BuildLogLevel.Warning:
                        Debug.LogWarning(line);
                        break;
                    case BuildLogLevel.Error:
                        Debug.LogError(line);
                        break;
                    default:
                        Debug.Log(line);
                        break;
                }
            }

            EntryAdded?.Invoke(entry);
        }

        /// <inheritdoc />
        public void Info(string message) => Write(BuildLogLevel.Info, message);

        /// <inheritdoc />
        public void Success(string message) => Write(BuildLogLevel.Success, message);

        /// <inheritdoc />
        public void Warning(string message) => Write(BuildLogLevel.Warning, message);

        /// <inheritdoc />
        public void Error(string message) => Write(BuildLogLevel.Error, message);

        /// <summary>Removes every line and resets the counters.</summary>
        public void Clear()
        {
            m_Entries.Clear();
            WarningCount = 0;
            ErrorCount = 0;
            m_DroppedEntries = 0;
            m_Stopwatch.Restart();
        }

        /// <summary>Renders the whole log as plain text.</summary>
        public string ToPlainText()
        {
            var builder = new StringBuilder();

            if (m_DroppedEntries > 0)
                builder.AppendLine(
                    $"… {m_DroppedEntries} earlier line(s) dropped: the run exceeded the {MaxEntries} line buffer.");

            foreach (var entry in m_Entries)
            {
                builder.Append(entry.level.ToString().ToUpperInvariant().PadRight(7));
                builder.AppendLine(entry.Format());
            }

            return builder.ToString();
        }

        /// <summary>
        /// Renders the log capped to <paramref name="maxCharacters"/>, keeping the tail — where
        /// the failure almost always is — and noting how much was cut. Used for the copy embedded
        /// in the JSON result, which CI systems parse and must not be hundreds of megabytes.
        /// </summary>
        public string ToPlainText(int maxCharacters)
        {
            var text = ToPlainText();

            if (maxCharacters <= 0 || text.Length <= maxCharacters)
                return text;

            var removed = text.Length - maxCharacters;
            return $"… {removed} character(s) of earlier output omitted; see the log file for the full text.\n"
                   + text.Substring(removed);
        }

        /// <summary>Writes the log next to the build output. Failures here never fail a build.</summary>
        public void SaveTo(string path)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var header = $"Build Manager Kit log — {DateTime.Now.ToString("u", CultureInfo.InvariantCulture)}"
                             + Environment.NewLine
                             + new string('-', 72)
                             + Environment.NewLine;

                File.WriteAllText(path, header + ToPlainText());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BuildManagerKit] Could not write build log to '{path}': {exception.Message}");
            }
        }
    }
}
