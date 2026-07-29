using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildManagerKit.Editor
{
    /// <summary>A single problem found while validating a configuration.</summary>
    public readonly struct BuildValidationIssue
    {
        /// <summary>True when the issue blocks the build.</summary>
        public bool IsError { get; }

        /// <summary>What is wrong.</summary>
        public string Message { get; }

        /// <summary>Where the problem was found, e.g. the step title.</summary>
        public string Source { get; }

        internal BuildValidationIssue(bool isError, string message, string source)
        {
            IsError = isError;
            Message = message;
            Source = source;
        }

        /// <summary>Formats the issue as one line.</summary>
        public override string ToString() =>
            string.IsNullOrEmpty(Source) ? Message : $"{Source}: {Message}";
    }

    /// <summary>
    /// Collects configuration problems found before a build starts. Errors abort the run,
    /// warnings are logged and the build continues.
    /// </summary>
    public sealed class BuildValidationReport
    {
        private readonly List<BuildValidationIssue> m_Issues = new List<BuildValidationIssue>();

        /// <summary>Source attributed to issues added without an explicit one.</summary>
        public string CurrentSource { get; set; } = string.Empty;

        /// <summary>Every issue found, in the order it was reported.</summary>
        public IReadOnlyList<BuildValidationIssue> Issues => m_Issues;

        /// <summary>True when at least one blocking issue was reported.</summary>
        public bool HasErrors => m_Issues.Any(issue => issue.IsError);

        /// <summary>True when at least one non-blocking issue was reported.</summary>
        public bool HasWarnings => m_Issues.Any(issue => !issue.IsError);

        /// <summary>Number of blocking issues.</summary>
        public int ErrorCount => m_Issues.Count(issue => issue.IsError);

        /// <summary>Number of non-blocking issues.</summary>
        public int WarningCount => m_Issues.Count(issue => !issue.IsError);

        /// <summary>Reports a blocking problem.</summary>
        public void AddError(string message, string source = null) =>
            m_Issues.Add(new BuildValidationIssue(true, message, source ?? CurrentSource));

        /// <summary>Reports a non-blocking problem.</summary>
        public void AddWarning(string message, string source = null) =>
            m_Issues.Add(new BuildValidationIssue(false, message, source ?? CurrentSource));

        /// <summary>Removes every issue.</summary>
        public void Clear() => m_Issues.Clear();

        /// <summary>Writes every issue into a build log at the matching severity.</summary>
        public void WriteTo(IBuildLog log)
        {
            foreach (var issue in m_Issues)
            {
                if (issue.IsError)
                    log.Error(issue.ToString());
                else
                    log.Warning(issue.ToString());
            }
        }

        /// <summary>Renders every issue as a multi line string.</summary>
        public override string ToString()
        {
            var builder = new StringBuilder();
            foreach (var issue in m_Issues)
                builder.AppendLine((issue.IsError ? "ERROR   " : "WARNING ") + issue);

            return builder.ToString();
        }
    }
}
