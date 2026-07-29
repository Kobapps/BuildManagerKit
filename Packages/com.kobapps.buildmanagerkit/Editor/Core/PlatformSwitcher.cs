using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// One-click platform switching that remembers each platform's own settings.
    ///
    /// Before leaving a platform the current defines, identifier, backend and stripping level are
    /// stored; when you come back they are restored. Switching to Windows to test something no
    /// longer costs you the Android configuration you spent an afternoon on.
    /// </summary>
    public static class PlatformSwitcher
    {
        private const string k_PendingRestoreKey = "BuildManagerKit.PendingPlatformRestore";
        private static readonly string k_StorePath =
            Path.Combine(ProjectPaths.ProjectRoot, "Library/BuildManagerKit/platform-settings.json");

        [Serializable]
        private sealed class Store
        {
            public List<string> keys = new List<string>();
            public List<string> values = new List<string>();
        }

        /// <summary>Raised after the active platform changed through this API.</summary>
        public static event Action<BuildTarget> PlatformSwitched;

        /// <summary>The target the Editor is currently configured for.</summary>
        public static BuildTarget Active => EditorUserBuildSettings.activeBuildTarget;

        /// <summary>
        /// Switches the active build target, saving the outgoing platform's settings and
        /// restoring anything previously saved for the incoming one.
        /// </summary>
        /// <param name="target">Platform to switch to.</param>
        /// <param name="subtarget">Player or dedicated server, honoured by Standalone targets.</param>
        /// <param name="interactive">Show progress and error dialogs.</param>
        /// <returns>True when the Editor is on <paramref name="target"/> when the call returns.</returns>
        public static bool Switch(
            BuildTarget target,
            StandaloneBuildSubtarget subtarget = StandaloneBuildSubtarget.Player,
            bool interactive = false)
        {
            if (!BuildTargetUtility.IsTargetInstalled(target))
            {
                var message = $"The platform module for {target} is not installed.";
                if (interactive)
                    EditorUtility.DisplayDialog("Build Manager Kit", message, "OK");
                else
                    Debug.LogError("[BuildManagerKit] " + message);

                return false;
            }

            var group = BuildPipeline.GetBuildTargetGroup(target);

            if (EditorUserBuildSettings.activeBuildTarget == target &&
                EditorUserBuildSettings.standaloneBuildSubtarget == subtarget)
                return true;

            SaveCurrent();

            try
            {
                if (interactive)
                    EditorUtility.DisplayProgressBar("Build Manager Kit", $"Switching to {target}…", 0.5f);

                EditorUserBuildSettings.standaloneBuildSubtarget = subtarget;

                // The switch reimports assets and may reload the domain, so the restore is queued
                // in SessionState and also attempted inline for the common no-reload case.
                var incoming = BuildTargetUtility.GetNamedBuildTarget(target, subtarget);
                SessionState.SetString(k_PendingRestoreKey, incoming.TargetName);

                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                {
                    SessionState.EraseString(k_PendingRestoreKey);
                    Debug.LogError($"[BuildManagerKit] Unity refused to switch to {target}.");
                    return false;
                }
            }
            finally
            {
                if (interactive)
                    EditorUtility.ClearProgressBar();
            }

            ApplyPendingRestore();
            PlatformSwitched?.Invoke(target);
            return true;
        }

        /// <summary>Stores the settings of the platform the Editor is currently on.</summary>
        public static void SaveCurrent()
        {
            var namedTarget = BuildTargetUtility.GetNamedBuildTarget(
                EditorUserBuildSettings.activeBuildTarget,
                EditorUserBuildSettings.standaloneBuildSubtarget);

            var snapshot = PlayerSettingsSnapshot.Capture(namedTarget);
            Write(namedTarget.TargetName, snapshot.ToJson());
        }

        /// <summary>True when settings were stored for the given target at some point.</summary>
        public static bool HasStoredSettings(NamedBuildTarget namedTarget) =>
            Read(namedTarget.TargetName) != null;

        /// <summary>Forgets every stored platform snapshot.</summary>
        public static void ClearStoredSettings()
        {
            try
            {
                if (File.Exists(k_StorePath))
                    File.Delete(k_StorePath);
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"[BuildManagerKit] Could not clear stored platform settings: {exception.Message}");
            }
        }

        [InitializeOnLoadMethod]
        private static void OnDomainReload() => EditorApplication.delayCall += ApplyPendingRestore;

        private static void ApplyPendingRestore()
        {
            var pending = SessionState.GetString(k_PendingRestoreKey, string.Empty);
            if (string.IsNullOrEmpty(pending))
                return;

            var expected = BuildTargetUtility.GetNamedBuildTarget(
                EditorUserBuildSettings.activeBuildTarget,
                EditorUserBuildSettings.standaloneBuildSubtarget);

            // Wait until Unity has finished moving to the requested platform.
            if (!string.Equals(expected.TargetName, pending, StringComparison.Ordinal))
                return;

            SessionState.EraseString(k_PendingRestoreKey);

            var snapshot = PlayerSettingsSnapshot.FromJson(Read(pending));
            if (snapshot == null)
                return;

            snapshot.Restore();
            Debug.Log($"[BuildManagerKit] Restored saved settings for {pending}.");
        }

        private static void Write(string key, string value)
        {
            var store = LoadStore();
            var index = store.keys.IndexOf(key);

            if (index >= 0)
            {
                store.values[index] = value;
            }
            else
            {
                store.keys.Add(key);
                store.values.Add(value);
            }

            try
            {
                var directory = Path.GetDirectoryName(k_StorePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(k_StorePath, JsonUtility.ToJson(store));
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"[BuildManagerKit] Could not store platform settings: {exception.Message}");
            }
        }

        private static string Read(string key)
        {
            var store = LoadStore();
            var index = store.keys.IndexOf(key);
            return index >= 0 ? store.values[index] : null;
        }

        private static Store LoadStore()
        {
            try
            {
                if (File.Exists(k_StorePath))
                {
                    var store = JsonUtility.FromJson<Store>(File.ReadAllText(k_StorePath));
                    if (store != null && store.keys != null && store.values != null &&
                        store.keys.Count == store.values.Count)
                        return store;
                }
            }
            catch (Exception)
            {
                // Fall through to a fresh store; a corrupt cache is not worth failing over.
            }

            return new Store();
        }
    }
}
