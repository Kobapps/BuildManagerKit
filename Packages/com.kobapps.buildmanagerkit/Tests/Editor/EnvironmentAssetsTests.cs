using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    /// <summary>
    /// Per-environment config assets: the merge of project-wide defaults with an environment's own
    /// entries, and the runtime lookups shipped code performs against the result.
    /// </summary>
    [TestFixture]
    internal sealed class EnvironmentAssetsTests
    {
        private readonly List<Object> m_Created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in m_Created)
                Object.DestroyImmediate(asset);

            m_Created.Clear();
        }

        // ---------------------------------------------------------------- resolution

        [Test]
        public void Resolve_PublishesTheEnvironmentsOwnEntries()
        {
            var settings = CreateSettings();
            var environment = CreateEnvironment("dev", ("gameConfig", CreateTextAsset("dev-config")));

            var resolved = EnvironmentAssetsWriter.Resolve(environment, settings);

            Assert.AreEqual(1, resolved.Count);
            Assert.AreEqual("gameConfig", resolved[0].key);
        }

        [Test]
        public void Resolve_InheritsProjectWideDefaults()
        {
            var shared = CreateTextAsset("shared");
            var settings = CreateSettings(("shared", shared));
            var environment = CreateEnvironment("dev");

            var resolved = EnvironmentAssetsWriter.Resolve(environment, settings);

            Assert.AreEqual(1, resolved.Count);
            Assert.AreSame(shared, resolved[0].asset);
        }

        [Test]
        public void Resolve_LetsTheEnvironmentOverrideADefault()
        {
            var fallback = CreateTextAsset("default-endpoints");
            var specific = CreateTextAsset("prod-endpoints");

            var settings = CreateSettings(("endpoints", fallback));
            var environment = CreateEnvironment("prod", ("endpoints", specific));

            var resolved = EnvironmentAssetsWriter.Resolve(environment, settings);

            Assert.AreEqual(1, resolved.Count, "The override must replace the default, not sit beside it.");
            Assert.AreSame(specific, resolved[0].asset);
        }

        [Test]
        public void Resolve_MatchesKeysCaseInsensitively()
        {
            var settings = CreateSettings(("Endpoints", CreateTextAsset("a")));
            var specific = CreateTextAsset("b");
            var environment = CreateEnvironment("prod", ("endpoints", specific));

            var resolved = EnvironmentAssetsWriter.Resolve(environment, settings);

            Assert.AreEqual(1, resolved.Count);
            Assert.AreSame(specific, resolved[0].asset);
        }

        [Test]
        public void Resolve_KeepsDefaultsFirstThenTheEnvironmentsAdditions()
        {
            var settings = CreateSettings(("a", CreateTextAsset("a")));
            var environment = CreateEnvironment("dev", ("b", CreateTextAsset("b")));

            CollectionAssert.AreEqual(
                new[] { "a", "b" },
                EnvironmentAssetsWriter.Resolve(environment, settings).Select(entry => entry.key).ToArray());
        }

        [Test]
        public void Resolve_DropsEntriesWithNoKeyOrNoAsset()
        {
            var settings = CreateSettings();
            var environment = CreateEnvironment("dev",
                ("", CreateTextAsset("orphan")),
                ("missing", null),
                ("good", CreateTextAsset("good")));

            var resolved = EnvironmentAssetsWriter.Resolve(environment, settings);

            CollectionAssert.AreEqual(new[] { "good" }, resolved.Select(entry => entry.key).ToArray());
        }

        [Test]
        public void Resolve_TrimsWhitespaceFromKeys()
        {
            var settings = CreateSettings();
            var environment = CreateEnvironment("dev", ("  spaced  ", CreateTextAsset("x")));

            Assert.AreEqual("spaced", EnvironmentAssetsWriter.Resolve(environment, settings)[0].key);
        }

        [Test]
        public void Resolve_HandlesANullEnvironment()
        {
            var settings = CreateSettings(("shared", CreateTextAsset("s")));

            // Clearing the environment should still publish the defaults rather than throw.
            Assert.AreEqual(1, EnvironmentAssetsWriter.Resolve(null, settings).Count);
        }

        // ---------------------------------------------------------------- runtime lookups

        [Test]
        public void RuntimeLookup_FindsAssetsByKeyIgnoringCase()
        {
            var text = CreateTextAsset("hello");
            var assets = CreateRuntimeAssets(("greeting", text));

            Assert.AreSame(text, assets.Get<TextAsset>("greeting"));
            Assert.AreSame(text, assets.Get<TextAsset>("GREETING"));
            Assert.IsTrue(assets.Has("greeting"));
        }

        [Test]
        public void RuntimeLookup_ReturnsNullForAbsentKeys()
        {
            var assets = CreateRuntimeAssets();

            Assert.IsNull(assets.Get<TextAsset>("nope"));
            Assert.IsFalse(assets.Has("nope"));
            Assert.IsFalse(assets.TryGet<TextAsset>("nope", out _));
        }

        [Test]
        public void RuntimeLookup_ToleratesNullAndEmptyKeys()
        {
            var assets = CreateRuntimeAssets(("a", CreateTextAsset("a")));

            Assert.IsNull(assets.Get<TextAsset>(null));
            Assert.IsNull(assets.Get<TextAsset>(string.Empty));
        }

        [Test]
        public void RuntimeLookup_GetTextReadsTheAssetContents()
        {
            var assets = CreateRuntimeAssets(("notes", CreateTextAsset("line one")));

            Assert.AreEqual("line one", assets.GetText("notes"));
            Assert.AreEqual("fallback", assets.GetText("absent", "fallback"));
        }

        [Test]
        public void RuntimeLookup_ParsesJson()
        {
            var assets = CreateRuntimeAssets(("endpoints", CreateTextAsset("{\"baseUrl\":\"https://prod.test\"}")));

            Assert.IsTrue(assets.TryGetJson<Endpoints>("endpoints", out var parsed));
            Assert.AreEqual("https://prod.test", parsed.baseUrl);
        }

        [Test]
        public void RuntimeLookup_ReportsMalformedJsonWithoutThrowing()
        {
            var assets = CreateRuntimeAssets(("broken", CreateTextAsset("{ this is not json")));

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.IsFalse(assets.TryGetJson<Endpoints>("broken", out _));
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void RuntimeLookup_TypeMismatchReturnsNullRatherThanCasting()
        {
            var assets = CreateRuntimeAssets(("texture", CreateTexture()));

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.IsNull(assets.Get<TextAsset>("texture"));
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }

            Assert.IsNotNull(assets.GetTexture("texture"));
        }

        [Test]
        public void RuntimeLookup_GetOrDefaultFallsBack()
        {
            var fallback = CreateTextAsset("fallback");
            var assets = CreateRuntimeAssets();

            Assert.AreSame(fallback, assets.GetOrDefault("absent", fallback));
        }

        [Test]
        public void RuntimeLookup_CurrentIsNeverNull()
        {
            // Shipped code calls this unguarded; an unbaked project must not blow up.
            Assert.IsNotNull(EnvironmentAssets.Current);
            Assert.IsNull(EnvironmentAssets.Current.Get<TextAsset>("anything-at-all"));
        }

        [Test]
        public void RuntimeLookup_KeysListsEveryPublishedKey()
        {
            var assets = CreateRuntimeAssets(("a", CreateTextAsset("a")), ("b", CreateTextAsset("b")));

            CollectionAssert.AreEquivalent(new[] { "a", "b" }, assets.Keys.ToArray());
        }

        // ---------------------------------------------------------------- integrity

        [Test]
        public void Integrity_FlagsAKeyMissingFromOneEnvironment()
        {
            // The "works in dev, null in prod" bug.
            var settings = CreateSettings();
            var dev = CreateEnvironment("dev", ("debugPanel", CreateTextAsset("panel")));
            var prod = CreateEnvironment("prod");

            settings.EnvironmentsMutable.Add(dev);
            settings.EnvironmentsMutable.Add(prod);

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.Issues.Any(issue => issue.Message.Contains("debugPanel")
                                                     && issue.Message.Contains("'prod'")));
        }

        [Test]
        public void Integrity_DoesNotFlagKeysEveryEnvironmentPublishes()
        {
            var settings = CreateSettings(("shared", CreateTextAsset("s")));
            settings.EnvironmentsMutable.Add(CreateEnvironment("dev"));
            settings.EnvironmentsMutable.Add(CreateEnvironment("prod"));

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsFalse(report.Issues.Any(issue => issue.Message.Contains("shared")),
                "A key inherited by every environment is consistent and must not warn.");
        }

        [Test]
        public void Integrity_FlagsDuplicateKeysInOneEnvironment()
        {
            var settings = CreateSettings();
            settings.EnvironmentsMutable.Add(CreateEnvironment("dev",
                ("config", CreateTextAsset("a")),
                ("config", CreateTextAsset("b"))));

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Issues.Any(issue => issue.Message.Contains("twice")));
        }

        [Test]
        public void Integrity_FlagsAnEntryWithNoKey()
        {
            var settings = CreateSettings();
            settings.EnvironmentsMutable.Add(CreateEnvironment("dev", ("", CreateTextAsset("a"))));

            Assert.IsTrue(BuildManagerIntegrity.Check(settings).HasErrors);
        }

        // ---------------------------------------------------------------- helpers

        [System.Serializable]
        private sealed class Endpoints
        {
            public string baseUrl;
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

        private BuildEnvironment CreateEnvironment(string id, params (string Key, Object Asset)[] assets)
        {
            var environment = ScriptableObject.CreateInstance<BuildEnvironment>();
            environment.hideFlags = HideFlags.HideAndDontSave;
            environment.name = "Env_" + id;
            m_Created.Add(environment);

            var serialized = new SerializedObject(environment);
            serialized.FindProperty("m_Id").stringValue = id;

            var list = serialized.FindProperty("m_ConfigAssets");
            list.arraySize = assets.Length;

            for (var i = 0; i < assets.Length; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("key").stringValue = assets[i].Key;
                element.FindPropertyRelative("asset").objectReferenceValue = assets[i].Asset;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return environment;
        }

        private EnvironmentAssets CreateRuntimeAssets(params (string Key, Object Asset)[] entries)
        {
            var assets = ScriptableObject.CreateInstance<EnvironmentAssets>();
            assets.hideFlags = HideFlags.HideAndDontSave;
            m_Created.Add(assets);

            foreach (var (key, asset) in entries)
                assets.m_Entries.Add(new EnvironmentAssetEntry(key, asset));

            assets.InvalidateLookup();
            return assets;
        }

        private TextAsset CreateTextAsset(string contents)
        {
            var asset = new TextAsset(contents) { hideFlags = HideFlags.HideAndDontSave };
            m_Created.Add(asset);
            return asset;
        }

        private Texture2D CreateTexture()
        {
            var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            m_Created.Add(texture);
            return texture;
        }
    }
}
