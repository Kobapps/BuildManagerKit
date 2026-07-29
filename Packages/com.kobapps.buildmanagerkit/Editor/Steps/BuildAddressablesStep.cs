using System;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Builds Addressables content before the player build.
    ///
    /// The action only does something when <c>com.unity.addressables</c> is installed; the
    /// assembly definition declares a version define so the package stays compilable without it.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Content/Build Addressables",
        Tooltip = "Runs an Addressables content build before the player build.",
        Scope = BuildStepScope.PreBuild,
        Order = -50)]
    public sealed class BuildAddressablesStep : BuildStep
    {
        [Tooltip("Clean the existing content build before rebuilding.")]
        [SerializeField] private bool m_CleanFirst;

        /// <inheritdoc />
        public override string Summary => m_CleanFirst ? "Clean and rebuild content" : "Rebuild content";

        /// <inheritdoc />
        public override void Validate(BuildContext context, BuildValidationReport report)
        {
#if !BUILDMANAGERKIT_ADDRESSABLES
            report.AddWarning("The Addressables package is not installed; this action will be skipped.");
#endif
        }

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
#if BUILDMANAGERKIT_ADDRESSABLES
            if (context.DryRun)
            {
                context.Log.Info("[dry run] Would run an Addressables content build.");
                return;
            }

            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new BuildStepException(
                    "Addressables is installed but this project has no Addressable Asset Settings.");

            if (m_CleanFirst)
            {
                context.Log.Info("Cleaning the existing Addressables content…");
                UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.CleanPlayerContent();
            }

            UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.BuildPlayerContent(out var result);

            if (!string.IsNullOrEmpty(result.Error))
                throw new BuildStepException($"The Addressables build failed: {result.Error}");

            context.Log.Success(
                $"Addressables content built in {result.Duration:0.0}s ({result.LocationCount} location(s)).");
#else
            context.Log.Warning("The Addressables package is not installed; skipping the content build.");
#endif
        }
    }
}
