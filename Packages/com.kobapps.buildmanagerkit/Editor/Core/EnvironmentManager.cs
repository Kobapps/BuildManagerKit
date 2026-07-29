using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Applies environments to the Editor and to builds.
    ///
    /// Activating an environment performs exactly the same work a build does — defines, product
    /// name, bundle identifier, runtime variables — so pressing Play after a switch reproduces the
    /// shipped configuration. Switching is one click from the Build Manager window, the
    /// <c>Tools &gt; Build Manager Kit &gt; Environment</c> menu or the Scene view overlay.
    /// </summary>
    public static class EnvironmentManager
    {
        /// <summary>Raised after an environment has been applied to the Editor.</summary>
        public static event Action<BuildEnvironment> EnvironmentActivated;

        /// <summary>The environment currently applied to the Editor, or null when none is set.</summary>
        public static BuildEnvironment Active
        {
            get
            {
                var settings = BuildManagerSettings.InstanceOrNull;
                return settings != null ? settings.ActiveEnvironment : null;
            }
        }

        /// <summary>
        /// Applies <paramref name="environment"/> to the Editor: defines, player setting overrides,
        /// the runtime <see cref="BuildInfo"/> asset and the environment's activation actions.
        /// </summary>
        /// <param name="environment">Environment to activate. Null clears the overrides.</param>
        /// <param name="interactive">
        /// True when triggered from the UI, which enables the confirmation prompt of protected
        /// environments and shows a progress bar.
        /// </param>
        /// <returns>True when the environment was applied.</returns>
        public static bool Activate(BuildEnvironment environment, bool interactive = false)
        {
            var settings = BuildManagerSettings.Instance;

            if (environment != null && environment.RequireConfirmation && interactive)
            {
                var proceed = EditorUtility.DisplayDialog(
                    "Switch environment",
                    $"'{environment.DisplayName}' is marked as protected.\n\n" +
                    "Its defines, identifiers and runtime variables will be applied to the Editor.\n\nContinue?",
                    "Switch",
                    "Cancel");

                if (!proceed)
                    return false;
            }

            var log = new BuildLog { MirrorToConsole = false };
            var namedTarget = BuildTargetUtility.GetNamedBuildTarget(
                EditorUserBuildSettings.activeBuildTarget,
                EditorUserBuildSettings.standaloneBuildSubtarget);

            try
            {
                if (interactive)
                    EditorUtility.DisplayProgressBar("Build Manager Kit",
                        $"Applying environment '{(environment != null ? environment.DisplayName : "none")}'…", 0.3f);

                var definesChanged = ApplyToTarget(environment, namedTarget, settings, null, log);
                ApplyPlayerSettingOverrides(environment, namedTarget, settings, log);

                if (settings.WriteBuildInfoAsset)
                    BuildInfoWriter.WriteForEditor(environment);

                if (settings.WriteEnvironmentAssets)
                    EnvironmentAssetsWriter.Write(environment, settings, log);

                settings.ActiveEnvironment = environment;

                RunActivationSteps(environment, settings, namedTarget, log);

                AssetDatabase.SaveAssets();

                var summary = environment != null
                    ? $"Environment '{environment.DisplayName}' active"
                    : "Environment cleared";

                Debug.Log($"[BuildManagerKit] {summary}{(definesChanged ? " (scripts will recompile)" : string.Empty)}.");

                EnvironmentActivated?.Invoke(environment);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BuildManagerKit] Failed to activate environment: {exception}");
                return false;
            }
            finally
            {
                if (interactive)
                    EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Applies the define set of an environment to a single target.
        /// </summary>
        /// <param name="environment">Environment supplying the defines, may be null.</param>
        /// <param name="namedTarget">Target to write the defines to.</param>
        /// <param name="settings">Settings asset, used to know which defines are environment owned.</param>
        /// <param name="extraDefines">Additional defines, usually the profile's own list.</param>
        /// <param name="log">Optional log to report the change to.</param>
        /// <returns>True when the define string actually changed.</returns>
        public static bool ApplyToTarget(
            BuildEnvironment environment,
            NamedBuildTarget namedTarget,
            BuildManagerSettings settings,
            IEnumerable<string> extraDefines,
            IBuildLog log)
        {
            var current = ScriptingDefineUtility.Get(namedTarget);
            var allEnvironments = settings != null
                ? (IEnumerable<BuildEnvironment>)settings.Environments
                : Array.Empty<BuildEnvironment>();

            var composed = ScriptingDefineUtility.Compose(current, environment, allEnvironments, extraDefines);
            var changed = ScriptingDefineUtility.Set(namedTarget, composed);

            if (changed)
                log?.Info($"Defines for {namedTarget.TargetName}: {string.Join(";", composed)}");

            return changed;
        }

        /// <summary>
        /// Applies product name, company name, icon and application identifier — the environment's
        /// own values where it overrides them, and the common configuration held by the base
        /// environment everywhere else.
        /// </summary>
        public static void ApplyPlayerSettingOverrides(
            BuildEnvironment environment,
            NamedBuildTarget namedTarget,
            IBuildLog log) =>
            ApplyPlayerSettingOverrides(environment, namedTarget, BuildManagerSettings.InstanceOrNull, log);

        /// <summary>
        /// Applies the resolved player settings using a specific settings asset, which is where the
        /// base environment holding the common configuration is nominated.
        /// </summary>
        /// <param name="environment">Environment being applied, may be null.</param>
        /// <param name="namedTarget">Target the identifier and icon are written for.</param>
        /// <param name="settings">Settings asset holding the base environment, may be null.</param>
        /// <param name="log">Optional log to report each applied value to.</param>
        public static void ApplyPlayerSettingOverrides(
            BuildEnvironment environment,
            NamedBuildTarget namedTarget,
            BuildManagerSettings settings,
            IBuildLog log)
        {
            if (environment == null)
                return;

            var productName = ConfigResolver.ResolveProductName(settings, environment);
            if (!string.IsNullOrEmpty(productName))
            {
                PlayerSettings.productName = productName;
                log?.Info($"Product name: {productName}");
            }

            var companyName = ConfigResolver.ResolveCompanyName(settings, environment);
            if (!string.IsNullOrEmpty(companyName))
            {
                PlayerSettings.companyName = companyName;
                log?.Info($"Company name: {companyName}");
            }

            var icon = ConfigResolver.ResolveApplicationIcon(settings, environment);
            if (icon != null)
                ApplicationIconService.Apply(namedTarget, icon, log);

            var identifier = ConfigResolver.ResolveApplicationIdentifier(settings, environment);
            if (!string.IsNullOrEmpty(identifier))
            {
                try
                {
                    PlayerSettings.SetApplicationIdentifier(namedTarget, identifier);
                    log?.Info($"Application identifier: {identifier}");
                }
                catch (Exception exception)
                {
                    log?.Warning($"Could not set the application identifier: {exception.Message}");
                }
            }
        }

        /// <summary>
        /// Cycles to the next environment in sort order. Bound to a menu shortcut so switching is
        /// a single keystroke while iterating.
        /// </summary>
        public static void ActivateNext()
        {
            var settings = BuildManagerSettings.Instance;
            var environments = settings.GetSortedEnvironments().ToList();

            if (environments.Count == 0)
            {
                Debug.LogWarning("[BuildManagerKit] No environments configured.");
                return;
            }

            var index = environments.IndexOf(settings.ActiveEnvironment);
            Activate(environments[(index + 1) % environments.Count], true);
        }

        private static void RunActivationSteps(
            BuildEnvironment environment,
            BuildManagerSettings settings,
            NamedBuildTarget namedTarget,
            BuildLog log)
        {
            // Global actions run for every environment, so shared activation work is configured
            // once instead of being duplicated on each environment asset. They run first, then the
            // environment's own list refines whatever they set up — and an environment action
            // sharing a global action's override key replaces it outright.
            var candidates = BuildStepResolver.Tag(settings.GlobalOnActivateSteps, BuildStepScopeLevel.Global)
                .Concat(BuildStepResolver.Tag(
                    environment != null ? environment.OnActivateSteps : Array.Empty<BuildStep>(),
                    BuildStepScopeLevel.Environment));

            var steps = BuildStepResolver.Resolve(candidates).ToList();

            if (steps.Count == 0)
                return;

            var context = new BuildContext(log)
            {
                Settings = settings,
                Environment = environment,
                Target = EditorUserBuildSettings.activeBuildTarget,
                NamedTarget = namedTarget,
                StandaloneSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget,
                Version = PlayerSettings.bundleVersion,
                BuildNumber = PlayerSettings.Android.bundleVersionCode,
                ResolvedVersioning = ConfigResolver.ResolveVersioning(settings, environment, null),
                Git = GitInfo.Read(),
                Phase = BuildPhase.Setup
            };

            context.ApplyEnvironmentVariables(environment);
            context.RefreshTokens();

            // The plumbing above (define diffs and so on) stays out of the console, but anything a
            // user-authored action logs must be visible — otherwise a Log Message action in an
            // activation list appears to do nothing at all.
            var wasMirroring = log.MirrorToConsole;
            log.MirrorToConsole = true;

            foreach (var step in steps)
            {
                if (step == null || !step.ShouldRun(context))
                    continue;

                try
                {
                    log.Scope = step.Title;
                    step.Execute(context);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[BuildManagerKit] Activation action '{step.Title}' failed: {Describe(exception)}");
                }
                finally
                {
                    log.Scope = null;
                }
            }

            log.MirrorToConsole = wasMirroring;
        }

        internal static string Describe(Exception exception)
        {
            var unwrapped = exception is System.Reflection.TargetInvocationException invocation &&
                            invocation.InnerException != null
                ? invocation.InnerException
                : exception;

            return unwrapped is BuildStepException ? unwrapped.Message : unwrapped.ToString();
        }
    }
}
