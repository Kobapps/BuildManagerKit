using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Everything a build step needs to know about the run it is participating in, and the only
    /// object custom steps receive. It is created by <see cref="BuildRunner"/> and stays alive for
    /// the whole run, so steps can pass data to each other through
    /// <see cref="SetVariable"/> / <see cref="GetVariable"/>.
    /// </summary>
    public sealed class BuildContext
    {
        private readonly Dictionary<string, string> m_Variables =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Case sensitive on purpose: {env} and {ENV} are two different tokens. BuildTokens falls
        // back to a case-insensitive match, so user variables still resolve in any casing.
        private readonly Dictionary<string, string> m_Tokens =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly List<string> m_Artifacts = new List<string>();

        // Paired with the list purely for the duplicate test: a step that registers one artifact
        // per copied file would otherwise make AddArtifact O(n²).
        private readonly HashSet<string> m_ArtifactSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal BuildContext(IBuildLog log)
        {
            Log = log ?? new BuildLog();
            StartTime = DateTime.Now;
            Git = GitInfo.None;
            IsBatchMode = Application.isBatchMode;
        }

        /// <summary>The settings asset driving the run.</summary>
        public BuildManagerSettings Settings { get; internal set; }

        /// <summary>The profile being built.</summary>
        public BuildTargetProfile Profile { get; internal set; }

        /// <summary>The environment the profile is being built with.</summary>
        public BuildEnvironment Environment { get; internal set; }

        /// <summary>The platform being built.</summary>
        public BuildTarget Target { get; internal set; }

        /// <summary>The named target used by the PlayerSettings API.</summary>
        public NamedBuildTarget NamedTarget { get; internal set; }

        /// <summary>Player or dedicated server.</summary>
        public StandaloneBuildSubtarget StandaloneSubtarget { get; internal set; }

        /// <summary>
        /// Version string applied to the player, e.g. <c>1.4.2</c>. Pre build steps may change it;
        /// call <see cref="RefreshTokens"/> afterwards so <c>{version}</c> picks the new value up.
        /// </summary>
        public string Version { get; set; } = "0.0.0";

        /// <summary>Numeric build counter applied to the player. Pre build steps may change it.</summary>
        public int BuildNumber { get; set; }

        /// <summary>Absolute folder the player is written to.</summary>
        public string OutputDirectory { get; internal set; } = string.Empty;

        /// <summary>Absolute path passed to <c>BuildPipeline.BuildPlayer</c>.</summary>
        public string OutputPath { get; internal set; } = string.Empty;

        /// <summary>Player file name including its extension.</summary>
        public string ExecutableName { get; internal set; } = string.Empty;

        /// <summary>Scene paths included in the build, in order.</summary>
        public string[] Scenes { get; internal set; } = Array.Empty<string>();

        /// <summary>Effective development-build flag after environment overrides.</summary>
        public bool DevelopmentBuild { get; internal set; }

        /// <summary>Scripting defines applied for this build.</summary>
        public string[] ScriptingDefines { get; set; } = Array.Empty<string>();

        /// <summary>Git state captured when the run started.</summary>
        public GitInfo Git { get; internal set; }

        /// <summary>Local time the run started.</summary>
        public DateTime StartTime { get; internal set; }

        /// <summary>True when Unity runs with <c>-batchmode</c>.</summary>
        public bool IsBatchMode { get; internal set; }

        /// <summary>
        /// True when the run only validates the configuration. Steps with side effects should
        /// check this and log what they would have done instead of doing it.
        /// </summary>
        public bool DryRun { get; internal set; }

        /// <summary>Which phase is currently executing.</summary>
        public BuildPhase Phase { get; internal set; } = BuildPhase.Idle;

        /// <summary>Unity's build report. Null until the player build has finished.</summary>
        public BuildReport Report { get; internal set; }

        /// <summary>Current outcome of the run.</summary>
        public BuildRunStatus Status { get; internal set; } = BuildRunStatus.Unknown;

        /// <summary>Where steps write their progress.</summary>
        public IBuildLog Log { get; }

        /// <summary>Files and folders produced by the run, in the order they were added.</summary>
        public IReadOnlyList<string> Artifacts => m_Artifacts;

        /// <summary>Reason the run failed, empty while it is healthy.</summary>
        public string FailureMessage { get; private set; } = string.Empty;

        /// <summary>True once <see cref="Fail"/> has been called or the player build failed.</summary>
        public bool HasFailed => Status == BuildRunStatus.Failed;

        /// <summary>True when the user cancelled the run.</summary>
        public bool IsCancelled => Status == BuildRunStatus.Cancelled;

        /// <summary>True when the player build itself reported success.</summary>
        public bool PlayerBuildSucceeded =>
            Report != null && Report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;

        /// <summary>Every key/value pair available to steps and tokens.</summary>
        public IReadOnlyDictionary<string, string> Variables => m_Variables;

        /// <summary>Reads a variable, falling back to the process environment.</summary>
        /// <param name="key">Variable name, case-insensitive.</param>
        /// <param name="fallback">Returned when neither the context nor the process has the value.</param>
        public string GetVariable(string key, string fallback = "")
        {
            if (string.IsNullOrEmpty(key))
                return fallback;

            if (m_Variables.TryGetValue(key, out var value))
                return value;

            var fromProcess = System.Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(fromProcess) ? fallback : fromProcess;
        }

        /// <summary>Writes a variable that later steps and <c>{tokens}</c> can read.</summary>
        public void SetVariable(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                return;

            m_Variables[key] = value ?? string.Empty;
            m_Tokens[key] = value ?? string.Empty;
        }

        /// <summary>Replaces every <c>{token}</c> in <paramref name="template"/>.</summary>
        public string Resolve(string template) => BuildTokens.Resolve(template, m_Tokens, StartTime);

        /// <summary>Resolves a path template and turns it into an absolute path.</summary>
        public string ResolvePath(string template) => ProjectPaths.MakeAbsolute(Resolve(template));

        /// <summary>
        /// Registers a produced file or folder. Artifacts show up in the Editor window, in the
        /// build manifest and in the JSON result written for CI.
        /// </summary>
        public void AddArtifact(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            var normalized = ProjectPaths.Normalize(path);
            if (m_ArtifactSet.Add(normalized))
                m_Artifacts.Add(normalized);
        }

        /// <summary>Marks the run as failed. The current phase finishes, then the run aborts.</summary>
        public void Fail(string message)
        {
            if (Status != BuildRunStatus.Failed)
            {
                Status = BuildRunStatus.Failed;
                FailureMessage = message ?? "Build failed.";
            }

            Log.Error(message);
        }

        /// <summary>Marks the run as cancelled.</summary>
        public void Cancel(string reason = "Cancelled by user.")
        {
            if (Status == BuildRunStatus.Unknown || Status == BuildRunStatus.Succeeded)
            {
                Status = BuildRunStatus.Cancelled;
                FailureMessage = reason;
            }
        }

        /// <summary>
        /// Recomputes the token table. Call it after changing <see cref="Version"/> or any other
        /// value a <c>{token}</c> is derived from.
        /// </summary>
        public void RefreshTokens()
        {
            var extension = BuildTargetUtility.GetPlayerExtension(
                Target,
                Profile != null && Profile.Android.buildAppBundle);

            m_Tokens["projectRoot"] = ProjectPaths.ProjectRoot;
            m_Tokens["projectName"] = ProjectPaths.ProjectName;
            m_Tokens["productName"] = PlayerSettings.productName;
            m_Tokens["companyName"] = PlayerSettings.companyName;
            m_Tokens["bundleId"] = SafeGetApplicationIdentifier();
            m_Tokens["profile"] = Profile != null ? Profile.Id : string.Empty;
            m_Tokens["profileName"] = Profile != null ? Profile.DisplayName : string.Empty;
            m_Tokens["env"] = Environment != null ? Environment.Id : string.Empty;
            m_Tokens["ENV"] = Environment != null ? Environment.Id.ToUpperInvariant() : string.Empty;
            m_Tokens["envName"] = Environment != null ? Environment.DisplayName : string.Empty;
            m_Tokens["target"] = Target.ToString();
            m_Tokens["targetShort"] = BuildTargetUtility.GetShortName(Target);
            m_Tokens["platform"] = BuildPipeline.GetBuildTargetGroup(Target).ToString();
            m_Tokens["version"] = Version;
            m_Tokens["versionDots"] = (Version ?? string.Empty).Replace(".", string.Empty);
            m_Tokens["buildNumber"] = BuildNumber.ToString(CultureInfo.InvariantCulture);
            m_Tokens["executable"] = ExecutableName;
            m_Tokens["extension"] = extension;
            m_Tokens["branch"] = Git != null ? Git.Branch : string.Empty;
            m_Tokens["commit"] = Git != null ? Git.ShortCommit : string.Empty;
            m_Tokens["commitLong"] = Git != null ? Git.Commit : string.Empty;
            m_Tokens["tag"] = Git != null ? Git.Tag : string.Empty;
            m_Tokens["dirty"] = Git != null && Git.IsDirty ? "dirty" : string.Empty;
            m_Tokens["user"] = SafeUserName();
            m_Tokens["machine"] = SafeMachineName();
            m_Tokens["buildType"] = DevelopmentBuild ? "Development" : "Release";
            m_Tokens["outputDir"] = OutputDirectory;
            m_Tokens["outputPath"] = OutputPath;

            // User variables win over built-ins so environments can override anything.
            foreach (var pair in m_Variables)
                m_Tokens[pair.Key] = pair.Value;
        }

        /// <summary>Copies the environment variables into the context.</summary>
        internal void ApplyEnvironmentVariables(BuildEnvironment environment)
        {
            if (environment == null)
                return;

            foreach (var variable in environment.Variables)
            {
                if (!string.IsNullOrEmpty(variable.key))
                    m_Variables[variable.key] = variable.value ?? string.Empty;
            }
        }

        /// <summary>Builds the machine readable summary written for CI consumers.</summary>
        internal BuildRunResult ToResult(TimeSpan duration)
        {
            var size = string.IsNullOrEmpty(OutputPath) ? 0 : BuildTargetUtility.GetOutputSize(OutputPath);

            return new BuildRunResult
            {
                status = Status,
                statusText = Status.ToString(),
                profileId = Profile != null ? Profile.Id : string.Empty,
                profileName = Profile != null ? Profile.DisplayName : string.Empty,
                environmentId = Environment != null ? Environment.Id : string.Empty,
                target = Target.ToString(),
                version = Version,
                buildNumber = BuildNumber,
                outputPath = OutputPath,
                outputSizeBytes = size,
                durationSeconds = duration.TotalSeconds,
                message = string.IsNullOrEmpty(FailureMessage)
                    ? (Status == BuildRunStatus.Succeeded ? "Build succeeded." : string.Empty)
                    : FailureMessage,
                startedAtUtc = StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                gitBranch = Git != null ? Git.Branch : string.Empty,
                gitCommit = Git != null ? Git.ShortCommit : string.Empty,
                errors = Report != null ? (int)Report.summary.totalErrors : 0,
                warnings = Report != null ? (int)Report.summary.totalWarnings : 0,
                artifacts = m_Artifacts.ToArray()
            };
        }

        private string SafeGetApplicationIdentifier()
        {
            try
            {
                return PlayerSettings.GetApplicationIdentifier(NamedTarget);
            }
            catch (Exception)
            {
                return Application.identifier;
            }
        }

        private static string SafeUserName()
        {
            try
            {
                return System.Environment.UserName;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static string SafeMachineName()
        {
            try
            {
                return System.Environment.MachineName;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        /// <summary>Convenience wrapper that creates <paramref name="directory"/> unless dry running.</summary>
        public void EnsureDirectory(string directory)
        {
            if (DryRun || string.IsNullOrEmpty(directory))
                return;

            Directory.CreateDirectory(directory);
        }
    }
}
