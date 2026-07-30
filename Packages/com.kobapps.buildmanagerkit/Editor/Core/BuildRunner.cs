using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BuildManagerKit.Editor
{
    /// <summary>Everything needed to start one build.</summary>
    public sealed class BuildRunRequest
    {
        /// <summary>Profile to build. Required.</summary>
        public BuildTargetProfile Profile;

        /// <summary>
        /// Environment to build with. Falls back to the profile default, then to the active
        /// Editor environment.
        /// </summary>
        public BuildEnvironment Environment;

        /// <summary>Overrides the resolved output directory when set.</summary>
        public string OutputDirectoryOverride;

        /// <summary>Overrides the resolved version string when set.</summary>
        public string VersionOverride;

        /// <summary>Overrides the resolved build number when set.</summary>
        public int? BuildNumberOverride;

        /// <summary>Extra scripting defines applied on top of the environment and profile ones.</summary>
        public string[] ExtraDefines = Array.Empty<string>();

        /// <summary>Validate and log everything but do not build or touch the project.</summary>
        public bool DryRun;

        /// <summary>
        /// Launch the player as soon as it is built — Unity's "Build And Run".
        ///
        /// Where it launches is the platform's business: a standalone player starts on this
        /// machine, an Android or iOS build is deployed to the connected device, and a WebGL build
        /// opens in a browser. It is a property of this run rather than of the profile, because
        /// whether you want to watch the build start is a question about right now, not about how
        /// the platform is configured — and CI must never answer it by accident.
        /// </summary>
        public bool RunAfterBuild;

        /// <summary>Started from the UI: show progress bars and confirmation dialogs.</summary>
        public bool Interactive;

        /// <summary>When set, the JSON result is written to this path.</summary>
        public string ResultFilePath;

        /// <summary>Switch the active platform when the profile targets a different one.</summary>
        public bool AllowPlatformSwitch = true;

        /// <summary>
        /// Per-run overrides applied on top of the profile, so a build server can reach every
        /// setting without editing the profile assets.
        /// </summary>
        public BuildOverrides Overrides = new BuildOverrides();
    }

    /// <summary>
    /// Executes a single build: resolve, validate, apply, run the pre actions, call
    /// <c>BuildPipeline.BuildPlayer</c>, run the post actions, restore the project and record the
    /// result.
    ///
    /// This is the single entry point used by the Editor window, the queue runner and the command
    /// line, so a CI build behaves exactly like the button in the UI.
    /// </summary>
    public static class BuildRunner
    {
        /// <summary>
        /// Upper bound on the log text embedded in <see cref="BuildRunResult.log"/>. The complete
        /// log always goes to the log file; this only bounds the copy CI parses.
        /// </summary>
        private const int k_MaxEmbeddedLogCharacters = 256 * 1024;

        /// <summary>True while a build is in progress.</summary>
        public static bool IsRunning { get; private set; }

        /// <summary>The context of the build in progress, or null.</summary>
        public static BuildContext Current { get; private set; }

        /// <summary>Raised when a run starts, before any action executes.</summary>
        public static event Action<BuildContext> RunStarted;

        /// <summary>Raised for every log line of the run in progress.</summary>
        public static event Action<BuildLogEntry> LogAppended;

        /// <summary>Raised when a run finishes, whatever the outcome.</summary>
        public static event Action<BuildRunResult> RunFinished;

        /// <summary>
        /// Where a build of this pairing would be written, resolved exactly as a real build would
        /// resolve it — every token, the version, the build number and the platform's own file
        /// extension.
        ///
        /// Nothing is created and nothing about the project is touched, so this is safe to call
        /// while drawing UI. It is the same resolution the build itself performs, which is the
        /// point: a path shown in the window that a build then ignores is worse than no path.
        /// </summary>
        /// <param name="profile">Profile to resolve. Required.</param>
        /// <param name="environment">
        /// Environment to resolve with. Falls back to the profile default, then to the active
        /// Editor environment — the same fallback <see cref="Run"/> applies.
        /// </param>
        /// <returns>The absolute path of the player file or bundle.</returns>
        /// <exception cref="ArgumentNullException">No profile was given.</exception>
        public static string ResolveOutputPath(BuildTargetProfile profile, BuildEnvironment environment = null)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var settings = BuildManagerSettings.Instance;
            environment ??= profile.DefaultEnvironment ?? settings.ActiveEnvironment;

            return BuildProbeContext(settings, profile, environment).OutputPath;
        }

        /// <summary>
        /// The folder <see cref="ResolveOutputPath"/> writes into. This is what "open the output
        /// folder" means: builds of one profile accumulate here.
        /// </summary>
        /// <inheritdoc cref="ResolveOutputPath" path="/param" />
        public static string ResolveOutputDirectory(BuildTargetProfile profile, BuildEnvironment environment = null)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var settings = BuildManagerSettings.Instance;
            environment ??= profile.DefaultEnvironment ?? settings.ActiveEnvironment;

            return BuildProbeContext(settings, profile, environment).OutputDirectory;
        }

        /// <summary>
        /// Validates a request without building anything. Equivalent to a dry run but cheaper:
        /// no defines are applied and no actions execute.
        /// </summary>
        public static BuildValidationReport Validate(BuildTargetProfile profile, BuildEnvironment environment)
        {
            var report = new BuildValidationReport();
            var settings = BuildManagerSettings.Instance;

            if (profile == null)
            {
                report.AddError("No build profile selected.");
                return report;
            }

            environment ??= profile.DefaultEnvironment ?? settings.ActiveEnvironment;

            if (environment == null && settings.Environments.Count > 0)
                report.AddWarning("No environment selected; the build will use the project settings as they are.");

            if (environment != null && !profile.SupportsEnvironment(environment))
                report.AddError($"Profile '{profile.DisplayName}' does not allow environment '{environment.Id}'.");

            if (!BuildTargetUtility.IsTargetInstalled(profile.Target))
                report.AddError($"The platform module for {profile.Target} is not installed in this Editor.");

            var scenes = profile.ResolveScenePaths();
            if (scenes.Length == 0)
            {
                if (settings.FailOnEmptySceneList)
                    report.AddError("The resolved scene list is empty.");
                else
                    report.AddWarning("The resolved scene list is empty.");
            }

            foreach (var scene in scenes.Where(scene => !File.Exists(ProjectPaths.MakeAbsolute(scene))))
                report.AddError($"Scene '{scene}' does not exist.");

            if (string.IsNullOrWhiteSpace(profile.OutputDirectoryTemplate))
                report.AddError("The output directory template is empty.");

            ValidateOutputPath(profile, environment, report);

            if (profile.Target == BuildTarget.Android && profile.Android.useCustomKeystore)
            {
                var keystore = ProjectPaths.MakeAbsolute(profile.Android.keystorePath);
                if (!File.Exists(keystore))
                    report.AddError($"Keystore '{keystore}' does not exist.");

                if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(
                        profile.Android.keystorePasswordEnvVar)))
                    report.AddWarning(
                        $"Environment variable '{profile.Android.keystorePasswordEnvVar}' is not set; signing will fail.");
            }

            var context = BuildProbeContext(settings, profile, environment);

            foreach (var step in EnumerateSteps(settings, profile, environment, preBuild: true)
                         .Concat(EnumerateSteps(settings, profile, environment, preBuild: false)))
            {
                report.CurrentSource = step.Title;
                try
                {
                    step.Validate(context, report);
                }
                catch (Exception exception)
                {
                    report.AddError($"Validation threw: {exception.Message}");
                }
            }

            report.CurrentSource = string.Empty;
            return report;
        }

        /// <summary>
        /// Checks where a profile would actually write. Getting this wrong is the most damaging
        /// mistake available: a template that resolves into <c>Assets</c> makes Unity import the
        /// whole player, and a clean step pointed at the project root would delete it.
        /// </summary>
        private static void ValidateOutputPath(BuildTargetProfile profile, BuildEnvironment environment,
            BuildValidationReport report)
        {
            string outputPath;

            try
            {
                var probe = BuildProbeContext(BuildManagerSettings.Instance, profile, environment);
                outputPath = probe.OutputPath;
            }
            catch (Exception exception)
            {
                report.AddError($"The output path could not be resolved: {exception.Message}");
                return;
            }

            if (ProjectPaths.IsProtectedOutputPath(outputPath, out var reason))
            {
                report.AddError(
                    $"Refusing to build to '{outputPath}' because {reason}. "
                    + "Point the output template somewhere outside the Unity project folders.");
                return;
            }

            if (ProjectPaths.IsDiscouragedOutputPath(outputPath, out var discouraged))
                report.AddWarning($"Building to '{outputPath}' is risky: {discouraged}.");

            if (outputPath.Length > ProjectPaths.MaxPathLength)
                report.AddError(
                    $"The resolved output path is {outputPath.Length} characters, over the {ProjectPaths.MaxPathLength} "
                    + "character limit. Windows builds will fail; shorten the output template.");
            else if (outputPath.Length > ProjectPaths.MaxRecommendedPathLength)
                report.AddWarning(
                    $"The resolved output path is {outputPath.Length} characters. Unity appends deeper paths beneath "
                    + "it, so Windows builds may hit the 260 character limit.");

            // A template with no discriminator makes several profiles or environments overwrite
            // each other, which only shows up once a team has more than one of either.
            var template = profile.OutputDirectoryTemplate ?? string.Empty;
            var settings = BuildManagerSettings.Instance;

            var hasTargetToken = template.IndexOf("{target", StringComparison.OrdinalIgnoreCase) >= 0
                                 || template.IndexOf("{profile", StringComparison.OrdinalIgnoreCase) >= 0
                                 || template.IndexOf("{platform", StringComparison.OrdinalIgnoreCase) >= 0;

            if (settings.Profiles.Count > 1 && !hasTargetToken)
                report.AddWarning(
                    "The output template has no {target}, {platform} or {profile} token, so profiles will "
                    + "overwrite each other's output.");

            if (settings.Environments.Count > 1 &&
                template.IndexOf("{env", StringComparison.OrdinalIgnoreCase) < 0)
                report.AddWarning(
                    "The output template has no {env} token, so environments will overwrite each other's output.");
        }

        /// <summary>Builds one profile and returns the machine readable result.</summary>
        /// <param name="request">What to build and how.</param>
        public static BuildRunResult Run(BuildRunRequest request)
        {
            if (request == null || request.Profile == null)
                return Failed("No build profile supplied.");

            if (IsRunning)
                return Failed("Another build is already running.");

            var settings = BuildManagerSettings.Instance;
            var stopwatch = Stopwatch.StartNew();
            var log = new BuildLog();
            var context = new BuildContext(log)
            {
                Settings = settings,
                Profile = request.Profile,
                DryRun = request.DryRun
            };

            log.EntryAdded += OnLogEntry;
            IsRunning = true;
            Current = context;

            PlayerSettingsSnapshot snapshot = null;
            var restoreSettings = settings.RestoreSettingsAfterBuild && !request.DryRun;

            try
            {
                if (!Prepare(context, request, settings))
                    return Finish(context, stopwatch, request, settings, null, restoreSettings: false);

                RunStarted?.Invoke(context);

                var validation = Validate(request.Profile, context.Environment);
                validation.WriteTo(log);
                if (validation.HasErrors)
                {
                    context.Fail($"Validation failed with {validation.ErrorCount} error(s).");
                    return Finish(context, stopwatch, request, settings, null, restoreSettings: false);
                }

                if (request.DryRun)
                    log.Info("Dry run: no files will be written and no project settings will change.");

                if (!request.DryRun)
                {
                    snapshot = PlayerSettingsSnapshot.Capture(context.NamedTarget);
                    ApplyProjectSettings(context, request, settings);
                }

                context.RefreshTokens();
                log.Info($"Output: {context.OutputPath}");

                if (!RunSteps(context, EnumerateSteps(settings, context.Profile, context.Environment, true),
                        BuildPhase.PreBuild, request.Interactive))
                    return Finish(context, stopwatch, request, settings, snapshot, restoreSettings);

                if (!RunHooks(context, BuildStepScope.PreBuild))
                    return Finish(context, stopwatch, request, settings, snapshot, restoreSettings);

                BuildPlayer(context, request);

                RunSteps(context, EnumerateSteps(settings, context.Profile, context.Environment, false),
                    BuildPhase.PostBuild, request.Interactive);
                RunHooks(context, BuildStepScope.PostBuild);

                return Finish(context, stopwatch, request, settings, snapshot, restoreSettings);
            }
            catch (Exception exception)
            {
                context.Fail($"Unhandled error: {exception}");
                return Finish(context, stopwatch, request, settings, snapshot, restoreSettings);
            }
            finally
            {
                log.EntryAdded -= OnLogEntry;
                IsRunning = false;
                Current = null;

                if (request.Interactive)
                    EditorUtility.ClearProgressBar();
            }
        }

        private static void OnLogEntry(BuildLogEntry entry) => LogAppended?.Invoke(entry);

        private static bool Prepare(BuildContext context, BuildRunRequest request, BuildManagerSettings settings)
        {
            var profile = request.Profile;
            var log = context.Log;

            var overrides = request.Overrides ?? new BuildOverrides();

            context.Environment = request.Environment ?? profile.DefaultEnvironment ?? settings.ActiveEnvironment;
            context.Target = profile.Target;
            context.StandaloneSubtarget = overrides.StandaloneSubtarget ?? profile.StandaloneSubtarget;
            context.NamedTarget = BuildTargetUtility.GetNamedBuildTarget(context.Target, context.StandaloneSubtarget);

            if (overrides.HasAny)
                log.Info("Overrides: " + overrides.Describe());
            context.Git = GitInfo.Read(forceRefresh: true);
            context.Phase = BuildPhase.Setup;

            log.Info($"Profile '{profile.DisplayName}' → {profile.Target}"
                     + (context.Environment != null ? $" [{context.Environment.Id}]" : string.Empty));

            if (context.Environment != null && !profile.SupportsEnvironment(context.Environment))
            {
                context.Fail(
                    $"Profile '{profile.DisplayName}' does not allow environment '{context.Environment.Id}'.");
                return false;
            }

            context.ApplyEnvironmentVariables(context.Environment);

            context.ResolvedVersioning = ConfigResolver.ResolveVersioning(settings, context.Environment, profile);

            context.Version = string.IsNullOrWhiteSpace(request.VersionOverride)
                ? VersionService.Resolve(context.Versioning, context.Git, log)
                : request.VersionOverride.Trim();

            context.BuildNumberWasSupplied = request.BuildNumberOverride.HasValue;
            context.BuildNumber = request.BuildNumberOverride
                                  ?? VersionService.ResolveBuildNumber(context.Versioning, context.Git);

            context.DevelopmentBuild = ResolveDevelopmentBuild(profile, context.Environment, overrides, settings);

            context.Scenes = overrides.Scenes != null && overrides.Scenes.Length > 0
                ? overrides.Scenes
                : profile.ResolveScenePaths();

            context.RefreshTokens();

            if (!EnsureActivePlatform(context, request))
                return false;

            ResolveOutput(context, request);

            if (context.Git != null && context.Git.IsRepository)
                log.Info($"Git: {context.Git.Branch}@{context.Git.ShortCommit}"
                         + (context.Git.IsDirty ? " (working copy has uncommitted changes)" : string.Empty));

            return true;
        }

        private static bool EnsureActivePlatform(BuildContext context, BuildRunRequest request)
        {
            if (EditorUserBuildSettings.activeBuildTarget == context.Target &&
                EditorUserBuildSettings.standaloneBuildSubtarget == context.StandaloneSubtarget)
                return true;

            if (request.DryRun)
            {
                context.Log.Info($"[dry run] Would switch the active platform to {context.Target}.");
                return true;
            }

            if (!request.AllowPlatformSwitch)
            {
                context.Fail(
                    $"The active platform is {EditorUserBuildSettings.activeBuildTarget} but the profile targets "
                    + $"{context.Target}, and switching is disabled for this run.");
                return false;
            }

            context.Log.Info($"Switching the active platform to {context.Target}…");

            if (!PlatformSwitcher.Switch(context.Target, context.StandaloneSubtarget, request.Interactive))
            {
                context.Fail($"Could not switch the active platform to {context.Target}.");
                return false;
            }

            return true;
        }

        private static bool ResolveDevelopmentBuild(BuildTargetProfile profile, BuildEnvironment environment,
            BuildOverrides overrides = null, BuildManagerSettings settings = null)
        {
            // An explicit command line flag is the most specific instruction available and beats
            // both the environment's force setting and the profile's own value.
            if (overrides?.DevelopmentBuild != null)
                return overrides.DevelopmentBuild.Value;

            if (environment == null)
                return profile.DevelopmentBuild;

            // Inherit: the environment says nothing, so the base environment gets a say before the
            // profile's own flag decides.
            switch (ConfigResolver.ResolveForceDevelopmentBuild(settings ?? BuildManagerSettings.InstanceOrNull,
                        environment))
            {
                case OptionalBool.Enabled: return true;
                case OptionalBool.Disabled: return false;
                default: return profile.DevelopmentBuild;
            }
        }

        private static void ResolveOutput(BuildContext context, BuildRunRequest request)
        {
            var profile = context.Profile;

            var directoryTemplate = string.IsNullOrWhiteSpace(request.OutputDirectoryOverride)
                ? profile.OutputDirectoryTemplate
                : request.OutputDirectoryOverride;

            var directory = ProjectPaths.MakeAbsolute(context.Resolve(directoryTemplate));

            var nameTemplate = !string.IsNullOrWhiteSpace(request.Overrides?.ExecutableName)
                ? request.Overrides.ExecutableName
                : string.IsNullOrWhiteSpace(profile.ExecutableNameTemplate)
                    ? "{productName}"
                    : profile.ExecutableNameTemplate;

            var baseName = context.Resolve(nameTemplate);

            context.OutputDirectory = directory;
            context.ExecutableName = BuildTargetUtility.GetPlayerFileName(
                profile.Target, baseName, profile.Android.buildAppBundle);
            context.OutputPath = ProjectPaths.Normalize(Path.Combine(directory, context.ExecutableName));
            context.RefreshTokens();
        }

        private static void ApplyProjectSettings(BuildContext context, BuildRunRequest request,
            BuildManagerSettings settings)
        {
            var profile = context.Profile;
            var log = context.Log;

            var extraDefines = profile.ExtraScriptingDefines.Concat(request.ExtraDefines ?? Array.Empty<string>());
            EnvironmentManager.ApplyToTarget(context.Environment, context.NamedTarget, settings, extraDefines, log);
            EnvironmentManager.ApplyPlayerSettingOverrides(context.Environment, context.NamedTarget, log);

            context.ScriptingDefines = ScriptingDefineUtility.Get(context.NamedTarget);

            VersionService.Apply(context);
            VersionService.ApplyPlayerOverrides(profile, context.NamedTarget, log);
            ApplyPlayerOverrides(context, request.Overrides, log);

            ApplyAndroidSettings(context, request.Overrides);
            ApplyIosSettings(context, request.Overrides);

            if (settings.WriteBuildInfoAsset)
            {
                BuildInfoWriter.Write(context);
                log.Info("Generated BuildInfo asset for runtime access.");
            }

            // Direct references, so only this environment's config assets end up in the player.
            if (settings.WriteEnvironmentAssets)
                EnvironmentAssetsWriter.Write(context.Environment, settings, log);

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Applies the command line's player setting overrides. These run after the profile's own
        /// so an explicit flag always wins.
        /// </summary>
        private static void ApplyPlayerOverrides(BuildContext context, BuildOverrides overrides, IBuildLog log)
        {
            if (overrides == null)
                return;

            if (overrides.ScriptingBackend.HasValue)
            {
                PlayerSettings.SetScriptingBackend(context.NamedTarget, overrides.ScriptingBackend.Value);
                log.Info($"Scripting backend override: {overrides.ScriptingBackend.Value}.");
            }

            if (overrides.Il2CppConfiguration.HasValue)
            {
                PlayerSettings.SetIl2CppCompilerConfiguration(context.NamedTarget,
                    overrides.Il2CppConfiguration.Value);
                log.Info($"IL2CPP configuration override: {overrides.Il2CppConfiguration.Value}.");
            }

            if (overrides.StrippingLevel.HasValue)
            {
                PlayerSettings.SetManagedStrippingLevel(context.NamedTarget, overrides.StrippingLevel.Value);
                log.Info($"Managed stripping override: {overrides.StrippingLevel.Value}.");
            }
        }

        private static void ApplyAndroidSettings(BuildContext context, BuildOverrides overrides)
        {
            if (context.Target != BuildTarget.Android)
                return;

            var android = context.Profile.Android;
            var log = context.Log;
            overrides ??= new BuildOverrides();

            EditorUserBuildSettings.buildAppBundle = overrides.AndroidAppBundle ?? android.buildAppBundle;
            PlayerSettings.Android.splitApplicationBinary =
                overrides.AndroidSplitBinary ?? android.splitApplicationBinary;

            if (overrides.AndroidArchitectures.HasValue)
                PlayerSettings.Android.targetArchitectures = overrides.AndroidArchitectures.Value;
            else if (android.overrideArchitectures)
                PlayerSettings.Android.targetArchitectures = android.architectures;

            var keystorePath = !string.IsNullOrEmpty(overrides.AndroidKeystorePath)
                ? overrides.AndroidKeystorePath
                : android.keystorePath;

            var keyalias = !string.IsNullOrEmpty(overrides.AndroidKeyaliasName)
                ? overrides.AndroidKeyaliasName
                : android.keyaliasName;

            // A keystore supplied on the command line implies signing, even when the profile
            // itself is configured for unsigned local builds.
            var useKeystore = android.useCustomKeystore || !string.IsNullOrEmpty(overrides.AndroidKeystorePath);

            if (!useKeystore)
            {
                PlayerSettings.Android.useCustomKeystore = false;
                return;
            }

            var keystorePassword = System.Environment.GetEnvironmentVariable(android.keystorePasswordEnvVar);
            var aliasPassword = System.Environment.GetEnvironmentVariable(android.keyaliasPasswordEnvVar);

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ProjectPaths.MakeAbsolute(keystorePath);
            PlayerSettings.Android.keyaliasName = keyalias;
            PlayerSettings.Android.keystorePass = keystorePassword ?? string.Empty;
            PlayerSettings.Android.keyaliasPass = aliasPassword ?? string.Empty;

            if (string.IsNullOrEmpty(keystorePassword) || string.IsNullOrEmpty(aliasPassword))
                log.Warning("Keystore passwords are missing from the environment; the build will not be signed.");
            else
                log.Info($"Signing with keystore '{Path.GetFileName(keystorePath)}'.");
        }

        private static void ApplyIosSettings(BuildContext context, BuildOverrides overrides)
        {
            if (context.Target != BuildTarget.iOS)
                return;

            var ios = context.Profile.Ios;

            var teamId = !string.IsNullOrWhiteSpace(overrides?.AppleTeamId)
                ? overrides.AppleTeamId
                : ios.overrideTeamId ? ios.appleDeveloperTeamId : null;

            if (string.IsNullOrWhiteSpace(teamId))
                return;

            PlayerSettings.iOS.appleDeveloperTeamID = teamId.Trim();
            context.Log.Info($"Apple team id: {teamId.Trim()}");
        }

        private static IEnumerable<BuildStep> EnumerateSteps(
            BuildManagerSettings settings,
            BuildTargetProfile profile,
            BuildEnvironment environment,
            bool preBuild)
        {
            var global = BuildStepResolver.Tag(
                preBuild ? settings.GlobalPreBuildSteps : settings.GlobalPostBuildSteps,
                BuildStepScopeLevel.Global);

            var fromEnvironment = BuildStepResolver.Tag(
                environment == null
                    ? Array.Empty<BuildStep>()
                    : preBuild
                        ? environment.PreBuildSteps
                        : environment.PostBuildSteps,
                BuildStepScopeLevel.Environment);

            var fromProfile = BuildStepResolver.Tag(
                preBuild ? profile.PreBuildSteps : profile.PostBuildSteps,
                BuildStepScopeLevel.Profile);

            // Pre build widens from global to specific; post build unwinds in the opposite order.
            var ordered = preBuild
                ? global.Concat(fromEnvironment).Concat(fromProfile)
                : fromProfile.Concat(fromEnvironment).Concat(global);

            // Actions sharing an override key collapse to the most specific one.
            return BuildStepResolver.Resolve(ordered);
        }

        private static bool RunSteps(BuildContext context, IEnumerable<BuildStep> steps, BuildPhase phase,
            bool interactive)
        {
            context.Phase = phase;
            var list = steps.ToList();
            var label = phase == BuildPhase.PreBuild ? "Pre build" : "Post build";

            for (var i = 0; i < list.Count; i++)
            {
                var step = list[i];

                if (!step.ShouldRun(context))
                    continue;

                if (interactive)
                {
                    EditorUtility.DisplayProgressBar(
                        $"Build Manager Kit — {label}",
                        step.Title,
                        list.Count == 0 ? 0f : (float)i / list.Count);
                }

                context.Log.Scope = step.Title;

                try
                {
                    step.Execute(context);
                }
                catch (Exception exception)
                {
                    var message = $"Action '{step.Title}' failed: {EnvironmentManager.Describe(exception)}";

                    if (step.OnError == StepFailurePolicy.WarnAndContinue)
                    {
                        context.Log.Warning(message);
                    }
                    else
                    {
                        context.Fail(message);
                        context.Log.Scope = null;
                        return false;
                    }
                }
                finally
                {
                    context.Log.Scope = null;
                }

                if (context.HasFailed || context.IsCancelled)
                    return false;
            }

            return true;
        }

        private static bool RunHooks(BuildContext context, BuildStepScope scope)
        {
            foreach (var hook in BuildStepRegistry.GetHooks(scope))
            {
                context.Log.Scope = hook.DisplayName;

                try
                {
                    hook.Invoke(context);
                }
                catch (Exception exception)
                {
                    context.Fail($"Build hook '{hook.DisplayName}' failed: {EnvironmentManager.Describe(exception)}");
                    context.Log.Scope = null;
                    return false;
                }
                finally
                {
                    context.Log.Scope = null;
                }

                if (context.HasFailed || context.IsCancelled)
                    return false;
            }

            return true;
        }

        private static void BuildPlayer(BuildContext context, BuildRunRequest request)
        {
            context.Phase = BuildPhase.Building;

            if (context.DryRun)
            {
                context.Log.Info($"[dry run] Would build {context.Scenes.Length} scene(s) to {context.OutputPath}.");
                context.Status = BuildRunStatus.Succeeded;
                return;
            }

            context.EnsureDirectory(context.OutputDirectory);

            var buildOptions = (request.Overrides ?? new BuildOverrides())
                .Apply(context.Profile.ResolveBuildOptions(context.DevelopmentBuild), context.DevelopmentBuild);

            // Added last, and only from the request: it is the one option that is a property of
            // this press rather than of the profile, and a profile that could carry it would
            // eventually launch a player on a build server.
            if (request.RunAfterBuild)
            {
                // Appending to an existing Xcode project produces a project rather than a runnable
                // player, so Unity refuses the pair. Dropping the launch and saying so beats
                // failing a build that was otherwise fine — the artifact is still what was wanted.
                if ((buildOptions & BuildOptions.AcceptExternalModificationsToPlayer) != 0)
                {
                    context.Log.Warning(
                        "Build and Run is not available while 'append project' is on: the build produces an "
                        + "Xcode project to open, not a player to launch. Building without launching.");
                }
                else
                {
                    buildOptions |= BuildOptions.AutoRunPlayer;
                }
            }

            var options = new BuildPlayerOptions
            {
                scenes = context.Scenes,
                locationPathName = context.OutputPath,
                target = context.Target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(context.Target),
                subtarget = (int)context.StandaloneSubtarget,
                options = buildOptions
            };

            if (request.Interactive)
                EditorUtility.DisplayProgressBar("Build Manager Kit", "Building player…", 0.5f);

            context.Log.Info($"Building {context.Scenes.Length} scene(s)…");

            // Read back off the options rather than off the request, so the log cannot promise a
            // launch the guard above has already withdrawn.
            if ((buildOptions & BuildOptions.AutoRunPlayer) != 0)
                context.Log.Info("The player will be launched as soon as it is built.");

            var report = BuildPipeline.BuildPlayer(options);
            context.Report = report;

            var summary = report.summary;
            context.Log.Info(
                $"Player build {summary.result} in {BuildTargetUtility.FormatDuration(summary.totalTime)} "
                + $"({BuildTargetUtility.FormatSize((long)summary.totalSize)}, "
                + $"{summary.totalErrors} error(s), {summary.totalWarnings} warning(s)).");

            switch (summary.result)
            {
                case UnityEditor.Build.Reporting.BuildResult.Succeeded:
                    context.Status = BuildRunStatus.Succeeded;
                    context.AddArtifact(context.OutputPath);
                    break;

                case UnityEditor.Build.Reporting.BuildResult.Cancelled:
                    context.Cancel("The player build was cancelled.");
                    break;

                default:
                    context.Fail(FirstError(report) ?? $"The player build failed ({summary.result}).");
                    break;
            }
        }

        private static string FirstError(BuildReport report)
        {
            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                        return message.content;
                }
            }

            return null;
        }

        private static BuildRunResult Finish(
            BuildContext context,
            Stopwatch stopwatch,
            BuildRunRequest request,
            BuildManagerSettings settings,
            PlayerSettingsSnapshot snapshot,
            bool restoreSettings)
        {
            context.Phase = BuildPhase.Finished;
            stopwatch.Stop();

            if (context.Status == BuildRunStatus.Unknown)
                context.Status = BuildRunStatus.Failed;

            if (context.Status == BuildRunStatus.Succeeded && !context.DryRun)
                VersionService.CommitBuildNumber(context);

            if (restoreSettings && snapshot != null)
            {
                snapshot.Restore();
                context.Log.Info("Restored the project settings that were active before the build.");
            }

            // Register the manifest before the result snapshots the artifact list, so the list the
            // caller receives and the list inside the manifest agree.
            var writeManifest = context.Status == BuildRunStatus.Succeeded
                                && !context.DryRun
                                && settings.WriteBuildManifest;

            var manifestPath = writeManifest
                ? ProjectPaths.Normalize(Path.Combine(context.OutputDirectory, "build_manifest.json"))
                : null;

            if (writeManifest)
                context.AddArtifact(manifestPath);

            var result = context.ToResult(stopwatch.Elapsed);

            if (writeManifest)
                WriteManifest(context, result, manifestPath);

            var logFile = string.Empty;
            if (settings.WriteLogFiles && !context.DryRun && context.Log is BuildLog buildLog)
            {
                logFile = BuildLogFilePath(settings, context);

                // The file gets everything; the JSON result gets a bounded tail so a chatty build
                // cannot produce a result file that no CI system can parse.
                buildLog.SaveTo(logFile);
                result.logFile = logFile;
                result.log = buildLog.ToPlainText(k_MaxEmbeddedLogCharacters);
            }
            else if (context.Log is BuildLog inMemory)
            {
                result.log = inMemory.ToPlainText(k_MaxEmbeddedLogCharacters);
            }

            if (!context.DryRun)
            {
                // The history file would grow without bound if it embedded every full log; the
                // entry points at the log file on disk instead. Blank the log around the clone so
                // the big string is never serialised at all.
                var fullLog = result.log;
                result.log = string.Empty;
                var forHistory = JsonUtility.FromJson<BuildRunResult>(JsonUtility.ToJson(result));
                result.log = fullLog;

                BuildHistory.Add(forHistory, logFile, settings.HistoryLimit);
            }

            if (!string.IsNullOrEmpty(request.ResultFilePath))
                WriteResultFile(request.ResultFilePath, result.ToJson(), context.Log);

            var summaryLine = result.ToSummaryLine();
            if (result.Succeeded)
                context.Log.Success(summaryLine);
            else
                context.Log.Write(BuildLogLevel.Error, summaryLine);

            // Not when the player was launched: the build is already in front of the user, and a
            // Finder window opening over it is noise.
            if (request.Interactive && result.Succeeded && settings.RevealOutputOnSuccess && !context.DryRun
                && !request.RunAfterBuild)
                EditorUtility.RevealInFinder(context.OutputPath);

            RunFinished?.Invoke(result);
            return result;
        }

        private static string BuildLogFilePath(BuildManagerSettings settings, BuildContext context)
        {
            var folder = ProjectPaths.MakeAbsolute(settings.LogFolder);
            var stamp = context.StartTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var profileId = BuildTokens.Sanitize(context.Profile != null ? context.Profile.Id : "build");
            var environmentId = BuildTokens.Sanitize(context.Environment != null ? context.Environment.Id : "none");

            return ProjectPaths.Normalize(Path.Combine(folder, $"{stamp}_{profileId}_{environmentId}.log"));
        }

        private static void WriteManifest(BuildContext context, BuildRunResult result, string path)
        {
            try
            {
                File.WriteAllText(path, result.ToJson());
                context.Log.Info("Wrote build_manifest.json.");
            }
            catch (Exception exception)
            {
                context.Log.Warning($"Could not write the build manifest: {exception.Message}");
            }
        }

        private static void WriteResultFile(string path, string json, IBuildLog log)
        {
            try
            {
                var absolute = ProjectPaths.MakeAbsolute(path);
                var directory = Path.GetDirectoryName(absolute);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(absolute, json);
                log.Info($"Wrote the JSON result to {absolute}.");
            }
            catch (Exception exception)
            {
                log.Warning($"Could not write the result file: {exception.Message}");
            }
        }

        private static BuildContext BuildProbeContext(
            BuildManagerSettings settings,
            BuildTargetProfile profile,
            BuildEnvironment environment)
        {
            var context = new BuildContext(new BuildLog { MirrorToConsole = false })
            {
                Settings = settings,
                Profile = profile,
                Environment = environment,
                Target = profile.Target,
                NamedTarget = profile.NamedTarget,
                StandaloneSubtarget = profile.StandaloneSubtarget,
                Git = GitInfo.Read(),
                DryRun = true,
                Phase = BuildPhase.Setup
            };

            context.ApplyEnvironmentVariables(environment);
            context.ResolvedVersioning = ConfigResolver.ResolveVersioning(settings, environment, profile);
            context.Version = VersionService.Resolve(context.Versioning, context.Git, null);
            context.BuildNumber = VersionService.ResolveBuildNumber(context.Versioning, context.Git);
            context.DevelopmentBuild = ResolveDevelopmentBuild(profile, environment, null, settings);
            context.Scenes = profile.ResolveScenePaths();
            context.RefreshTokens();

            var directory = ProjectPaths.MakeAbsolute(context.Resolve(profile.OutputDirectoryTemplate));
            context.OutputDirectory = directory;
            context.ExecutableName = BuildTargetUtility.GetPlayerFileName(
                profile.Target,
                context.Resolve(profile.ExecutableNameTemplate),
                profile.Android.buildAppBundle);
            context.OutputPath = ProjectPaths.Normalize(Path.Combine(directory, context.ExecutableName));
            context.RefreshTokens();

            return context;
        }

        private static BuildRunResult Failed(string message)
        {
            Debug.LogError("[BuildManagerKit] " + message);

            return new BuildRunResult
            {
                status = BuildRunStatus.Failed,
                statusText = BuildRunStatus.Failed.ToString(),
                message = message
            };
        }
    }
}
