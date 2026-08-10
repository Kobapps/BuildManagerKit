using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    /// <summary>
    /// The application icon override: which slots it reaches, and that the project's own icons come
    /// back afterwards.
    ///
    /// This is the one player setting whose mistake is invisible in the Editor. Android draws the
    /// adaptive icon on API 26 and up and ignores the legacy one, so an override that writes only the
    /// legacy slots changes what the Inspector shows and not what the phone shows — the tests below
    /// assert every kind, every size and every required layer instead of taking "an icon was applied"
    /// for an answer.
    ///
    /// The tests write real player settings, so each one captures the target's icons first and puts
    /// them back in teardown.
    /// </summary>
    [TestFixture]
    internal sealed class ApplicationIconTests
    {
        private const string k_IconPath = "Assets/BuildManagerKit_TestIcon.png";

        private readonly List<KeyValuePair<NamedBuildTarget, IconSnapshot>> m_Restore =
            new List<KeyValuePair<NamedBuildTarget, IconSnapshot>>();

        private Texture2D m_Icon;

        [SetUp]
        public void SetUp()
        {
            // An imported asset rather than a `new Texture2D`: the snapshot stores GUIDs, so an
            // in-memory texture could never be restored and the round-trip test would prove nothing.
            var texture = new Texture2D(64, 64);
            var pixels = new Color[64 * 64];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = Color.magenta;

            texture.SetPixels(pixels);
            texture.Apply();

            File.WriteAllBytes(k_IconPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(k_IconPath, ImportAssetOptions.ForceSynchronousImport);
            m_Icon = AssetDatabase.LoadAssetAtPath<Texture2D>(k_IconPath);
            Assert.IsNotNull(m_Icon, "The test icon did not import as a Texture2D.");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var pair in m_Restore)
                ApplicationIconService.Restore(pair.Key, pair.Value);

            m_Restore.Clear();
            m_Icon = null;

            AssetDatabase.DeleteAsset(k_IconPath);
        }

        // ---------------------------------------------------------------- platform icons

        [Test]
        public void EveryPlatformIconSlotAndRequiredLayerIsWritten()
        {
            foreach (var target in TargetsWithPlatformIcons())
            {
                Guard(target);
                Assert.IsTrue(ApplicationIconService.Apply(target, m_Icon), $"Nothing was applied to {target.TargetName}.");

                var written = 0;

                foreach (var kind in PlayerSettings.GetSupportedIconKinds(target).Where(IsLauncherKind))
                {
                    foreach (var slot in PlayerSettings.GetPlatformIcons(target, kind))
                    {
                        var textures = slot.GetTextures();
                        var required = Mathf.Clamp(slot.minLayerCount, 1, Math.Max(slot.maxLayerCount, 1));

                        Assert.GreaterOrEqual(textures.Length, required,
                            $"{target.TargetName} {kind} {slot.width}×{slot.height} has {textures.Length} layer(s), "
                            + $"needs {required}.");

                        for (var layer = 0; layer < required; layer++)
                        {
                            Assert.AreSame(m_Icon, textures[layer],
                                $"{target.TargetName} {kind} {slot.width}×{slot.height} layer {layer} was not written.");
                            written++;
                        }
                    }
                }

                Assert.Greater(written, 0, $"{target.TargetName} reported icon kinds but no slots.");
            }
        }

        [Test]
        public void AdaptiveIconsGetBothLayers()
        {
            // The regression this whole file exists for: an adaptive icon missing its background
            // layer makes the launcher fall back to the legacy icon, which is the project's own.
            var verified = 0;

            foreach (var target in TargetsWithPlatformIcons())
            {
                var adaptive = PlayerSettings.GetSupportedIconKinds(target).FirstOrDefault(
                    kind => kind.ToString().IndexOf("Adaptive", StringComparison.OrdinalIgnoreCase) >= 0);

                if (adaptive == null)
                    continue;

                Guard(target);
                ApplicationIconService.Apply(target, m_Icon);

                foreach (var slot in PlayerSettings.GetPlatformIcons(target, adaptive))
                {
                    Assert.AreEqual(2, slot.minLayerCount, "An adaptive icon is a background and a foreground.");
                    var textures = slot.GetTextures();

                    Assert.AreSame(m_Icon, textures[0], $"Adaptive {slot.width} background was not written.");
                    Assert.AreSame(m_Icon, textures[1], $"Adaptive {slot.width} foreground was not written.");
                    verified++;
                }
            }

            if (verified == 0)
                Assert.Ignore("No installed platform has adaptive icons.");
        }

        [Test]
        public void NotificationAndSettingsIconsAreLeftAlone()
        {
            var verified = 0;

            foreach (var target in TargetsWithPlatformIcons())
            {
                var kinds = PlayerSettings.GetSupportedIconKinds(target)
                    .Where(kind => !IsLauncherKind(kind))
                    .ToArray();

                if (kinds.Length == 0)
                    continue;

                var before = kinds.Select(kind => Describe(target, kind)).ToArray();

                Guard(target);
                ApplicationIconService.Apply(target, m_Icon);

                for (var i = 0; i < kinds.Length; i++)
                {
                    Assert.AreEqual(before[i], Describe(target, kinds[i]),
                        $"The {target.TargetName} {kinds[i]} icons were overwritten.");
                    verified++;
                }
            }

            if (verified == 0)
                Assert.Ignore("No installed platform has notification or settings icons.");
        }

        // ---------------------------------------------------------------- legacy icons

        [Test]
        public void EveryLegacyIconSizeIsWrittenForAPlatformWithoutPlatformIcons()
        {
            // Standalone has no platform icons, so the legacy path is the one that ships its icons.
            var target = NamedBuildTarget.Standalone;
            if (PlayerSettings.GetSupportedIconKinds(target).Length > 0)
                Assert.Ignore("Standalone reports platform icons in this Editor version.");

            Guard(target);
            Assert.IsTrue(ApplicationIconService.Apply(target, m_Icon));

            var sizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
            var icons = PlayerSettings.GetIcons(target, IconKind.Application);

            Assert.AreEqual(sizes.Length, icons.Length, "One icon per size, or Unity drops the rest.");
            Assert.IsTrue(icons.All(icon => icon == m_Icon), "A size was left with the project's icon.");
        }

        // ---------------------------------------------------------------- capture and restore

        [Test]
        public void RestorePutsTheProjectIconsBack()
        {
            foreach (var target in TargetsWithPlatformIcons())
            {
                var before = Describe(target);

                var snapshot = ApplicationIconService.Capture(target);
                ApplicationIconService.Apply(target, m_Icon);

                Assert.AreNotEqual(before, Describe(target),
                    $"{target.TargetName} did not change, so the test proves nothing.");

                ApplicationIconService.Restore(target, snapshot);

                Assert.AreEqual(before, Describe(target),
                    $"The project's own {target.TargetName} icons did not come back.");
            }
        }

        [Test]
        public void ASnapshotSurvivesSerialisation()
        {
            // The runner persists the snapshot across a domain reload, so a restore that only works
            // in memory would put nothing back after a script recompile mid-build.
            foreach (var target in TargetsWithPlatformIcons())
            {
                Guard(target);
                ApplicationIconService.Apply(target, m_Icon);

                var snapshot = ApplicationIconService.Capture(target);
                var restored = JsonUtility.FromJson<IconSnapshot>(JsonUtility.ToJson(snapshot));

                Assert.IsTrue(restored.HasData);
                Assert.AreEqual(snapshot.platformKinds.Count, restored.platformKinds.Count);
                Assert.Greater(snapshot.platformKinds.Count, 0, $"{target.TargetName} captured no platform kinds.");

                for (var i = 0; i < snapshot.platformKinds.Count; i++)
                {
                    var captured = snapshot.platformKinds[i];
                    var roundTripped = restored.platformKinds[i];

                    Assert.AreEqual(captured.index, roundTripped.index);
                    Assert.AreEqual(captured.kind, roundTripped.kind);
                    Assert.AreEqual(captured.icons.Count, roundTripped.icons.Count);

                    for (var slot = 0; slot < captured.icons.Count; slot++)
                    {
                        Assert.AreEqual(captured.icons[slot].guids, roundTripped.icons[slot].guids,
                            $"{captured.kind} slot {slot} lost its layers.");

                        Assert.IsTrue(roundTripped.icons[slot].guids.All(guid => !string.IsNullOrEmpty(guid)),
                            $"{captured.kind} slot {slot} did not capture the applied icon as an asset GUID.");
                    }
                }
            }
        }

        [Test]
        public void PlatformIconKindsHaveTheNamesTheExclusionReliesOn()
        {
            // The notification and settings kinds are recognised by name, so a kind whose ToString
            // stopped returning the name would quietly hand a launcher icon to a status bar.
            foreach (var target in TargetsWithPlatformIcons())
            {
                foreach (var kind in PlayerSettings.GetSupportedIconKinds(target))
                {
                    var name = kind.ToString();
                    Assert.IsNotEmpty(name);
                    Assert.IsFalse(name.Contains("."), $"'{name}' looks like a type name rather than a kind name.");
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Every installed platform that has platform icons, so the tests run against the API the
        /// shipped app reads. A fixture that finds none ignores itself rather than passing on an
        /// Editor with no mobile module installed.
        /// </summary>
        private static IEnumerable<NamedBuildTarget> TargetsWithPlatformIcons()
        {
            var candidates = new[]
            {
                NamedBuildTarget.Android,
                NamedBuildTarget.iOS,
                NamedBuildTarget.tvOS,
                NamedBuildTarget.FromBuildTargetGroup(
                    BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget))
            };

            var found = new List<NamedBuildTarget>();

            foreach (var candidate in candidates)
            {
                if (found.Contains(candidate))
                    continue;

                try
                {
                    if (PlayerSettings.GetSupportedIconKinds(candidate).Length > 0)
                        found.Add(candidate);
                }
                catch (Exception)
                {
                    // A platform module that is not installed: try the next candidate.
                }
            }

            if (found.Count == 0)
                Assert.Ignore("No installed platform in this Editor exposes platform icons.");

            return found;
        }

        /// <summary>
        /// True for the kinds the override owns — everything except the notification and settings
        /// icons, which follow their own design rules.
        /// </summary>
        private static bool IsLauncherKind(PlatformIconKind kind)
        {
            var name = kind.ToString();

            return name.IndexOf("Notification", StringComparison.OrdinalIgnoreCase) < 0
                   && name.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>Remembers a target's icons so teardown can put them back.</summary>
        private void Guard(NamedBuildTarget target) =>
            m_Restore.Add(new KeyValuePair<NamedBuildTarget, IconSnapshot>(
                target, ApplicationIconService.Capture(target)));

        /// <summary>Every icon of a target as text, which is what two states are compared by.</summary>
        private static string Describe(NamedBuildTarget target)
        {
            var parts = PlayerSettings.GetSupportedIconKinds(target)
                .Select(kind => Describe(target, kind))
                .ToList();

            foreach (var kind in new[] { IconKind.Any, IconKind.Application })
            {
                var textures = (PlayerSettings.GetIcons(target, kind) ?? Array.Empty<Texture2D>())
                    .Select(texture => texture != null ? texture.name : "-");

                parts.Add($"legacy {kind}:{string.Join(",", textures)}");
            }

            return string.Join("|", parts);
        }

        /// <summary>One platform icon kind as text — every slot, every layer.</summary>
        private static string Describe(NamedBuildTarget target, PlatformIconKind kind)
        {
            var slots = PlayerSettings.GetPlatformIcons(target, kind)
                .Select(slot =>
                {
                    var textures = slot.GetTextures()
                        .Select(texture => texture != null ? texture.name : "-");

                    return $"{slot.width}x{slot.height}:{string.Join(",", textures)}";
                });

            return $"{kind}/{string.Join(";", slots)}";
        }
    }
}
