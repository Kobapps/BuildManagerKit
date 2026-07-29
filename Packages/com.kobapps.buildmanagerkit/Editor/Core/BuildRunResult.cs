using System;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Machine readable outcome of a single build run. Serialised to JSON for CI consumers via
    /// <c>-bmkResultFile</c> and stored in the build history.
    /// </summary>
    [Serializable]
    public sealed class BuildRunResult
    {
        /// <summary>Final state of the run.</summary>
        public BuildRunStatus status = BuildRunStatus.Unknown;

        /// <summary>
        /// <see cref="status"/> as text. JsonUtility writes enums as integers, so CI scripts
        /// should read this field instead.
        /// </summary>
        public string statusText = BuildRunStatus.Unknown.ToString();

        /// <summary>Identifier of the profile that was built.</summary>
        public string profileId = string.Empty;

        /// <summary>Display name of the profile that was built.</summary>
        public string profileName = string.Empty;

        /// <summary>Identifier of the environment used.</summary>
        public string environmentId = string.Empty;

        /// <summary>Name of the <c>BuildTarget</c> that was built.</summary>
        public string target = string.Empty;

        /// <summary>Version string applied to the player.</summary>
        public string version = string.Empty;

        /// <summary>Build counter applied to the player.</summary>
        public int buildNumber;

        /// <summary>Absolute path of the produced player.</summary>
        public string outputPath = string.Empty;

        /// <summary>Total size of the output in bytes.</summary>
        public long outputSizeBytes;

        /// <summary>Wall clock duration of the whole run, including pre and post steps.</summary>
        public double durationSeconds;

        /// <summary>Success message, or the reason the run failed.</summary>
        public string message = string.Empty;

        /// <summary>UTC start timestamp in round-trip ("o") format.</summary>
        public string startedAtUtc = string.Empty;

        /// <summary>Git branch the build was made from.</summary>
        public string gitBranch = string.Empty;

        /// <summary>Short git commit the build was made from.</summary>
        public string gitCommit = string.Empty;

        /// <summary>Error count reported by Unity's build report.</summary>
        public int errors;

        /// <summary>Warning count reported by Unity's build report.</summary>
        public int warnings;

        /// <summary>Files and folders produced by the run.</summary>
        public string[] artifacts = Array.Empty<string>();

        /// <summary>Absolute path of the full text log written for this run.</summary>
        public string logFile = string.Empty;

        /// <summary>Complete log text, included so CI can print it without a second file read.</summary>
        public string log = string.Empty;

        /// <summary>True when the run finished successfully.</summary>
        public bool Succeeded => status == BuildRunStatus.Succeeded;

        /// <summary>Formats the result as pretty printed JSON.</summary>
        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>One line summary suitable for a CI log or a notification.</summary>
        public string ToSummaryLine()
        {
            var duration = BuildTargetUtility.FormatDuration(TimeSpan.FromSeconds(durationSeconds));
            var size = BuildTargetUtility.FormatSize(outputSizeBytes);
            return $"{status}: {profileName} [{environmentId}] {version}+{buildNumber} in {duration} ({size})";
        }
    }

    /// <summary>Aggregated outcome of a queue that built several profiles.</summary>
    [Serializable]
    public sealed class BuildQueueResult
    {
        /// <summary>Name of the queue that was executed.</summary>
        public string queueName = string.Empty;

        /// <summary>Result of every entry, in execution order.</summary>
        public BuildRunResult[] results = Array.Empty<BuildRunResult>();

        /// <summary>Total wall clock duration of the queue.</summary>
        public double durationSeconds;

        /// <summary>True when every entry succeeded.</summary>
        public bool Succeeded
        {
            get
            {
                if (results == null || results.Length == 0)
                    return false;

                foreach (var result in results)
                {
                    if (!result.Succeeded)
                        return false;
                }

                return true;
            }
        }

        /// <summary>Formats the queue result as pretty printed JSON.</summary>
        public string ToJson() => JsonUtility.ToJson(this, true);
    }
}
