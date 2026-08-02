using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Generates <c>Assets/Resources/BuildManagerKit/EnvironmentAssets.asset</c> — the bridge that
    /// puts an environment's config assets in reach of shipped code.
    ///
    /// The generated asset holds direct references, which is the point: Unity's dependency scanner
    /// then pulls in exactly the assets the environment being built publishes. Assets belonging to
    /// the other environments are never referenced from a Resources asset, so they stay out of the
    /// player entirely.
    /// </summary>
    public static class EnvironmentAssetsWriter
    {
        /// <summary>Asset path of the generated file.</summary>
        public const string AssetPath = ProjectPaths.GeneratedResourcesFolder + "/EnvironmentAssets.asset";

        /// <summary>Loads the generated asset, or null when it has not been created yet.</summary>
        public static EnvironmentAssets Load() => AssetDatabase.LoadAssetAtPath<EnvironmentAssets>(AssetPath);

        /// <summary>Loads the generated asset, creating it when missing.</summary>
        public static EnvironmentAssets LoadOrCreate()
        {
            var asset = Load();
            if (asset != null)
                return asset;

            ProjectPaths.EnsureAssetFolder(ProjectPaths.GeneratedResourcesFolder);

            asset = ScriptableObject.CreateInstance<EnvironmentAssets>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();

            return asset;
        }

        /// <summary>
        /// Merges the project-wide defaults with <paramref name="environment"/>'s own entries and
        /// writes the result. Entries the environment declares win over a default with the same
        /// key, matching how its actions override the global ones.
        /// </summary>
        /// <param name="environment">Environment being published, or null to publish only defaults.</param>
        /// <param name="settings">Settings holding the defaults.</param>
        /// <param name="log">Optional log for a one-line summary.</param>
        public static void Write(BuildEnvironment environment, BuildManagerSettings settings, IBuildLog log = null)
        {
            var merged = Resolve(environment, settings);
            var asset = LoadOrCreate();

            asset.m_EnvironmentId = environment != null ? environment.Id : "editor";
            asset.m_Entries.Clear();
            asset.m_Entries.AddRange(merged);
            asset.InvalidateLookup();

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            if (merged.Count > 0)
                log?.Info($"Published {merged.Count} config asset(s): {string.Join(", ", merged.Select(e => e.key))}");
        }

        /// <summary>
        /// The entries that would be published, in precedence order: the project-wide defaults, then
        /// the environment's typed configs, then its own keyed entries. Later layers win on a shared
        /// key. Empty keys and missing assets are dropped.
        ///
        /// Typed configs sit between the two so a keyed entry can still override one by name — the
        /// escape hatch for a project that has both — while a config never silently loses to a
        /// project-wide default it knows nothing about.
        /// </summary>
        /// <param name="environment">Environment to resolve, may be null.</param>
        /// <param name="settings">Settings holding the defaults, may be null.</param>
        public static List<EnvironmentAssetEntry> Resolve(BuildEnvironment environment,
            BuildManagerSettings settings)
        {
            var byKey = new Dictionary<string, EnvironmentAssetEntry>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            void Add(string key, UnityEngine.Object asset)
            {
                if (string.IsNullOrWhiteSpace(key) || asset == null)
                    return;

                var trimmed = key.Trim();

                if (!byKey.ContainsKey(trimmed))
                    order.Add(trimmed);

                byKey[trimmed] = new EnvironmentAssetEntry(trimmed, asset);
            }

            void Apply(IReadOnlyList<EnvironmentAssetEntry> entries)
            {
                if (entries == null)
                    return;

                foreach (var entry in entries)
                    Add(entry.key, entry.asset);
            }

            void ApplyConfigs(IReadOnlyList<EnvironmentConfig> configs)
            {
                if (configs == null)
                    return;

                foreach (var config in configs)
                {
                    if (config != null)
                        Add(config.ConfigKey, config);
                }
            }

            Apply(settings != null ? settings.DefaultConfigAssets : null);
            ApplyConfigs(environment != null ? environment.Configs : null);
            Apply(environment != null ? environment.ConfigAssets : null);

            return order.Select(key => byKey[key]).ToList();
        }

        /// <summary>Deletes the generated asset.</summary>
        public static void Delete()
        {
            if (Load() != null)
                AssetDatabase.DeleteAsset(AssetPath);
        }
    }
}
