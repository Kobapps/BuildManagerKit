using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Creates and updates <c>Assets/Resources/BuildManagerKit/BuildInfo.asset</c>, the bridge
    /// that lets runtime code read the environment, version and git state it was built with.
    /// </summary>
    public static class BuildInfoWriter
    {
        /// <summary>Loads the generated asset, or null when it has not been created yet.</summary>
        public static BuildInfo Load() => AssetDatabase.LoadAssetAtPath<BuildInfo>(ProjectPaths.BuildInfoAssetPath);

        /// <summary>Loads the generated asset, creating it when missing.</summary>
        public static BuildInfo LoadOrCreate()
        {
            var asset = Load();
            if (asset != null)
                return asset;

            ProjectPaths.EnsureAssetFolder(ProjectPaths.GeneratedResourcesFolder);

            asset = ScriptableObject.CreateInstance<BuildInfo>();
            AssetDatabase.CreateAsset(asset, ProjectPaths.BuildInfoAssetPath);
            AssetDatabase.SaveAssets();

            return asset;
        }

        /// <summary>
        /// Writes the values of a build run into the asset. Called by the runner right before the
        /// player build so the data ends up inside the player.
        /// </summary>
        public static void Write(BuildContext context)
        {
            if (context == null || context.DryRun)
                return;

            Write(
                context.Environment,
                context.Profile != null ? context.Profile.Id : string.Empty,
                context.Version,
                context.BuildNumber,
                context.Target.ToString(),
                context.DevelopmentBuild,
                context.Git,
                context.Variables);
        }

        /// <summary>
        /// Writes the values of an Editor environment switch into the asset, so play mode sees
        /// the same variables a real build would.
        /// </summary>
        public static void WriteForEditor(BuildEnvironment environment)
        {
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (environment != null)
            {
                foreach (var variable in environment.Variables)
                {
                    if (!string.IsNullOrEmpty(variable.key))
                        variables[variable.key] = variable.value ?? string.Empty;
                }
            }

            Write(
                environment,
                string.Empty,
                PlayerSettings.bundleVersion,
                PlayerSettings.Android.bundleVersionCode,
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                EditorUserBuildSettings.development,
                GitInfo.Read(),
                variables);
        }

        private static void Write(
            BuildEnvironment environment,
            string profileId,
            string version,
            int buildNumber,
            string target,
            bool developmentBuild,
            GitInfo git,
            IReadOnlyDictionary<string, string> variables)
        {
            var asset = LoadOrCreate();

            asset.m_EnvironmentId = environment != null ? environment.Id : "editor";
            asset.m_EnvironmentName = environment != null ? environment.DisplayName : "Editor";
            asset.m_ProfileId = profileId ?? string.Empty;
            asset.m_Version = version ?? "0.0.0";
            asset.m_BuildNumber = buildNumber;
            asset.m_BuildTarget = target ?? string.Empty;
            asset.m_IsDevelopmentBuild = developmentBuild;
            asset.m_GitBranch = git != null ? git.Branch : string.Empty;
            asset.m_GitCommit = git != null ? git.ShortCommit : string.Empty;
            asset.m_GitDirty = git != null && git.IsDirty;
            asset.m_BuildTimeUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            asset.m_BuildMachine = SafeMachineName();

            asset.m_Variables.Clear();
            if (variables != null)
            {
                foreach (var pair in variables)
                    asset.m_Variables.Add(new BuildVariable(pair.Key, pair.Value));
            }

            asset.InvalidateLookup();

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        /// <summary>Deletes the generated asset and its Resources folder when it is left empty.</summary>
        public static void Delete()
        {
            if (Load() == null)
                return;

            AssetDatabase.DeleteAsset(ProjectPaths.BuildInfoAssetPath);

            var remaining = AssetDatabase.FindAssets(string.Empty,
                new[] { ProjectPaths.GeneratedResourcesFolder });

            if (remaining.Length == 0)
                AssetDatabase.DeleteAsset(ProjectPaths.GeneratedResourcesFolder);
        }

        private static string SafeMachineName()
        {
            try
            {
                return Environment.MachineName;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
