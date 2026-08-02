using System.Collections.Generic;
using System.Linq;
using BuildManagerKit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Tests
{
    /// <summary>
    /// Typed configs: how a config is addressed, how the same asset is shared between environments,
    /// and what runtime code gets back.
    /// </summary>
    [TestFixture]
    internal sealed class EnvironmentConfigTests
    {
        private readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in m_Created)
                Object.DestroyImmediate(asset);

            m_Created.Clear();
        }

        // ---------------------------------------------------------------- keys

        [Test]
        public void Key_DefaultsToTheTypeName()
        {
            Assert.AreEqual(nameof(TuningConfig), CreateConfig<TuningConfig>().ConfigKey);
            Assert.AreEqual(nameof(TuningConfig), EnvironmentConfig.DefaultKey<TuningConfig>());
        }

        [Test]
        public void Key_CanBeOverriddenOnTheAsset()
        {
            var config = CreateConfig<TuningConfig>("combat");

            Assert.AreEqual("combat", config.ConfigKey);
            Assert.IsTrue(config.HasExplicitKey);
        }

        [Test]
        public void Key_TreatsWhitespaceAsNoOverride()
        {
            var config = CreateConfig<TuningConfig>("   ");

            Assert.AreEqual(nameof(TuningConfig), config.ConfigKey);
            Assert.IsFalse(config.HasExplicitKey);
        }

        // ---------------------------------------------------------------- publishing

        [Test]
        public void Resolve_PublishesConfigsUnderTheirKey()
        {
            var settings = CreateSettings();
            var environment = CreateEnvironment("dev", CreateConfig<TuningConfig>());

            var resolved = EnvironmentAssetsWriter.Resolve(environment, settings);

            Assert.AreEqual(1, resolved.Count);
            Assert.AreEqual(nameof(TuningConfig), resolved[0].key);
        }

        [Test]
        public void Resolve_PublishesTheSameAssetFromEveryEnvironmentThatListsIt()
        {
            // The whole point of sharing: one asset, several environments, edited once.
            var shared = CreateConfig<TuningConfig>();
            var settings = CreateSettings();

            var dev = CreateEnvironment("dev", shared);
            var stage = CreateEnvironment("stage", shared);

            Assert.AreSame(shared, EnvironmentAssetsWriter.Resolve(dev, settings).Single().asset);
            Assert.AreSame(shared, EnvironmentAssetsWriter.Resolve(stage, settings).Single().asset);
        }

        [Test]
        public void Resolve_LetsEachEnvironmentPublishItsOwnAssetOfTheSameType()
        {
            var settings = CreateSettings();
            var devTuning = CreateConfig<TuningConfig>();
            var prodTuning = CreateConfig<TuningConfig>();

            var dev = CreateEnvironment("dev", devTuning);
            var prod = CreateEnvironment("prod", prodTuning);

            // Same key in both — which is exactly what makes Get<TuningConfig>() work everywhere.
            Assert.AreSame(devTuning, EnvironmentAssetsWriter.Resolve(dev, settings).Single().asset);
            Assert.AreSame(prodTuning, EnvironmentAssetsWriter.Resolve(prod, settings).Single().asset);
        }

        [Test]
        public void Resolve_ConfigOverridesAProjectWideDefaultOnTheSameKey()
        {
            var fallback = CreateTextAsset("default");
            var settings = CreateSettings((nameof(TuningConfig), fallback));

            var config = CreateConfig<TuningConfig>();
            var environment = CreateEnvironment("prod", config);

            var resolved = EnvironmentAssetsWriter.Resolve(environment, settings);

            Assert.AreEqual(1, resolved.Count, "The config must replace the default, not sit beside it.");
            Assert.AreSame(config, resolved[0].asset);
        }

        [Test]
        public void Resolve_KeyedEntryStillWinsOverAConfig()
        {
            // The escape hatch for a project mixing both styles; documented precedence.
            var replacement = CreateTextAsset("hand-picked");
            var settings = CreateSettings();

            var environment = CreateEnvironment("dev", CreateConfig<TuningConfig>());
            AddKeyedEntry(environment, nameof(TuningConfig), replacement);

            Assert.AreSame(replacement, EnvironmentAssetsWriter.Resolve(environment, settings).Single().asset);
        }

        [Test]
        public void Resolve_SkipsEmptySlots()
        {
            var settings = CreateSettings();
            var environment = CreateEnvironment("dev", CreateConfig<TuningConfig>(), null);

            Assert.AreEqual(1, EnvironmentAssetsWriter.Resolve(environment, settings).Count);
        }

        // ---------------------------------------------------------------- runtime lookups

        [Test]
        public void Runtime_FindsAConfigByItsType()
        {
            var config = CreateConfig<TuningConfig>();
            var assets = CreateRuntimeAssets(config);

            Assert.AreSame(config, assets.GetConfig<TuningConfig>());
            Assert.IsTrue(assets.TryGetConfig<TuningConfig>(out _));
        }

        [Test]
        public void Runtime_FindsAConfigWhoseKeyWasOverridden()
        {
            // Type-based lookup is what makes an overridden key invisible to calling code.
            var config = CreateConfig<TuningConfig>("combat");
            var assets = CreateRuntimeAssets(config);

            Assert.AreSame(config, assets.GetConfig<TuningConfig>());
        }

        [Test]
        public void Runtime_ReturnsNullForAConfigThisEnvironmentDoesNotPublish()
        {
            var assets = CreateRuntimeAssets(CreateConfig<TuningConfig>());

            Assert.IsNull(assets.GetConfig<EndpointsConfig>());
            Assert.IsFalse(assets.TryGetConfig<EndpointsConfig>(out _));
        }

        [Test]
        public void Runtime_AsksForABaseTypeAndGetsTheSubclass()
        {
            var config = CreateConfig<DerivedTuningConfig>();
            var assets = CreateRuntimeAssets(config);

            Assert.AreSame(config, assets.GetConfig<TuningConfig>());
        }

        [Test]
        public void Runtime_PrefersAnExactTypeMatchOverAnAssignableOne()
        {
            var exact = CreateConfig<TuningConfig>();
            var derived = CreateConfig<DerivedTuningConfig>("derived");

            // Derived listed first, so a naive scan would return it.
            var assets = CreateRuntimeAssets(derived, exact);

            Assert.AreSame(exact, assets.GetConfig<TuningConfig>());
        }

        [Test]
        public void Runtime_ConfigsListsOnlyConfigs()
        {
            var config = CreateConfig<TuningConfig>();

            var assets = ScriptableObject.CreateInstance<EnvironmentAssets>();
            assets.hideFlags = HideFlags.HideAndDontSave;
            m_Created.Add(assets);

            assets.m_Entries.Add(new EnvironmentAssetEntry("notes", CreateTextAsset("plain")));
            assets.m_Entries.Add(new EnvironmentAssetEntry(config.ConfigKey, config));
            assets.InvalidateLookup();

            CollectionAssert.AreEqual(new[] { config }, assets.Configs.ToArray());
        }

        [Test]
        public void Runtime_FacadeIsUsableWithoutABakedAsset()
        {
            // Shipped code calls these unguarded in a project that has never built.
            Assert.IsNotNull(EnvironmentConfigs.EnvironmentId);
            Assert.IsNull(EnvironmentConfigs.Get<TuningConfig>());
            Assert.IsFalse(EnvironmentConfigs.Has<TuningConfig>());

            var fallback = CreateConfig<TuningConfig>();
            Assert.AreSame(fallback, EnvironmentConfigs.GetOrDefault(fallback));
        }

        [Test]
        public void Runtime_RequireNamesWhatIsMissing()
        {
            var exception = Assert.Throws<System.InvalidOperationException>(
                () => EnvironmentConfigs.Require<TuningConfig>());

            Assert.IsTrue(exception.Message.Contains(nameof(TuningConfig)),
                "The message has to name the type, or it is no better than a null reference.");
        }

        // ---------------------------------------------------------------- editor lookups

        [Test]
        public void Editor_EnvironmentResolvesItsOwnConfigByType()
        {
            var config = CreateConfig<TuningConfig>();
            var environment = CreateEnvironment("dev", config);

            Assert.AreSame(config, environment.GetConfig<TuningConfig>());
            Assert.IsNull(environment.GetConfig<EndpointsConfig>());
        }

        [Test]
        public void Editor_AttachIsIdempotentAndDetachUndoesIt()
        {
            var config = CreateConfig<TuningConfig>();
            var environment = CreateEnvironment("dev");

            Assert.IsTrue(EnvironmentConfigCatalog.Attach(environment, config));
            Assert.IsFalse(EnvironmentConfigCatalog.Attach(environment, config),
                "Publishing the same asset twice would only produce a duplicate key.");

            Assert.AreEqual(1, environment.Configs.Count);
            Assert.IsTrue(EnvironmentConfigCatalog.Detach(environment, config));
            Assert.AreEqual(0, environment.Configs.Count);
        }

        [Test]
        public void Editor_UsedByListsEveryEnvironmentSharingTheAsset()
        {
            var shared = CreateConfig<TuningConfig>();
            var settings = CreateSettings();

            var dev = CreateEnvironment("dev", shared);
            var stage = CreateEnvironment("stage", shared);
            var prod = CreateEnvironment("prod");

            settings.EnvironmentsMutable.AddRange(new[] { dev, stage, prod });

            CollectionAssert.AreEquivalent(new[] { dev, stage },
                EnvironmentConfigCatalog.UsedBy(shared, settings).ToArray());
        }

        [Test]
        public void Editor_PublishedElsewhereOffersWhatAnotherEnvironmentAlreadyUses()
        {
            var shared = CreateConfig<TuningConfig>();
            var settings = CreateSettings();

            var dev = CreateEnvironment("dev", shared);
            var prod = CreateEnvironment("prod");

            settings.EnvironmentsMutable.AddRange(new[] { dev, prod });

            CollectionAssert.AreEqual(new[] { shared },
                EnvironmentConfigCatalog.PublishedElsewhere(prod, settings).ToArray());

            CollectionAssert.IsEmpty(EnvironmentConfigCatalog.PublishedElsewhere(dev, settings),
                "An environment must not be offered a config it already publishes.");
        }

        // ---------------------------------------------------------------- integrity

        [Test]
        public void Integrity_FlagsTwoConfigsOfTheSameTypeInOneEnvironment()
        {
            var settings = CreateSettings();
            settings.EnvironmentsMutable.Add(
                CreateEnvironment("dev", CreateConfig<TuningConfig>(), CreateConfig<TuningConfig>()));

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Issues.Any(issue => issue.Message.Contains(nameof(TuningConfig))));
        }

        [Test]
        public void Integrity_AcceptsTwoOfTheSameTypeOnceOneHasItsOwnKey()
        {
            var settings = CreateSettings();
            settings.EnvironmentsMutable.Add(
                CreateEnvironment("dev", CreateConfig<TuningConfig>(), CreateConfig<TuningConfig>("combat")));

            // Asserted against the collision message rather than HasErrors: the check runs over the
            // whole project, which may legitimately have unrelated issues of its own.
            Assert.IsFalse(BuildManagerIntegrity.Check(settings).Issues
                    .Any(issue => issue.Message.Contains("under the same key")),
                "Two configs of one type are fine once one of them carries its own key.");
        }

        [Test]
        public void Integrity_FlagsAnEmptySlot()
        {
            var settings = CreateSettings();
            settings.EnvironmentsMutable.Add(CreateEnvironment("dev", CreateConfig<TuningConfig>(), null));

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.Issues.Any(issue => issue.Message.Contains("empty slot")));
        }

        [Test]
        public void Integrity_FlagsAConfigOneEnvironmentPublishesAndAnotherDoesNot()
        {
            // The "works in dev, null in prod" bug, now for typed configs.
            var settings = CreateSettings();
            settings.EnvironmentsMutable.Add(CreateEnvironment("dev", CreateConfig<TuningConfig>()));
            settings.EnvironmentsMutable.Add(CreateEnvironment("prod"));

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.Issues.Any(issue => issue.Message.Contains(nameof(TuningConfig))
                                                     && issue.Message.Contains("'prod'")));
        }

        [Test]
        public void Editor_DoesNotOfferTypesNoAssetCanBeCreatedFrom()
        {
            // These fixtures are nested types, so Unity can find no MonoScript for them and an asset
            // made from one loads with a null script. TypeCache reports them all the same.
            Assert.IsFalse(EnvironmentConfigCatalog.IsCreatable(typeof(TuningConfig)));
            Assert.IsFalse(EnvironmentConfigCatalog.IsCreatable(typeof(EnvironmentConfig)));
            CollectionAssert.DoesNotContain(EnvironmentConfigCatalog.ConfigTypes, typeof(EndpointsConfig));
        }

        // ---------------------------------------------------------------- fixtures

        private class TuningConfig : EnvironmentConfig
        {
            public float difficulty = 1f;
        }

        private sealed class DerivedTuningConfig : TuningConfig
        {
        }

        private sealed class EndpointsConfig : EnvironmentConfig
        {
            public string baseUrl = "https://example.test";
        }

        private T CreateConfig<T>(string key = null) where T : EnvironmentConfig
        {
            var config = ScriptableObject.CreateInstance<T>();
            config.hideFlags = HideFlags.HideAndDontSave;
            config.name = typeof(T).Name;
            m_Created.Add(config);

            if (key == null)
                return config;

            var serialized = new SerializedObject(config);
            serialized.FindProperty("m_ConfigKey").stringValue = key;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return config;
        }

        private BuildManagerSettings CreateSettings(params (string Key, Object Asset)[] defaults)
        {
            var settings = ScriptableObject.CreateInstance<BuildManagerSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            m_Created.Add(settings);

            foreach (var (key, asset) in defaults)
                settings.DefaultConfigAssetsMutable.Add(new EnvironmentAssetEntry(key, asset));

            return settings;
        }

        private BuildEnvironment CreateEnvironment(string id, params EnvironmentConfig[] configs)
        {
            var environment = ScriptableObject.CreateInstance<BuildEnvironment>();
            environment.hideFlags = HideFlags.HideAndDontSave;
            environment.name = "Env_" + id;
            m_Created.Add(environment);

            var serialized = new SerializedObject(environment);
            serialized.FindProperty("m_Id").stringValue = id;

            var list = serialized.FindProperty("m_Configs");
            list.arraySize = configs.Length;

            for (var i = 0; i < configs.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = configs[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return environment;
        }

        private static void AddKeyedEntry(BuildEnvironment environment, string key, Object asset)
        {
            var serialized = new SerializedObject(environment);
            var list = serialized.FindProperty("m_ConfigAssets");

            list.arraySize += 1;

            var element = list.GetArrayElementAtIndex(list.arraySize - 1);
            element.FindPropertyRelative("key").stringValue = key;
            element.FindPropertyRelative("asset").objectReferenceValue = asset;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private EnvironmentAssets CreateRuntimeAssets(params EnvironmentConfig[] configs)
        {
            var assets = ScriptableObject.CreateInstance<EnvironmentAssets>();
            assets.hideFlags = HideFlags.HideAndDontSave;
            m_Created.Add(assets);

            foreach (var config in configs)
                assets.m_Entries.Add(new EnvironmentAssetEntry(config.ConfigKey, config));

            assets.InvalidateLookup();
            return assets;
        }

        private TextAsset CreateTextAsset(string contents)
        {
            var asset = new TextAsset(contents) { hideFlags = HideFlags.HideAndDontSave };
            m_Created.Add(asset);
            return asset;
        }
    }
}
