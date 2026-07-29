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
        /// The entries that would be published: defaults first, then the environment's own, with
        /// the environment winning on a shared key. Empty keys and missing assets are dropped.
        /// </summary>
        /// <param name="environment">Environment to resolve, may be null.</param>
        /// <param name="settings">Settings holding the defaults, may be null.</param>
        public static List<EnvironmentAssetEntry> Resolve(BuildEnvironment environment,
            BuildManagerSettings settings)
        {
            var byKey = new Dictionary<string, EnvironmentAssetEntry>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            void Apply(IReadOnlyList<EnvironmentAssetEntry> entries)
            {
                if (entries == null)
                    return;

                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.key) || entry.asset == null)
                        continue;

                    var key = entry.key.Trim();

                    if (!byKey.ContainsKey(key))
                        order.Add(key);

                    byKey[key] = new EnvironmentAssetEntry(key, entry.asset);
                }
            }

            Apply(settings != null ? settings.DefaultConfigAssets : null);
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
