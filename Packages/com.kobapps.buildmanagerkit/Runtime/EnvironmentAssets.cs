using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildManagerKit
{
    /// <summary>One asset an environment publishes to runtime code, addressed by key.</summary>
    [Serializable]
    public struct EnvironmentAssetEntry
    {
        /// <summary>Lookup key, e.g. <c>"gameConfig"</c>.</summary>
        public string key;

        /// <summary>
        /// The asset itself: a ScriptableObject, a TextAsset holding JSON, a texture, an audio
        /// clip — anything Unity can reference.
        /// </summary>
        public UnityEngine.Object asset;

        /// <summary>Creates an entry.</summary>
        public EnvironmentAssetEntry(string key, UnityEngine.Object asset)
        {
            this.key = key;
            this.asset = asset;
        }
    }

    /// <summary>
    /// The assets the active environment publishes, available to shipped code.
    ///
    /// Each environment lists key/asset pairs — a balancing ScriptableObject, a JSON
    /// <c>TextAsset</c> of endpoints, a splash image — and the build bakes the active
    /// environment's list into <c>Assets/Resources/BuildManagerKit/EnvironmentAssets.asset</c>.
    ///
    /// Because the generated asset holds direct references, Unity's dependency scanner pulls in
    /// exactly the assets the environment being built uses and nothing else: a 200 MB debug atlas
    /// referenced only by the dev environment never reaches a prod build.
    /// </summary>
    /// <example>
    /// <code>
    /// // A ScriptableObject of tuning values, different per environment.
    /// var config = EnvironmentAssets.Current.Get&lt;GameConfig&gt;("gameConfig");
    ///
    /// // A JSON TextAsset parsed into your own type.
    /// if (EnvironmentAssets.Current.TryGetJson&lt;Endpoints&gt;("endpoints", out var endpoints))
    ///     Api.Configure(endpoints.baseUrl);
    ///
    /// // An image that differs between flavours.
    /// splash.texture = EnvironmentAssets.Current.GetTexture("splash");
    /// </code>
    /// </example>
    public sealed class EnvironmentAssets : ScriptableObject
    {
        /// <summary>Resources relative path of the generated asset.</summary>
        public const string ResourcePath = "BuildManagerKit/EnvironmentAssets";

        [SerializeField] internal string m_EnvironmentId = "editor";
        [SerializeField] internal List<EnvironmentAssetEntry> m_Entries = new List<EnvironmentAssetEntry>();

        private static EnvironmentAssets s_Current;
        private Dictionary<string, UnityEngine.Object> m_Lookup;
        private Dictionary<Type, EnvironmentConfig> m_ConfigLookup;

        /// <summary>
        /// The set baked for the running player. Never null: a project that has not generated one
        /// yet gets an empty in-memory instance, so lookups return the fallback instead of
        /// throwing.
        /// </summary>
        public static EnvironmentAssets Current
        {
            get
            {
                if (s_Current != null)
                    return s_Current;

                s_Current = Resources.Load<EnvironmentAssets>(ResourcePath);

                if (s_Current == null)
                {
                    s_Current = CreateInstance<EnvironmentAssets>();
                    s_Current.name = "EnvironmentAssets (Unbaked)";
                    s_Current.hideFlags = HideFlags.HideAndDontSave;
                }

                return s_Current;
            }
        }

        /// <summary>Identifier of the environment these assets came from.</summary>
        public string EnvironmentId => m_EnvironmentId;

        /// <summary>Every published entry.</summary>
        public IReadOnlyList<EnvironmentAssetEntry> Entries => m_Entries;

        /// <summary>Every published key.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var entry in m_Entries)
                {
                    if (!string.IsNullOrEmpty(entry.key))
                        yield return entry.key;
                }
            }
        }

        /// <summary>True when <paramref name="key"/> is published and its asset still exists.</summary>
        public bool Has(string key) => Lookup(key) != null;

        /// <summary>
        /// The asset published under <paramref name="key"/>, or null when it is absent or of a
        /// different type.
        /// </summary>
        /// <typeparam name="T">Expected asset type.</typeparam>
        /// <param name="key">Lookup key, case-insensitive.</param>
        public T Get<T>(string key) where T : UnityEngine.Object
        {
            var asset = Lookup(key);

            if (asset == null)
                return null;

            if (asset is T typed)
                return typed;

            // A GameObject asked for as a Component, or similar: let Unity try the conversion
            // before giving up, then say plainly what was found instead.
            Debug.LogWarning(
                $"[BuildManagerKit] Environment asset '{key}' is a {asset.GetType().Name}, not a {typeof(T).Name}.");

            return null;
        }

        /// <summary>Non-throwing variant of <see cref="Get{T}"/>.</summary>
        public bool TryGet<T>(string key, out T value) where T : UnityEngine.Object
        {
            var asset = Lookup(key);
            value = asset as T;
            return value != null;
        }

        /// <summary>The asset published under <paramref name="key"/>, or <paramref name="fallback"/>.</summary>
        public T GetOrDefault<T>(string key, T fallback) where T : UnityEngine.Object =>
            Lookup(key) is T typed ? typed : fallback;

        /// <summary>Text of a published <see cref="TextAsset"/>.</summary>
        public string GetText(string key, string fallback = "")
        {
            var asset = Lookup(key) as TextAsset;
            return asset != null ? asset.text : fallback;
        }

        /// <summary>Parses a published JSON <see cref="TextAsset"/> with <see cref="JsonUtility"/>.</summary>
        /// <typeparam name="T">Type to deserialise into.</typeparam>
        /// <param name="key">Lookup key.</param>
        public T GetJson<T>(string key) => TryGetJson<T>(key, out var value) ? value : default;

        /// <summary>Non-throwing JSON parse; malformed content is reported rather than thrown.</summary>
        public bool TryGetJson<T>(string key, out T value)
        {
            value = default;

            var asset = Lookup(key) as TextAsset;
            if (asset == null)
                return false;

            try
            {
                value = JsonUtility.FromJson<T>(asset.text);
                return value != null;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[BuildManagerKit] Environment asset '{key}' is not valid JSON for {typeof(T).Name}: "
                    + exception.Message);
                return false;
            }
        }

        /// <summary>A published texture, or null.</summary>
        public Texture2D GetTexture(string key) => Lookup(key) as Texture2D;

        /// <summary>A published sprite, or null.</summary>
        public Sprite GetSprite(string key) => Lookup(key) as Sprite;

        /// <summary>A published audio clip, or null.</summary>
        public AudioClip GetAudioClip(string key) => Lookup(key) as AudioClip;

        /// <summary>A published ScriptableObject of the given type, or null.</summary>
        public T GetConfig<T>(string key) where T : ScriptableObject => Get<T>(key);

        /// <summary>
        /// Every published <see cref="EnvironmentConfig"/>, in the order the environment lists them.
        /// The plain keyed assets — textures, TextAssets, ScriptableObjects that do not derive from
        /// <see cref="EnvironmentConfig"/> — are not included; read those with <see cref="Get{T}"/>.
        /// </summary>
        public IEnumerable<EnvironmentConfig> Configs
        {
            get
            {
                foreach (var entry in m_Entries)
                {
                    if (entry.asset is EnvironmentConfig config && config != null)
                        yield return config;
                }
            }
        }

        /// <summary>
        /// The published config of type <typeparamref name="T"/>, or null when this environment does
        /// not publish one.
        ///
        /// Resolved by type rather than by key, so it keeps working when an asset overrides its
        /// <see cref="EnvironmentConfig.ConfigKey"/>. An exact type match wins; failing that the
        /// first assignable config is returned, which is what makes asking for a base class work
        /// when each environment publishes a different subclass.
        /// </summary>
        /// <typeparam name="T">The config type to look for.</typeparam>
        public T GetConfig<T>() where T : EnvironmentConfig
        {
            if (m_ConfigLookup == null)
            {
                m_ConfigLookup = new Dictionary<Type, EnvironmentConfig>();

                foreach (var entry in m_Entries)
                {
                    // Exact type only: a subclass must not claim its base's slot, or the assignable
                    // scan below would never get the chance to pick between two of them.
                    if (entry.asset is EnvironmentConfig config && config != null)
                        m_ConfigLookup.TryAdd(config.GetType(), config);
                }
            }

            if (m_ConfigLookup.TryGetValue(typeof(T), out var exact))
                return (T)exact;

            foreach (var candidate in m_ConfigLookup.Values)
            {
                if (candidate is T assignable)
                    return assignable;
            }

            return null;
        }

        /// <summary>Non-throwing variant of <see cref="GetConfig{T}()"/>.</summary>
        /// <typeparam name="T">The config type to look for.</typeparam>
        /// <param name="config">The published config, or null.</param>
        public bool TryGetConfig<T>(out T config) where T : EnvironmentConfig
        {
            config = GetConfig<T>();
            return config != null;
        }

        private UnityEngine.Object Lookup(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            if (m_Lookup == null)
            {
                m_Lookup = new Dictionary<string, UnityEngine.Object>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in m_Entries)
                {
                    if (!string.IsNullOrEmpty(entry.key) && entry.asset != null)
                        m_Lookup[entry.key] = entry.asset;
                }
            }

            return m_Lookup.TryGetValue(key, out var asset) ? asset : null;
        }

        internal void InvalidateLookup()
        {
            m_Lookup = null;
            m_ConfigLookup = null;
        }

        private void OnEnable()
        {
            m_Entries ??= new List<EnvironmentAssetEntry>();
            InvalidateLookup();
        }

        /// <inheritdoc />
        public override string ToString() => $"{m_EnvironmentId}: {m_Entries.Count} asset(s)";
    }
}
