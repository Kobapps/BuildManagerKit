using UnityEngine;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Samples
{
    /// <summary>
    /// The zero-configuration extension point: static methods that run for every build.
    ///
    /// Use hooks for logic that has nothing to configure and should never be accidentally
    /// disabled. Use a <see cref="BuildStep"/> instead when the behaviour differs per profile or
    /// per environment.
    /// </summary>
    internal static class CodeHooks
    {
        /// <summary>Runs before the configured pre build actions of every build.</summary>
        [BuildHook(BuildStepScope.PreBuild, Order = -100)]
        private static void LogWhatWeAreBuilding(BuildContext context)
        {
            context.Log.Info(
                $"{context.Profile.DisplayName} · {context.Target} · "
                + $"{(context.Environment != null ? context.Environment.Id : "no environment")} · "
                + $"{context.Version}+{context.BuildNumber}");
        }

        /// <summary>Bakes anything that must match the environment exactly.</summary>
        [BuildHook(BuildStepScope.PreBuild, Order = 0)]
        private static void BakeEnvironmentData(BuildContext context)
        {
            if (context.DryRun)
                return;

            var api = context.GetVariable("api_url", "https://localhost:8080");
            context.Log.Info($"Backend for this build: {api}");

            // …regenerate ScriptableObjects, addressable groups, licence files, and so on.
        }

        /// <summary>Runs after every build, whatever the outcome.</summary>
        [BuildHook(BuildStepScope.PostBuild, Order = 100)]
        private static void ReportOutcome(BuildContext context)
        {
            if (context.Status == BuildRunStatus.Succeeded)
                Debug.Log($"[Samples] Shipped {context.OutputPath}");
            else
                Debug.LogWarning($"[Samples] Build did not succeed: {context.FailureMessage}");
        }
    }
}
