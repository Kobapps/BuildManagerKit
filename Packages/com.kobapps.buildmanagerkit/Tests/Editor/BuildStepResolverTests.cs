using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    [TestFixture]
    internal sealed class BuildStepResolverTests
    {
        /// <summary>Minimal concrete step; the resolver only reads <see cref="BuildStep.Key"/>.</summary>
        [Serializable]
        internal sealed class NamedStep : BuildStep
        {
            [SerializeField] private string m_Name = string.Empty;

            internal string Name => m_Name;

            internal NamedStep(string name, string key)
            {
                m_Name = name;
                SetKey(key);
            }

            /// <summary>Required for the registry; never used by these tests.</summary>
            public NamedStep()
            {
            }

            public override void Execute(BuildContext context)
            {
            }

            public override string ToString() => m_Name;
        }

        private static ScopedBuildStep Global(string name, string key = "") =>
            new ScopedBuildStep(new NamedStep(name, key), BuildStepScopeLevel.Global);

        private static ScopedBuildStep Environment(string name, string key = "") =>
            new ScopedBuildStep(new NamedStep(name, key), BuildStepScopeLevel.Environment);

        private static ScopedBuildStep Profile(string name, string key = "") =>
            new ScopedBuildStep(new NamedStep(name, key), BuildStepScopeLevel.Profile);

        private static string[] Names(IEnumerable<BuildStep> steps) =>
            steps.Cast<NamedStep>().Select(step => step.Name).ToArray();

        [Test]
        public void Resolve_KeepsEverythingWhenNoKeysAreUsed()
        {
            var result = BuildStepResolver.Resolve(new[]
            {
                Global("g1"), Environment("e1"), Profile("p1")
            });

            CollectionAssert.AreEqual(new[] { "g1", "e1", "p1" }, Names(result));
        }

        [Test]
        public void Resolve_ProfileOverridesEnvironmentAndGlobal()
        {
            var result = BuildStepResolver.Resolve(new[]
            {
                Global("g-notify", "notify"),
                Environment("e-notify", "notify"),
                Profile("p-notify", "notify")
            });

            CollectionAssert.AreEqual(new[] { "p-notify" }, Names(result));
        }

        [Test]
        public void Resolve_EnvironmentOverridesGlobalWhenNoProfileCompetes()
        {
            var result = BuildStepResolver.Resolve(new[]
            {
                Global("g-notify", "notify"),
                Environment("e-notify", "notify")
            });

            CollectionAssert.AreEqual(new[] { "e-notify" }, Names(result));
        }

        [Test]
        public void Resolve_KeepsTheWinnerAtItsOwnPosition()
        {
            // The winner sits last in execution order; the suppressed global must not leave a gap
            // that reorders the untouched actions around it.
            var result = BuildStepResolver.Resolve(new[]
            {
                Global("g-first"),
                Global("g-notify", "notify"),
                Environment("e-middle"),
                Profile("p-notify", "notify"),
                Profile("p-last")
            });

            CollectionAssert.AreEqual(new[] { "g-first", "e-middle", "p-notify", "p-last" }, Names(result));
        }

        [Test]
        public void Resolve_TreatsDifferentKeysIndependently()
        {
            var result = BuildStepResolver.Resolve(new[]
            {
                Global("g-notify", "notify"),
                Global("g-upload", "upload"),
                Profile("p-notify", "notify")
            });

            CollectionAssert.AreEqual(new[] { "g-upload", "p-notify" }, Names(result));
        }

        [Test]
        public void Resolve_KeepsOnlyTheFirstOfDuplicateKeysInTheSameList()
        {
            var result = BuildStepResolver.Resolve(new[]
            {
                Profile("p-a", "dup"),
                Profile("p-b", "dup")
            });

            CollectionAssert.AreEqual(new[] { "p-a" }, Names(result));
        }

        [Test]
        public void Resolve_MatchesKeysCaseInsensitively()
        {
            var result = BuildStepResolver.Resolve(new[]
            {
                Global("g", "Notify"),
                Profile("p", "notify")
            });

            CollectionAssert.AreEqual(new[] { "p" }, Names(result));
        }

        [Test]
        public void Resolve_IgnoresNullEntries()
        {
            var result = BuildStepResolver.Resolve(new[]
            {
                new ScopedBuildStep(null, BuildStepScopeLevel.Global),
                Profile("p")
            });

            CollectionAssert.AreEqual(new[] { "p" }, Names(result));
        }

        [Test]
        public void GetSuppressed_ReportsWhatWasReplaced()
        {
            var candidates = new[]
            {
                Global("g-notify", "notify"),
                Environment("e-notify", "notify"),
                Profile("p-notify", "notify"),
                Profile("p-plain")
            };

            var suppressed = BuildStepResolver.GetSuppressed(candidates);

            CollectionAssert.AreEquivalent(
                new[] { "g-notify", "e-notify" },
                suppressed.Select(entry => ((NamedStep)entry.Step).Name).ToArray());
        }

        [Test]
        public void Tag_MarksEveryStepWithTheGivenLevel()
        {
            var tagged = BuildStepResolver
                .Tag(new BuildStep[] { new NamedStep("a", string.Empty), null, new NamedStep("b", string.Empty) },
                    BuildStepScopeLevel.Environment)
                .ToArray();

            Assert.AreEqual(2, tagged.Length, "Null entries should be dropped.");
            Assert.IsTrue(tagged.All(entry => entry.Level == BuildStepScopeLevel.Environment));
        }

        [Test]
        public void Key_TrimsAndNormalisesEmptyValues()
        {
            Assert.AreEqual("notify", new NamedStep("a", "  notify  ").Key);
            Assert.AreEqual(string.Empty, new NamedStep("a", "   ").Key);
            Assert.AreEqual(string.Empty, new NamedStep("a", null).Key);
        }
    }
}
