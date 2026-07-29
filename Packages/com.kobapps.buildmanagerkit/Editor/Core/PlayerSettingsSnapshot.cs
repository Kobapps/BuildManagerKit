using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// A restorable copy of every player setting Build Manager Kit is allowed to touch.
    ///
    /// The runner captures one before applying an environment and restores it afterwards, so a
    /// build never leaves the project dirty. The platform quick-switcher keeps one snapshot per
    /// target so switching away and back preserves the platform's own configuration.
    /// </summary>
    [Serializable]
    public sealed class PlayerSettingsSnapshot
    {
        /// <summary>True when this snapshot holds real captured data.</summary>
        public bool valid;

        /// <summary>Build target group the per-target values belong to.</summary>
        public int buildTargetGroup = (int)BuildTargetGroup.Standalone;

        /// <summary>True when the per-target values were captured for the dedicated server target.</summary>
        public bool isServerTarget;

        // Project wide
        public string productName = string.Empty;
        public string companyName = string.Empty;
        public string bundleVersion = string.Empty;

        // Per named target
        public string scriptingDefines = string.Empty;
        public string applicationIdentifier = string.Empty;
        public int scriptingBackend;
        public int il2CppConfiguration;
        public int strippingLevel;

        // Android
        public int androidBundleVersionCode = 1;
        public bool androidBuildAppBundle;
        public bool androidSplitApplicationBinary;
        public int androidArchitectures;
        public bool androidUseCustomKeystore;
        public string androidKeystoreName = string.Empty;
        public string androidKeyaliasName = string.Empty;

        // iOS
        public string iosBuildNumber = string.Empty;
        public string appleDeveloperTeamId = string.Empty;

        /// <summary>
        /// Application icons, stored as asset GUIDs so an environment icon override is undone even
        /// when the snapshot has been round-tripped through disk.
        /// </summary>
        public IconSnapshot icons = new IconSnapshot();

        // Editor build settings
        public int standaloneSubtarget = (int)StandaloneBuildSubtarget.Player;
        public bool developmentBuild;
        public bool allowDebugging;
        public bool connectProfiler;
        public bool deepProfiling;

        /// <summary>The named target the per-target values belong to.</summary>
        public NamedBuildTarget NamedTarget
        {
            get
            {
                var group = (BuildTargetGroup)buildTargetGroup;
                if (group == BuildTargetGroup.Standalone && isServerTarget)
                    return NamedBuildTarget.Server;

                try
                {
                    return NamedBuildTarget.FromBuildTargetGroup(group);
                }
                catch (ArgumentException)
                {
                    return NamedBuildTarget.Standalone;
                }
            }
        }

        /// <summary>Captures the current state of everything the kit may modify.</summary>
        /// <param name="namedTarget">Target whose per-platform settings are captured.</param>
        public static PlayerSettingsSnapshot Capture(NamedBuildTarget namedTarget)
        {
            var snapshot = new PlayerSettingsSnapshot
            {
                valid = true,
                buildTargetGroup = (int)namedTarget.ToBuildTargetGroup(),
                isServerTarget = namedTarget == NamedBuildTarget.Server,
                productName = PlayerSettings.productName,
                companyName = PlayerSettings.companyName,
                bundleVersion = PlayerSettings.bundleVersion,
                androidBundleVersionCode = PlayerSettings.Android.bundleVersionCode,
                androidBuildAppBundle = EditorUserBuildSettings.buildAppBundle,
                androidSplitApplicationBinary = PlayerSettings.Android.splitApplicationBinary,
                androidArchitectures = (int)PlayerSettings.Android.targetArchitectures,
                androidUseCustomKeystore = PlayerSettings.Android.useCustomKeystore,
                androidKeystoreName = PlayerSettings.Android.keystoreName ?? string.Empty,
                androidKeyaliasName = PlayerSettings.Android.keyaliasName ?? string.Empty,
                iosBuildNumber = PlayerSettings.iOS.buildNumber ?? string.Empty,
                appleDeveloperTeamId = PlayerSettings.iOS.appleDeveloperTeamID ?? string.Empty,
                standaloneSubtarget = (int)EditorUserBuildSettings.standaloneBuildSubtarget,
                developmentBuild = EditorUserBuildSettings.development,
                allowDebugging = EditorUserBuildSettings.allowDebugging,
                connectProfiler = EditorUserBuildSettings.connectProfiler,
                deepProfiling = EditorUserBuildSettings.buildWithDeepProfilingSupport
            };

            snapshot.icons = ApplicationIconService.Capture(namedTarget);

            try
            {
                snapshot.scriptingDefines = PlayerSettings.GetScriptingDefineSymbols(namedTarget) ?? string.Empty;
                snapshot.applicationIdentifier = PlayerSettings.GetApplicationIdentifier(namedTarget) ?? string.Empty;
                snapshot.scriptingBackend = (int)PlayerSettings.GetScriptingBackend(namedTarget);
                snapshot.il2CppConfiguration = (int)PlayerSettings.GetIl2CppCompilerConfiguration(namedTarget);
                snapshot.strippingLevel = (int)PlayerSettings.GetManagedStrippingLevel(namedTarget);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[BuildManagerKit] Could not fully capture player settings for {namedTarget.TargetName}: {exception.Message}");
            }

            return snapshot;
        }

        /// <summary>Writes every captured value back into the project.</summary>
        public void Restore()
        {
            if (!valid)
                return;

            var namedTarget = NamedTarget;

            PlayerSettings.productName = productName;
            PlayerSettings.companyName = companyName;
            PlayerSettings.bundleVersion = bundleVersion;

            PlayerSettings.Android.bundleVersionCode = androidBundleVersionCode;
            EditorUserBuildSettings.buildAppBundle = androidBuildAppBundle;
            PlayerSettings.Android.splitApplicationBinary = androidSplitApplicationBinary;
            PlayerSettings.Android.targetArchitectures = (AndroidArchitecture)androidArchitectures;
            PlayerSettings.Android.useCustomKeystore = androidUseCustomKeystore;
            PlayerSettings.Android.keystoreName = androidKeystoreName;
            PlayerSettings.Android.keyaliasName = androidKeyaliasName;

            PlayerSettings.iOS.buildNumber = iosBuildNumber;
            PlayerSettings.iOS.appleDeveloperTeamID = appleDeveloperTeamId;

            ApplicationIconService.Restore(namedTarget, icons);

            EditorUserBuildSettings.standaloneBuildSubtarget = (StandaloneBuildSubtarget)standaloneSubtarget;
            EditorUserBuildSettings.development = developmentBuild;
            EditorUserBuildSettings.allowDebugging = allowDebugging;
            EditorUserBuildSettings.connectProfiler = connectProfiler;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = deepProfiling;

            try
            {
                PlayerSettings.SetScriptingDefineSymbols(namedTarget, scriptingDefines);
                PlayerSettings.SetApplicationIdentifier(namedTarget, applicationIdentifier);
                PlayerSettings.SetScriptingBackend(namedTarget, (ScriptingImplementation)scriptingBackend);
                PlayerSettings.SetIl2CppCompilerConfiguration(namedTarget,
                    (Il2CppCompilerConfiguration)il2CppConfiguration);
                PlayerSettings.SetManagedStrippingLevel(namedTarget, (ManagedStrippingLevel)strippingLevel);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[BuildManagerKit] Could not fully restore player settings for {namedTarget.TargetName}: {exception.Message}");
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>Serialises the snapshot so it can be persisted between sessions.</summary>
        public string ToJson() => JsonUtility.ToJson(this);

        /// <summary>Deserialises a snapshot, returning null for empty or malformed input.</summary>
        public static PlayerSettingsSnapshot FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                var snapshot = JsonUtility.FromJson<PlayerSettingsSnapshot>(json);
                return snapshot != null && snapshot.valid ? snapshot : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
