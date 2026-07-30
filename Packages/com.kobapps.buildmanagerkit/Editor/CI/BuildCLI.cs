using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Command line entry points for headless builds.
    ///
    /// Every method here is safe to pass to <c>-executeMethod</c>. They parse their own arguments,
    /// print a machine readable summary and exit with a meaningful code, so a CI job needs a
    /// single line:
    /// <code>
    /// Unity -batchmode -nographics -quit=false -projectPath . \
    ///       -executeMethod BuildManagerKit.Editor.BuildCLI.Build \
    ///       -bmkProfile android -bmkEnv prod -bmkResultFile build-result.json
    /// </code>
    /// </summary>
    public static class BuildCLI
    {
        /// <summary>Exit code used when everything worked.</summary>
        public const int ExitSuccess = 0;

        /// <summary>Exit code used when the build itself failed.</summary>
        public const int ExitBuildFailed = 1;

        /// <summary>Exit code used when the arguments or configuration are wrong.</summary>
        public const int ExitUsageError = 2;

        /// <summary>Exit code used when the build was cancelled.</summary>
        public const int ExitCancelled = 3;

        /// <summary>
        /// Builds a single profile.
        ///
        /// Arguments: <c>-bmkProfile</c> (required), <c>-bmkEnv</c>, <c>-bmkOutput</c>,
        /// <c>-bmkVersion</c>, <c>-bmkBuildNumber</c>, <c>-bmkDefines</c>, <c>-bmkResultFile</c>,
        /// <c>-bmkDryRun</c>, <c>-bmkRun</c>, <c>-bmkNoExit</c>.
        ///
        /// <c>-bmkRun</c> launches the player when the build finishes. It exists for a local
        /// headless run on a developer machine; on a build server it would leave a player process
        /// behind holding the agent, so leave it off there.
        /// </summary>
        public static void Build()
        {
            var arguments = CommandLineArgs.FromProcess();
            var settings = BuildManagerSettings.Instance;

            Print($"Build Manager Kit — arguments: {arguments}");

            var profileName = arguments.GetString("bmkProfile");
            if (string.IsNullOrWhiteSpace(profileName))
            {
                Print("ERROR: -bmkProfile is required.");
                PrintUsage();
                Exit(ExitUsageError, arguments);
                return;
            }

            var profile = settings.FindProfile(profileName);
            if (profile == null)
            {
                Print($"ERROR: no build profile named '{profileName}'. Known profiles: "
                      + string.Join(", ", settings.Profiles.Where(p => p != null).Select(p => p.Id)));
                Exit(ExitUsageError, arguments);
                return;
            }

            BuildEnvironment environment = null;
            var environmentName = arguments.GetString("bmkEnv");
            if (!string.IsNullOrWhiteSpace(environmentName))
            {
                environment = settings.FindEnvironment(environmentName);
                if (environment == null)
                {
                    Print($"ERROR: no environment named '{environmentName}'. Known environments: "
                          + string.Join(", ", settings.Environments.Where(e => e != null).Select(e => e.Id)));
                    Exit(ExitUsageError, arguments);
                    return;
                }
            }

            var overrides = ReadOverrides(arguments, out var overrideError);

            if (overrideError)
            {
                Exit(ExitUsageError, arguments);
                return;
            }

            var result = BuildRunner.Run(new BuildRunRequest
            {
                Profile = profile,
                Environment = environment,
                OutputDirectoryOverride = arguments.GetString("bmkOutput"),
                VersionOverride = arguments.GetString("bmkVersion"),
                BuildNumberOverride = arguments.GetInt("bmkBuildNumber"),
                ExtraDefines = arguments.GetList("bmkDefines"),
                DryRun = arguments.GetBool("bmkDryRun"),
                RunAfterBuild = arguments.GetBool("bmkRun"),
                ResultFilePath = arguments.GetString("bmkResultFile"),
                AllowPlatformSwitch = !arguments.GetBool("bmkNoPlatformSwitch"),
                Overrides = overrides,
                Interactive = false
            });

            Print(result.ToSummaryLine());
            Exit(ExitCodeFor(result.status), arguments);
        }

        /// <summary>
        /// Runs a build queue to completion.
        ///
        /// Arguments: <c>-bmkQueue</c> (required), <c>-bmkEnv</c>, <c>-bmkResultFile</c>,
        /// <c>-bmkNoExit</c>.
        /// </summary>
        public static void BuildQueue()
        {
            var arguments = CommandLineArgs.FromProcess();
            var settings = BuildManagerSettings.Instance;

            var queueName = arguments.GetString("bmkQueue");
            if (string.IsNullOrWhiteSpace(queueName))
            {
                Print("ERROR: -bmkQueue is required.");
                PrintUsage();
                Exit(ExitUsageError, arguments);
                return;
            }

            var queue = settings.FindQueue(queueName);
            if (queue == null)
            {
                Print($"ERROR: no queue named '{queueName}'. Known queues: "
                      + string.Join(", ", settings.Queues.Where(q => q != null).Select(q => q.id)));
                Exit(ExitUsageError, arguments);
                return;
            }

            var environmentName = arguments.GetString("bmkEnv");
            var environment = string.IsNullOrWhiteSpace(environmentName)
                ? null
                : settings.FindEnvironment(environmentName);

            if (!string.IsNullOrWhiteSpace(environmentName) && environment == null)
            {
                Print($"ERROR: no environment named '{environmentName}'.");
                Exit(ExitUsageError, arguments);
                return;
            }

            var overrides = ReadOverrides(arguments, out var overrideError);

            if (overrideError)
            {
                Exit(ExitUsageError, arguments);
                return;
            }

            var result = BuildQueueRunner.RunBlocking(
                queue,
                environment,
                arguments.GetString("bmkResultFile"),
                overrides,
                arguments.GetBoolOrNull("bmkStopOnFailure"));

            foreach (var entry in result.results)
                Print("  " + entry.ToSummaryLine());

            Print($"Queue '{result.queueName}': "
                  + $"{result.results.Count(entry => entry.Succeeded)}/{result.results.Length} succeeded in "
                  + BuildTargetUtility.FormatDuration(TimeSpan.FromSeconds(result.durationSeconds)));

            Exit(result.Succeeded ? ExitSuccess : ExitBuildFailed, arguments);
        }

        /// <summary>
        /// Applies an environment to the project without building. Useful as a preparation step in
        /// a pipeline, or to make a workstation match CI.
        ///
        /// Arguments: <c>-bmkEnv</c> (required), <c>-bmkNoExit</c>.
        /// </summary>
        public static void SwitchEnvironment()
        {
            var arguments = CommandLineArgs.FromProcess();
            var settings = BuildManagerSettings.Instance;

            var environmentName = arguments.GetString("bmkEnv");
            var environment = settings.FindEnvironment(environmentName);

            if (environment == null)
            {
                Print($"ERROR: no environment named '{environmentName}'.");
                Exit(ExitUsageError, arguments);
                return;
            }

            var ok = EnvironmentManager.Activate(environment);
            Print(ok ? $"Environment '{environment.Id}' applied." : "ERROR: could not apply the environment.");
            Exit(ok ? ExitSuccess : ExitBuildFailed, arguments);
        }

        /// <summary>
        /// Switches the active build target.
        ///
        /// Arguments: <c>-bmkTarget</c> (a <c>BuildTarget</c> name, required), <c>-bmkServer</c>,
        /// <c>-bmkNoExit</c>.
        /// </summary>
        public static void SwitchPlatform()
        {
            var arguments = CommandLineArgs.FromProcess();
            var targetName = arguments.GetString("bmkTarget");

            if (!Enum.TryParse<BuildTarget>(targetName, true, out var target))
            {
                Print($"ERROR: '{targetName}' is not a valid BuildTarget.");
                Exit(ExitUsageError, arguments);
                return;
            }

            var subtarget = arguments.GetBool("bmkServer")
                ? StandaloneBuildSubtarget.Server
                : StandaloneBuildSubtarget.Player;

            var ok = PlatformSwitcher.Switch(target, subtarget);
            Print(ok ? $"Active platform is now {target}." : $"ERROR: could not switch to {target}.");
            Exit(ok ? ExitSuccess : ExitBuildFailed, arguments);
        }

        /// <summary>Prints the full argument reference and exits successfully.</summary>
        public static void Help()
        {
            PrintUsage();
            Exit(ExitSuccess, CommandLineArgs.FromProcess());
        }

        /// <summary>Prints the configured profiles, environments and queues, then exits.</summary>
        public static void List()
        {
            var arguments = CommandLineArgs.FromProcess();
            var settings = BuildManagerSettings.Instance;
            var builder = new StringBuilder();

            builder.AppendLine("Profiles:");
            foreach (var profile in settings.Profiles.Where(profile => profile != null))
                builder.AppendLine(
                    $"  {profile.Id,-24} {profile.Target,-24} {(profile.Enabled ? string.Empty : "(disabled)")}");

            builder.AppendLine("Environments:");
            foreach (var environment in settings.GetSortedEnvironments())
                builder.AppendLine($"  {environment.Id,-24} {environment.DisplayName}");

            builder.AppendLine("Queues:");
            foreach (var queue in settings.Queues.Where(queue => queue != null))
                builder.AppendLine($"  {queue.id,-24} {queue.ActiveEntries.Count()} entries");

            Print(builder.ToString());
            Exit(ExitSuccess, arguments);
        }

        /// <summary>
        /// Project-wide health check: duplicate ids, colliding output paths, clashing environment
        /// defines, several settings assets, broken queue entries. Exits non-zero on any error, so
        /// it works as a pull request gate that costs seconds rather than a build.
        ///
        /// Arguments: <c>-bmkStrict</c> (treat warnings as failures), <c>-bmkNoExit</c>.
        /// </summary>
        public static void Doctor()
        {
            var arguments = CommandLineArgs.FromProcess();
            var strict = arguments.GetBool("bmkStrict");
            var report = BuildManagerIntegrity.Check();

            if (report.Issues.Count == 0)
            {
                Print("Health check passed: no problems found.");
                Exit(ExitSuccess, arguments);
                return;
            }

            foreach (var issue in report.Issues)
                Print($"{(issue.IsError ? "ERROR  " : "WARNING")} {issue}");

            Print($"Health check finished with {report.ErrorCount} error(s) and {report.WarningCount} warning(s).");

            var failed = report.HasErrors || (strict && report.HasWarnings);
            Exit(failed ? ExitBuildFailed : ExitSuccess, arguments);
        }

        /// <summary>
        /// Validates every enabled profile without building. Exits non-zero when any profile has
        /// a blocking problem, which makes it a cheap pull request check. The project-wide health
        /// check runs first, so id clashes and output collisions are reported too.
        /// </summary>
        public static void ValidateAll()
        {
            var arguments = CommandLineArgs.FromProcess();
            var settings = BuildManagerSettings.Instance;
            var failed = 0;

            var integrity = BuildManagerIntegrity.Check(settings);
            foreach (var issue in integrity.Issues)
                Print($"{(issue.IsError ? "ERROR  " : "WARNING")} project: {issue}");

            if (integrity.HasErrors)
                failed++;

            foreach (var profile in settings.GetEnabledProfiles())
            {
                var report = BuildRunner.Validate(profile, profile.DefaultEnvironment ?? settings.ActiveEnvironment);
                var status = report.HasErrors ? "FAIL" : report.HasWarnings ? "WARN" : "OK  ";

                Print($"{status} {profile.Id}");
                foreach (var issue in report.Issues)
                    Print($"       {(issue.IsError ? "error" : "warn ")}: {issue}");

                if (report.HasErrors)
                    failed++;
            }

            Print(failed == 0 ? "All profiles validated." : $"{failed} profile(s) failed validation.");
            Exit(failed == 0 ? ExitSuccess : ExitBuildFailed, arguments);
        }

        /// <summary>
        /// Reads every per-run build override from the command line, so a pipeline can reach the
        /// same settings the Editor exposes without editing — and dirtying — the profile assets.
        /// </summary>
        /// <param name="arguments">Parsed command line.</param>
        /// <param name="hasError">Set when a value failed to parse; the caller should abort.</param>
        internal static BuildOverrides ReadOverrides(CommandLineArgs arguments, out bool hasError)
        {
            var failed = false;

            void Invalid(string message)
            {
                Print("ERROR: " + message);
                failed = true;
            }

            var overrides = new BuildOverrides
            {
                DevelopmentBuild = arguments.GetBoolOrNull("bmkDevelopment"),
                AutoConnectProfiler = arguments.GetBoolOrNull("bmkAutoConnectProfiler"),
                DeepProfiling = arguments.GetBoolOrNull("bmkDeepProfiling"),
                ScriptDebugging = arguments.GetBoolOrNull("bmkScriptDebugging"),
                StrictMode = arguments.GetBoolOrNull("bmkStrictMode"),
                CleanBuildCache = arguments.GetBoolOrNull("bmkCleanBuild"),
                DetailedBuildReport = arguments.GetBoolOrNull("bmkDetailedReport"),
                Compression = arguments.GetEnum<BuildCompression>("bmkCompression", Invalid),
                ScriptingBackend = arguments.GetEnum<ScriptingImplementation>("bmkScriptingBackend", Invalid),
                Il2CppConfiguration = arguments.GetEnum<Il2CppCompilerConfiguration>("bmkIl2CppConfig", Invalid),
                StrippingLevel = arguments.GetEnum<ManagedStrippingLevel>("bmkStripping", Invalid),
                AndroidAppBundle = arguments.GetBoolOrNull("bmkAppBundle"),
                AndroidSplitBinary = arguments.GetBoolOrNull("bmkSplitBinary"),
                AndroidArchitectures = arguments.GetEnum<AndroidArchitecture>("bmkAndroidArchitectures", Invalid),
                AndroidKeystorePath = arguments.GetString("bmkKeystore"),
                AndroidKeyaliasName = arguments.GetString("bmkKeyalias"),
                AppleTeamId = arguments.GetString("bmkAppleTeamId"),
                StandaloneSubtarget = arguments.GetEnum<StandaloneBuildSubtarget>(
                    "bmkSubtarget", Invalid),
                ExecutableName = arguments.GetString("bmkExecutable"),
                Scenes = arguments.GetList("bmkScenes")
            };

            // -bmkServer is a friendlier spelling of -bmkSubtarget Server.
            if (arguments.GetBool("bmkServer"))
                overrides.StandaloneSubtarget = StandaloneBuildSubtarget.Server;

            hasError = failed;
            return overrides;
        }

        private static int ExitCodeFor(BuildRunStatus status)
        {
            switch (status)
            {
                case BuildRunStatus.Succeeded: return ExitSuccess;
                case BuildRunStatus.Cancelled: return ExitCancelled;
                default: return ExitBuildFailed;
            }
        }

        private static void Exit(int code, CommandLineArgs arguments)
        {
            if (arguments.GetBool("bmkNoExit"))
            {
                Print($"Exit code would be {code} (-bmkNoExit was passed).");
                return;
            }

            if (!Application.isBatchMode)
            {
                Print($"Exit code would be {code} (not running in batch mode, so the Editor stays open).");
                return;
            }

            EditorApplication.Exit(code);
        }

        private static void PrintUsage() => Print(@"
Build Manager Kit — command line

  -executeMethod BuildManagerKit.Editor.BuildCLI.Build
      -bmkProfile      <id>     Profile to build                       (required)
      -bmkEnv          <id>     Environment to build with
      -bmkOutput       <path>   Override the output directory
      -bmkExecutable   <name>   Override the player file name (tokens allowed)
      -bmkVersion      <x.y.z>  Override the version string
      -bmkBuildNumber  <n>      Override the build number
      -bmkDefines      <a;b>    Extra scripting defines
      -bmkScenes       <a;b>    Override the scene list with these paths
      -bmkResultFile   <path>   Write the JSON result here
      -bmkDryRun                Validate and log without building
      -bmkRun                   Launch the player once it is built (Build And Run)
      -bmkNoPlatformSwitch      Fail instead of switching the active platform
      -bmkNoExit                Do not call EditorApplication.Exit

    Build option overrides (omit to keep the profile's value):
      -bmkDevelopment          <bool>   Development player
      -bmkAutoConnectProfiler  <bool>   Auto connect the profiler
      -bmkDeepProfiling        <bool>   Deep profiling support
      -bmkScriptDebugging      <bool>   Allow script debugging
      -bmkStrictMode           <bool>   Fail the build on any error
      -bmkCleanBuild           <bool>   Clean the build cache first
      -bmkDetailedReport       <bool>   Detailed build report
      -bmkCompression          <Default|Lz4|Lz4HC>
      -bmkScriptingBackend     <Mono2x|IL2CPP|WinRTDotNET>
      -bmkIl2CppConfig         <Debug|Release|Master>
      -bmkStripping            <Disabled|Minimal|Low|Medium|High>
      -bmkSubtarget            <Player|Server>   (or just -bmkServer)

    Android:
      -bmkAppBundle            <bool>   Build an .aab instead of an .apk
      -bmkSplitBinary          <bool>   Split the application binary
      -bmkAndroidArchitectures <ARM64|ARMv7, or a comma separated set>
      -bmkKeystore             <path>   Keystore to sign with (implies signing)
      -bmkKeyalias             <name>   Key alias
        Passwords come from the environment variables named on the profile,
        ANDROID_KEYSTORE_PASS and ANDROID_KEYALIAS_PASS by default.

    iOS:
      -bmkAppleTeamId          <id>     Apple Developer Team ID

  -executeMethod BuildManagerKit.Editor.BuildCLI.BuildQueue
      -bmkQueue        <id>     Queue to run                           (required)
      -bmkEnv          <id>     Environment for entries without one
      -bmkStopOnFailure <bool>  Override the queue's stop-on-failure setting
      -bmkResultFile   <path>   Write the aggregate JSON result here
      Every build option override above applies to each entry too.

  -executeMethod BuildManagerKit.Editor.BuildCLI.SwitchEnvironment  -bmkEnv <id>
  -executeMethod BuildManagerKit.Editor.BuildCLI.SwitchPlatform     -bmkTarget <BuildTarget> [-bmkServer]
  -executeMethod BuildManagerKit.Editor.BuildCLI.List
  -executeMethod BuildManagerKit.Editor.BuildCLI.ValidateAll
  -executeMethod BuildManagerKit.Editor.BuildCLI.Doctor  [-bmkStrict]
  -executeMethod BuildManagerKit.Editor.BuildCLI.Help

Exit codes: 0 success · 1 build failed · 2 usage error · 3 cancelled");

        private static void Print(string message)
        {
            // Console.WriteLine reaches the CI log directly, Debug.Log reaches the Editor log.
            Console.WriteLine("[BuildManagerKit] " + message);
            Debug.Log("[BuildManagerKit] " + message);
        }
    }
}
