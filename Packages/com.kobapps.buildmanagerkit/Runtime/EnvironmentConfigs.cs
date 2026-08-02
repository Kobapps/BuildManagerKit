using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildManagerKit
{
    /// <summary>
    /// Reads the configuration assets the built environment publishes.
    ///
    /// This is the front door for shipped code. Every call resolves against the single
    /// <see cref="EnvironmentAssets"/> instance baked into the player, so there is nothing to wire
    /// up, nothing to put in a scene, and no <c>Resources.Load</c> at the call site:
    ///
    /// <code>
    /// // The config the environment this player was built from publishes.
    /// var endpoints = EnvironmentConfigs.Get&lt;Endpoints&gt;();
    ///
    /// // Optional configs — a debug overlay only dev and stage ship.
    /// if (EnvironmentConfigs.TryGet&lt;DebugOverlayConfig&gt;(out var overlay))
    ///     ShowOverlay(overlay);
    ///
    /// // Configs the game cannot start without.
    /// var tuning = EnvironmentConfigs.Require&lt;TuningConfig&gt;();
    /// </code>
    ///
    /// A missing config is null rather than an exception, because "dev publishes a debug config and
    /// prod does not" is a normal thing to express and should not need a try/catch. When a config is
    /// genuinely mandatory, <see cref="Require{T}"/> says so and fails with a message naming the
    /// type and the environment instead of a <c>NullReferenceException</c> three frames later.
    /// </summary>
    /// <seealso cref="EnvironmentConfig"/>
    /// <seealso cref="EnvironmentAssets"/>
    public static class EnvironmentConfigs
    {
        /// <summary>
        /// Identifier of the environment these configs came from, e.g. <c>"prod"</c>. Reads
        /// <c>"editor"</c> in a project that has not baked an environment yet.
        /// </summary>
        public static string EnvironmentId => EnvironmentAssets.Current.EnvironmentId;

        /// <summary>Every config the environment publishes, in the order it lists them.</summary>
        public static IEnumerable<EnvironmentConfig> All => EnvironmentAssets.Current.Configs;

        /// <summary>
        /// The published config of type <typeparamref name="T"/>, or null when this environment does
        /// not publish one.
        /// </summary>
        /// <typeparam name="T">The config type.</typeparam>
        public static T Get<T>() where T : EnvironmentConfig => EnvironmentAssets.Current.GetConfig<T>();

        /// <summary>Non-throwing lookup, for configs only some environments publish.</summary>
        /// <typeparam name="T">The config type.</typeparam>
        /// <param name="config">The published config, or null.</param>
        public static bool TryGet<T>(out T config) where T : EnvironmentConfig =>
            EnvironmentAssets.Current.TryGetConfig(out config);

        /// <summary>True when this environment publishes a config of type <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The config type.</typeparam>
        public static bool Has<T>() where T : EnvironmentConfig => Get<T>() != null;

        /// <summary>
        /// The published config, or <paramref name="fallback"/> when there is none — for an optional
        /// config that has a sensible default baked into the calling code.
        /// </summary>
        /// <typeparam name="T">The config type.</typeparam>
        /// <param name="fallback">Returned when the environment publishes nothing of this type.</param>
        public static T GetOrDefault<T>(T fallback) where T : EnvironmentConfig
        {
            // Compared rather than coalesced: ?? bypasses Unity's null operator, so a destroyed
            // asset would satisfy it and be handed back in place of the fallback.
            var config = Get<T>();
            return config != null ? config : fallback;
        }

        /// <summary>
        /// The published config, or a fresh in-memory instance carrying the type's field defaults.
        ///
        /// Useful for a config whose absence should degrade rather than crash: a feature reading
        /// defaults still runs, and the environment can start publishing an asset later without any
        /// code change. The instance is not saved and is created once per call.
        /// </summary>
        /// <typeparam name="T">The config type.</typeparam>
        public static T GetOrCreate<T>() where T : EnvironmentConfig
        {
            var config = Get<T>();
            if (config != null)
                return config;

            var created = ScriptableObject.CreateInstance<T>();
            created.name = typeof(T).Name + " (Defaults)";
            created.hideFlags = HideFlags.HideAndDontSave;
            return created;
        }

        /// <summary>
        /// The published config, or an exception naming what is missing and where.
        ///
        /// Use it for configs the game genuinely cannot run without. Failing here, with the type and
        /// the environment in the message, is the difference between "prod forgot to publish
        /// Endpoints" and a null reference in whichever system happened to touch it first.
        /// </summary>
        /// <typeparam name="T">The config type.</typeparam>
        /// <exception cref="InvalidOperationException">The environment publishes no such config.</exception>
        public static T Require<T>() where T : EnvironmentConfig
        {
            var config = Get<T>();

            if (config != null)
                return config;

            throw new InvalidOperationException(
                $"[BuildManagerKit] Environment '{EnvironmentId}' publishes no {typeof(T).Name}. "
                + "Add the asset to that environment's Configs list in the Build Manager window "
                + "(Tools > Build Manager Kit > Build Manager > Environments).");
        }

        /// <summary>
        /// A config addressed by key rather than by type, for the rare asset that overrides its
        /// <see cref="EnvironmentConfig.ConfigKey"/> because one environment publishes two of the
        /// same type.
        /// </summary>
        /// <typeparam name="T">The config type.</typeparam>
        /// <param name="key">The key the asset is published under.</param>
        public static T Get<T>(string key) where T : EnvironmentConfig =>
            EnvironmentAssets.Current.Get<T>(key);
    }
}
