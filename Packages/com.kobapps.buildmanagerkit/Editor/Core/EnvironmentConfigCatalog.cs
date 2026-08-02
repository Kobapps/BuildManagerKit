using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Everything the Editor needs to know about <see cref="EnvironmentConfig"/> assets: which types
    /// a project defines, which assets exist, which environments publish each of them, and how to
    /// create, attach and detach one.
    ///
    /// It lives apart from the window so the same operations are available to the CLI, the integrity
    /// check and the tests — a config attached from a script must end up in exactly the state a
    /// config attached by dragging does, or the two paths drift.
    /// </summary>
    public static class EnvironmentConfigCatalog
    {
        /// <summary>Folder new config assets are created in.</summary>
        public const string ConfigsFolder = ProjectPaths.DefaultSettingsFolder + "/Configs";

        /// <summary>
        /// Every concrete config type the project defines, sorted by name.
        ///
        /// Abstract bases are left out: they are a way of grouping configs, not something an
        /// environment can publish.
        ///
        /// Nested types are left out too, and that one is not cosmetic. Unity resolves a
        /// ScriptableObject's <c>MonoScript</c> by finding a file named after the class, which a
        /// nested type never has — creating an asset from one produces a file with
        /// <c>m_Script: {fileID: 0}</c> that no build can load. <see cref="TypeCache"/> reports
        /// every loaded assembly including the test ones, whose fixtures are exactly this shape, so
        /// without the filter the New menu offers types that cannot work.
        /// </summary>
        public static IReadOnlyList<Type> ConfigTypes =>
            TypeCache.GetTypesDerivedFrom<EnvironmentConfig>()
                .Where(IsCreatable)
                .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// True when an asset of this type can actually be created and loaded: a concrete,
        /// top-level, non-generic config.
        /// </summary>
        /// <param name="type">Candidate config type.</param>
        public static bool IsCreatable(Type type) =>
            type != null
            && typeof(EnvironmentConfig).IsAssignableFrom(type)
            && !type.IsAbstract
            && !type.IsGenericTypeDefinition
            && !type.IsNested;

        /// <summary>Every config asset in the project, optionally narrowed to one type.</summary>
        /// <param name="type">Type to filter by, or null for all configs.</param>
        public static IReadOnlyList<EnvironmentConfig> FindAll(Type type = null)
        {
            // Searched by short name, which every Unity version accepts, then filtered by the real
            // type: a same-named type in another namespace would otherwise slip through, and a
            // qualified t: filter is not reliably supported.
            var searched = type ?? typeof(EnvironmentConfig);

            return AssetDatabase.FindAssets("t:" + searched.Name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<EnvironmentConfig>)
                .Where(config => config != null && searched.IsInstanceOfType(config))
                .OrderBy(config => config.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The environments that publish <paramref name="config"/>.
        ///
        /// This is what makes reuse visible. An asset listed by three environments is edited once
        /// and changes all three, which is the whole point — and is also exactly the kind of thing
        /// someone needs to be told before they edit it.
        /// </summary>
        /// <param name="config">The config to look for.</param>
        /// <param name="settings">Settings holding the environments.</param>
        public static IReadOnlyList<BuildEnvironment> UsedBy(EnvironmentConfig config,
            BuildManagerSettings settings)
        {
            if (config == null || settings == null)
                return Array.Empty<BuildEnvironment>();

            return settings.Environments
                .Where(environment => environment != null && environment.Configs.Contains(config))
                .ToList();
        }

        /// <summary>
        /// Creates a config asset of <paramref name="type"/> and attaches it to
        /// <paramref name="environment"/>.
        /// </summary>
        /// <param name="type">Concrete <see cref="EnvironmentConfig"/> type to instantiate.</param>
        /// <param name="environment">Environment to attach it to, or null to only create the asset.</param>
        /// <param name="nameHint">
        /// Suffix for the asset name, usually the environment id — <c>Endpoints_prod</c> reads better
        /// in the project window than three assets all called <c>Endpoints</c>.
        /// </param>
        public static EnvironmentConfig Create(Type type, BuildEnvironment environment = null,
            string nameHint = null)
        {
            if (!IsCreatable(type))
            {
                Debug.LogError(
                    $"[BuildManagerKit] '{type}' cannot be created as a config asset. It has to be a "
                    + "concrete, top-level, non-generic class deriving from EnvironmentConfig, declared in "
                    + "a file of its own name.");

                return null;
            }

            ProjectPaths.EnsureAssetFolder(ConfigsFolder);

            var config = (EnvironmentConfig)ScriptableObject.CreateInstance(type);
            var suffix = string.IsNullOrWhiteSpace(nameHint) ? string.Empty : "_" + BuildTokens.Sanitize(nameHint);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{ConfigsFolder}/{type.Name}{suffix}.asset");

            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();

            if (environment != null)
                Attach(environment, config);

            return config;
        }

        /// <summary>
        /// Adds an existing config to an environment, undoably. Already-listed configs are ignored:
        /// publishing the same asset twice would only produce a duplicate key.
        /// </summary>
        /// <param name="environment">Environment to add to.</param>
        /// <param name="config">Config to publish.</param>
        /// <returns>True when the list changed.</returns>
        public static bool Attach(BuildEnvironment environment, EnvironmentConfig config)
        {
            if (environment == null || config == null || environment.Configs.Contains(config))
                return false;

            Undo.RecordObject(environment, "Add Config");
            environment.ConfigsMutable.Add(config);
            EditorUtility.SetDirty(environment);

            // Guarded: an environment held only in memory — a test fixture, or one being built up
            // before it is written — is not something the AssetDatabase can be asked to save.
            if (EditorUtility.IsPersistent(environment))
                AssetDatabase.SaveAssetIfDirty(environment);

            return true;
        }

        /// <summary>
        /// Removes a config from an environment, undoably. The asset itself is left alone — it is
        /// very likely still published by another environment, and deleting it is a separate,
        /// louder decision.
        /// </summary>
        /// <param name="environment">Environment to remove from.</param>
        /// <param name="config">Config to stop publishing.</param>
        /// <returns>True when the list changed.</returns>
        public static bool Detach(BuildEnvironment environment, EnvironmentConfig config)
        {
            if (environment == null || config == null)
                return false;

            Undo.RecordObject(environment, "Remove Config");

            if (!environment.ConfigsMutable.Remove(config))
                return false;

            EditorUtility.SetDirty(environment);

            // Guarded: an environment held only in memory — a test fixture, or one being built up
            // before it is written — is not something the AssetDatabase can be asked to save.
            if (EditorUtility.IsPersistent(environment))
                AssetDatabase.SaveAssetIfDirty(environment);

            return true;
        }

        /// <summary>
        /// The configs published by every environment except <paramref name="environment"/>, grouped
        /// by the config. Drives the "add one that another environment already uses" picker, which is
        /// the shortest path to sharing an asset rather than duplicating it.
        /// </summary>
        /// <param name="environment">The environment being edited.</param>
        /// <param name="settings">Settings holding the environments.</param>
        public static IReadOnlyList<EnvironmentConfig> PublishedElsewhere(BuildEnvironment environment,
            BuildManagerSettings settings)
        {
            if (settings == null)
                return Array.Empty<EnvironmentConfig>();

            return settings.Environments
                .Where(candidate => candidate != null && candidate != environment)
                .SelectMany(candidate => candidate.Configs)
                .Where(config => config != null)
                .Distinct()
                .Where(config => environment == null || !environment.Configs.Contains(config))
                .OrderBy(config => config.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// A short description of a config for a list row: its type, its key when that differs, and
        /// whatever <see cref="EnvironmentConfig.Summary"/> the type chooses to add.
        /// </summary>
        /// <param name="config">The config to describe.</param>
        public static string Describe(EnvironmentConfig config)
        {
            if (config == null)
                return "missing config";

            var parts = new List<string> { config.GetType().Name };

            if (config.HasExplicitKey)
                parts.Add("key '" + config.ConfigKey + "'");

            var summary = config.Summary;
            if (!string.IsNullOrWhiteSpace(summary))
                parts.Add(summary.Trim());

            return string.Join(" · ", parts);
        }
    }
}
