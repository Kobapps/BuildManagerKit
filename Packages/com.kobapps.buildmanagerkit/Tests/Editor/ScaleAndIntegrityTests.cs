using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    /// <summary>
    /// Bounds on everything that grows with the size of a build.
    ///
    /// A shell step that walks a large project can emit hundreds of thousands of lines, and a
    /// build can produce thousands of artifacts. Without ceilings the Editor's memory grows with
    /// them and the JSON result becomes too large for CI to parse.
    /// </summary>
    [TestFixture]
    internal sealed class BuildLogBoundsTests
    {
        private BuildLog m_Log;

        [SetUp]
        public void SetUp() => m_Log = new BuildLog { MirrorToConsole = false };

        [Test]
        public void Entries_StopGrowingAtTheCap()
        {
            for (var i = 0; i < BuildLog.MaxEntries + 5000; i++)
                m_Log.Info("line " + i);

            Assert.LessOrEqual(m_Log.Entries.Count, BuildLog.MaxEntries,
                "The in-memory log must not grow without bound.");
            Assert.Greater(m_Log.DroppedEntryCount, 0, "Dropped lines should be counted.");
        }

        [Test]
        public void TheTailIsKept_BecauseThatIsWhereFailuresAre()
        {
            for (var i = 0; i < BuildLog.MaxEntries + 100; i++)
                m_Log.Info("line " + i);

            m_Log.Error("the actual failure");

            Assert.AreEqual("the actual failure", m_Log.Entries[m_Log.Entries.Count - 1].message);
        }

        [Test]
        public void CountersSurviveTrimming()
        {
            m_Log.Warning("w");
            m_Log.Error("e");

            for (var i = 0; i < BuildLog.MaxEntries + 3000; i++)
                m_Log.Info("noise " + i);

            // The lines are gone but the totals must still describe the whole run.
            Assert.AreEqual(1, m_Log.WarningCount);
            Assert.AreEqual(1, m_Log.ErrorCount);
        }

        [Test]
        public void PlainText_SaysHowMuchWasDropped()
        {
            for (var i = 0; i < BuildLog.MaxEntries + 2500; i++)
                m_Log.Info("line " + i);

            StringAssert.Contains("dropped", m_Log.ToPlainText());
        }

        [Test]
        public void BoundedPlainText_RespectsTheCeilingAndKeepsTheTail()
        {
            for (var i = 0; i < 5000; i++)
                m_Log.Info("some reasonably long log line number " + i);

            m_Log.Error("LAST-LINE-MARKER");

            const int cap = 4096;
            var text = m_Log.ToPlainText(cap);

            Assert.LessOrEqual(text.Length, cap + 200, "The notice may add a line, nothing more.");
            StringAssert.Contains("LAST-LINE-MARKER", text);
            StringAssert.Contains("omitted", text);
        }

        [Test]
        public void BoundedPlainText_ReturnsEverythingWhenUnderTheCeiling()
        {
            m_Log.Info("short");
            Assert.AreEqual(m_Log.ToPlainText(), m_Log.ToPlainText(100000));
        }

        [Test]
        public void Appending_StaysLinear()
        {
            // Trimming one entry at a time would make this quadratic and take minutes.
            var stopwatch = Stopwatch.StartNew();

            for (var i = 0; i < BuildLog.MaxEntries * 3; i++)
                m_Log.Info("line " + i);

            stopwatch.Stop();

            Assert.Less(stopwatch.ElapsedMilliseconds, 10000,
                $"Writing {BuildLog.MaxEntries * 3} lines took {stopwatch.ElapsedMilliseconds} ms.");
        }
    }

    [TestFixture]
    internal sealed class BuildContextScaleTests
    {
        private BuildContext m_Context;

        [SetUp]
        public void SetUp() => m_Context = new BuildContext(new BuildLog { MirrorToConsole = false });

        [Test]
        public void Artifacts_AreDeduplicated()
        {
            m_Context.AddArtifact("/tmp/a");
            m_Context.AddArtifact("/tmp/a");
            m_Context.AddArtifact("/tmp/b");

            Assert.AreEqual(2, m_Context.Artifacts.Count);
        }

        [Test]
        public void Artifacts_IgnoreEmptyPathsAndNormaliseSeparators()
        {
            m_Context.AddArtifact(null);
            m_Context.AddArtifact(string.Empty);
            m_Context.AddArtifact("/tmp/a/");
            m_Context.AddArtifact("\\tmp\\a");

            Assert.AreEqual(1, m_Context.Artifacts.Count);
        }

        [Test]
        public void Artifacts_PreserveInsertionOrder()
        {
            m_Context.AddArtifact("/tmp/z");
            m_Context.AddArtifact("/tmp/a");

            CollectionAssert.AreEqual(new[] { "/tmp/z", "/tmp/a" }, m_Context.Artifacts);
        }

        [Test]
        public void ManyArtifacts_StayLinear()
        {
            // A step registering one artifact per copied file used to be O(n²) here.
            var stopwatch = Stopwatch.StartNew();

            for (var i = 0; i < 50000; i++)
                m_Context.AddArtifact("/tmp/file" + i);

            stopwatch.Stop();

            Assert.AreEqual(50000, m_Context.Artifacts.Count);
            Assert.Less(stopwatch.ElapsedMilliseconds, 5000,
                $"Registering 50k artifacts took {stopwatch.ElapsedMilliseconds} ms.");
        }

        [Test]
        public void TokenResolution_StaysFastOnLongTemplates()
        {
            m_Context.SetVariable("api_url", "https://example.test");
            m_Context.RefreshTokens();

            var template = string.Concat(Enumerable.Repeat("{version}/{env}/{api_url}/", 2000));

            var stopwatch = Stopwatch.StartNew();
            var resolved = m_Context.Resolve(template);
            stopwatch.Stop();

            StringAssert.DoesNotContain("{version}", resolved);
            Assert.Less(stopwatch.ElapsedMilliseconds, 2000);
        }
    }

    /// <summary>
    /// Project-wide consistency. Every case here is something that works fine with one profile and
    /// one environment, and silently does the wrong thing once a team has several.
    /// </summary>
    [TestFixture]
    internal sealed class BuildManagerIntegrityTests
    {
        private readonly List<UnityEngine.Object> m_Created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in m_Created)
                UnityEngine.Object.DestroyImmediate(asset);

            m_Created.Clear();
        }

        [Test]
        public void CleanSetup_ReportsNoErrors()
        {
            var settings = CreateSettings();
            AddProfile(settings, "android", BuildTarget.Android);
            AddProfile(settings, "ios", BuildTarget.iOS);
            AddEnvironment(settings, "dev");
            AddEnvironment(settings, "prod");

            AssertNoConfigurationErrors(BuildManagerIntegrity.Check(settings));
        }

        [Test]
        public void DuplicateProfileIds_AreAnError()
        {
            var settings = CreateSettings();
            AddProfile(settings, "android", BuildTarget.Android);
            AddProfile(settings, "android", BuildTarget.iOS);

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Issues.Any(i => i.Message.Contains("share the id 'android'")));
        }

        [Test]
        public void DuplicateProfileIds_AreDetectedRegardlessOfCase()
        {
            var settings = CreateSettings();
            AddProfile(settings, "Android", BuildTarget.Android);
            AddProfile(settings, "android", BuildTarget.iOS);

            Assert.IsTrue(BuildManagerIntegrity.Check(settings).HasErrors);
        }

        [Test]
        public void DuplicateEnvironmentIds_AreAnError()
        {
            var settings = CreateSettings();
            AddEnvironment(settings, "prod");
            AddEnvironment(settings, "prod");

            Assert.IsTrue(BuildManagerIntegrity.Check(settings).HasErrors);
        }

        [Test]
        public void EnvironmentsWhoseGeneratedDefinesCollide_AreAnError()
        {
            // "my env" and "my-env" both sanitise to ENV_MY_ENV, so #if cannot tell them apart.
            var settings = CreateSettings();
            AddEnvironment(settings, "my env");
            AddEnvironment(settings, "my-env");

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Issues.Any(i => i.Message.Contains("generate the define")));
        }

        [Test]
        public void ProfilesSharingAnUndiscriminatedOutputTemplate_AreAnError()
        {
            var settings = CreateSettings();
            AddProfile(settings, "a", BuildTarget.Android, "{projectRoot}/Builds/{version}");
            AddProfile(settings, "b", BuildTarget.iOS, "{projectRoot}/Builds/{version}");

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Issues.Any(i => i.Message.Contains("overwrite each other")));
        }

        [Test]
        public void ProfilesSharingATemplateWithATargetToken_AreFine()
        {
            var settings = CreateSettings();
            AddProfile(settings, "a", BuildTarget.Android, "{projectRoot}/Builds/{target}");
            AddProfile(settings, "b", BuildTarget.iOS, "{projectRoot}/Builds/{target}");

            AssertNoConfigurationErrors(BuildManagerIntegrity.Check(settings));
        }

        [Test]
        public void ADefaultEnvironmentTheProfileDisallows_IsAnError()
        {
            var settings = CreateSettings();
            var allowed = AddEnvironment(settings, "dev");
            var other = AddEnvironment(settings, "prod");
            var profile = AddProfile(settings, "android", BuildTarget.Android);

            var serialized = new SerializedObject(profile);
            var list = serialized.FindProperty("m_AllowedEnvironments");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = allowed;
            serialized.FindProperty("m_DefaultEnvironment").objectReferenceValue = other;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Issues.Any(i => i.Message.Contains("does not allow it")));
        }

        [Test]
        public void ALogFolderInsideAssets_IsAnError()
        {
            var settings = CreateSettings();

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("m_LogFolder").stringValue = "Assets/BuildLogs";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Issues.Any(i => i.Message.Contains("inside Assets")));
        }

        [Test]
        public void NullEntries_AreReportedAsWarningsNotCrashes()
        {
            var settings = CreateSettings();
            AddProfile(settings, "android", BuildTarget.Android);
            settings.ProfilesMutable.Add(null);
            settings.EnvironmentsMutable.Add(null);

            var report = BuildManagerIntegrity.Check(settings);

            AssertNoConfigurationErrors(report);
            Assert.IsTrue(report.HasWarnings);
        }

        [Test]
        public void AnEmptyQueue_IsAWarning()
        {
            var settings = CreateSettings();
            settings.QueuesMutable.Add(new BuildQueue { id = "empty", displayName = "Empty" });

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.HasWarnings);
        }

        [Test]
        public void Check_ScalesToALargeCatalogue()
        {
            var settings = CreateSettings();

            for (var i = 0; i < 200; i++)
                AddProfile(settings, "profile" + i, BuildTarget.Android, "{projectRoot}/Builds/{profile}");

            for (var i = 0; i < 40; i++)
                AddEnvironment(settings, "env" + i);

            var stopwatch = Stopwatch.StartNew();
            var report = BuildManagerIntegrity.Check(settings);
            stopwatch.Stop();

            AssertNoConfigurationErrors(report);
            Assert.Less(stopwatch.ElapsedMilliseconds, 5000,
                $"Checking 200 profiles took {stopwatch.ElapsedMilliseconds} ms.");
        }

        [Test]
        public void EnvironmentOrder_FollowsTheListNotTheName()
        {
            // Display order is the list order, so the switchers show what the user arranged rather
            // than an alphabetical guess.
            var settings = CreateSettings();
            AddEnvironment(settings, "zulu");
            AddEnvironment(settings, "alpha");
            AddEnvironment(settings, "mike");

            CollectionAssert.AreEqual(
                new[] { "zulu", "alpha", "mike" },
                settings.GetSortedEnvironments().Select(environment => environment.Id).ToArray());
        }

        [Test]
        public void MoveEnvironment_ReordersTheList()
        {
            var settings = CreateSettings();
            AddEnvironment(settings, "dev");
            AddEnvironment(settings, "stage");
            AddEnvironment(settings, "prod");

            Assert.IsTrue(settings.MoveEnvironment(2, 0), "Moving prod to the front should succeed.");

            CollectionAssert.AreEqual(
                new[] { "prod", "dev", "stage" },
                settings.GetSortedEnvironments().Select(environment => environment.Id).ToArray());
        }

        [Test]
        public void MoveEnvironment_HandlesMovingDownwards()
        {
            var settings = CreateSettings();
            AddEnvironment(settings, "dev");
            AddEnvironment(settings, "stage");
            AddEnvironment(settings, "prod");

            Assert.IsTrue(settings.MoveEnvironment(0, 2));

            CollectionAssert.AreEqual(
                new[] { "stage", "prod", "dev" },
                settings.GetSortedEnvironments().Select(environment => environment.Id).ToArray());
        }

        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        [TestCase(0, 5)]
        [TestCase(5, 0)]
        [TestCase(1, 1)]
        public void MoveEnvironment_RejectsOutOfRangeAndNoOpMoves(int from, int to)
        {
            var settings = CreateSettings();
            AddEnvironment(settings, "dev");
            AddEnvironment(settings, "stage");

            Assert.IsFalse(settings.MoveEnvironment(from, to));

            CollectionAssert.AreEqual(
                new[] { "dev", "stage" },
                settings.GetSortedEnvironments().Select(environment => environment.Id).ToArray());
        }

        [Test]
        public void MoveEnvironment_PreservesEveryEntry()
        {
            var settings = CreateSettings();
            for (var i = 0; i < 10; i++)
                AddEnvironment(settings, "env" + i);

            settings.MoveEnvironment(7, 1);
            settings.MoveEnvironment(0, 9);
            settings.MoveEnvironment(4, 4);

            var ids = settings.GetSortedEnvironments().Select(environment => environment.Id).ToArray();

            Assert.AreEqual(10, ids.Length, "Reordering must never drop or duplicate an entry.");
            CollectionAssert.AllItemsAreUnique(ids);
        }

        [Test]
        public void EnvironmentOrder_SurvivesNullSlots()
        {
            // A deleted asset leaves a null in the list; ordering must skip it, not throw.
            var settings = CreateSettings();
            AddEnvironment(settings, "dev");
            settings.EnvironmentsMutable.Add(null);
            AddEnvironment(settings, "prod");

            CollectionAssert.AreEqual(
                new[] { "dev", "prod" },
                settings.GetSortedEnvironments().Select(environment => environment.Id).ToArray());
        }

        /// <summary>
        /// Asserts the report has no configuration errors, ignoring the "several settings assets"
        /// finding: that describes the host project the tests run in, not the fixture under test.
        /// </summary>
        private static void AssertNoConfigurationErrors(BuildValidationReport report)
        {
            var errors = report.Issues
                .Where(issue => issue.IsError && !issue.Message.Contains("settings assets exist"))
                .Select(issue => issue.ToString())
                .ToArray();

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        private BuildManagerSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<BuildManagerSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            m_Created.Add(settings);
            return settings;
        }

        private BuildTargetProfile AddProfile(BuildManagerSettings settings, string id, BuildTarget target,
            string outputTemplate = null)
        {
            var profile = ScriptableObject.CreateInstance<BuildTargetProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            profile.name = "Profile_" + id;
            m_Created.Add(profile);

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_Id").stringValue = id;
            serialized.FindProperty("m_Target").intValue = (int)target;

            if (!string.IsNullOrEmpty(outputTemplate))
                serialized.FindProperty("m_OutputDirectoryTemplate").stringValue = outputTemplate;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            settings.ProfilesMutable.Add(profile);
            return profile;
        }

        private BuildEnvironment AddEnvironment(BuildManagerSettings settings, string id)
        {
            var environment = ScriptableObject.CreateInstance<BuildEnvironment>();
            environment.hideFlags = HideFlags.HideAndDontSave;
            environment.name = "Env_" + id;
            m_Created.Add(environment);

            var serialized = new SerializedObject(environment);
            serialized.FindProperty("m_Id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            settings.EnvironmentsMutable.Add(environment);
            return environment;
        }
    }

    /// <summary>External process handling, which every shell-based action depends on.</summary>
    [TestFixture]
    internal sealed class ProcessRunnerTests
    {
        [Test]
        public void RunShell_CapturesOutputAndSucceeds()
        {
            var result = ProcessRunner.RunShell("echo bmk-hello");

            Assert.IsTrue(result.Succeeded, result.StandardError);
            StringAssert.Contains("bmk-hello", result.Trimmed);
            Assert.IsFalse(result.OutputTruncated);
        }

        [Test]
        public void RunShell_ReportsANonZeroExitCode()
        {
            var result = ProcessRunner.RunShell("exit 3");

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(3, result.ExitCode);
        }

        [Test]
        public void RunShell_KillsACommandThatOverrunsItsTimeout()
        {
#if UNITY_EDITOR_WIN
            Assert.Ignore("Uses a POSIX shell command; the behaviour under cmd.exe is covered manually.");
#endif
            var stopwatch = Stopwatch.StartNew();
            var result = ProcessRunner.RunShell("sleep 30", timeoutMs: 1500);
            stopwatch.Stop();

            Assert.IsTrue(result.TimedOut, "A command past its timeout must be reported as timed out.");
            Assert.IsFalse(result.Succeeded);
            Assert.Less(stopwatch.ElapsedMilliseconds, 20000, "The process should have been killed, not waited out.");
        }

        [Test]
        public void RunShell_StreamsEveryLineEvenWhenCaptureIsCapped()
        {
#if UNITY_EDITOR_WIN
            Assert.Ignore("Uses a POSIX shell loop; cmd.exe needs different syntax.");
#endif
            var streamed = 0;
            var result = ProcessRunner.RunShell(
                "for i in $(seq 1 500); do echo line-$i; done",
                onLine: (_, __) => streamed++);

            Assert.IsTrue(result.Succeeded, result.StandardError);
            Assert.AreEqual(500, streamed, "Every line must reach the streaming callback.");
        }

        [Test]
        public void Quote_EscapesEmbeddedQuotes()
        {
            Assert.AreEqual("\"a b\"", ProcessRunner.Quote("a b"));
            StringAssert.Contains("\\\"", ProcessRunner.Quote("say \"hi\""));
            Assert.AreEqual("\"\"", ProcessRunner.Quote(null));
        }
    }
}
