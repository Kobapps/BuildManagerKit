using System;
using UnityEngine;

namespace BuildManagerKit
{
    /// <summary>
    /// Base class for a configuration asset an environment publishes to runtime code.
    ///
    /// Derive from this, add whatever fields the feature needs, and the asset becomes something an
    /// environment can list — with the type itself acting as the address:
    ///
    /// <code>
    /// public sealed class Endpoints : EnvironmentConfig
    /// {
    ///     public string baseUrl = "https://api.example.com";
    ///     public float timeoutSeconds = 10f;
    /// }
    ///
    /// // Anywhere in shipped code, no key and no Resources.Load:
    /// var endpoints = EnvironmentConfigs.Get&lt;Endpoints&gt;();
    /// </code>
    ///
    /// The point of the base class is that address. A plain <c>ScriptableObject</c> published under
    /// a string key works — that is what <see cref="EnvironmentAssets"/> has always done — but the
    /// key has to be spelled identically in the asset list and at every call site, and a typo
    /// surfaces as a null reference at runtime rather than as a compile error. Deriving from
    /// <see cref="EnvironmentConfig"/> replaces the string with the type.
    ///
    /// One asset can be listed by any number of environments. That is how a value shared by dev and
    /// stage but not prod is expressed: one asset, referenced twice, edited once.
    /// </summary>
    /// <seealso cref="EnvironmentConfigs"/>
    public abstract class EnvironmentConfig : ScriptableObject
    {
        [Tooltip("Optional. Overrides the lookup key, which is otherwise the type name. Only needed when "
                 + "an environment publishes two assets of the same type — leave it empty and the type "
                 + "alone identifies the config.")]
        [SerializeField] private string m_ConfigKey = string.Empty;

        /// <summary>
        /// The key this config is published under: <see cref="DefaultKey(Type)"/> unless the asset
        /// names one of its own.
        ///
        /// Runtime code rarely needs this — <see cref="EnvironmentConfigs.Get{T}()"/> resolves by
        /// type. It exists because the published set is ultimately key-addressed, so a config and a
        /// plain keyed asset can collide, and a collision is much easier to explain when both sides
        /// can be named.
        /// </summary>
        public string ConfigKey =>
            string.IsNullOrWhiteSpace(m_ConfigKey) ? DefaultKey(GetType()) : m_ConfigKey.Trim();

        /// <summary>
        /// True when the asset overrides the key rather than taking its type name. The editor shows
        /// this, because an overridden key is the one case where two assets of the same type do not
        /// fight over the same slot.
        /// </summary>
        public bool HasExplicitKey => !string.IsNullOrWhiteSpace(m_ConfigKey);

        /// <summary>
        /// The key a config type takes when the asset does not override it: the type's name, without
        /// namespace.
        ///
        /// Deliberately not the full name. The key ends up in generated assets, CLI output and
        /// validation messages, and <c>Endpoints</c> reads in all of those where
        /// <c>Studio.Game.Backend.Endpoints</c> does not. The cost is that two config types with the
        /// same short name collide — which the integrity check reports rather than leaving to be
        /// discovered in a build.
        /// </summary>
        /// <param name="type">The config type.</param>
        public static string DefaultKey(Type type) => type != null ? type.Name : string.Empty;

        /// <summary>The key <typeparamref name="T"/> takes when the asset does not override it.</summary>
        /// <typeparam name="T">The config type.</typeparam>
        public static string DefaultKey<T>() where T : EnvironmentConfig => DefaultKey(typeof(T));

        /// <summary>
        /// A one-line description shown beside the asset in the Build Manager window, so a list of
        /// configs can be read without opening each one. Override it to summarise the values that
        /// actually differ between environments — a base URL, a log level, a feature flag count.
        /// </summary>
        public virtual string Summary => string.Empty;

        /// <inheritdoc />
        public override string ToString() => $"{GetType().Name} ({ConfigKey})";
    }
}
