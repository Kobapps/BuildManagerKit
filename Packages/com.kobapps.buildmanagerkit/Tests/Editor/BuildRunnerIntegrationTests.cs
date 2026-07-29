using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    /// <summary>
    /// End to end coverage of <see cref="BuildRunner"/>.
    ///
    /// The dry run tests exercise the whole pipeline — resolution, validation, ordering, token
    /// substitution, action execution and reporting — without invoking
    /// <c>BuildPipeline.BuildPlayer</c>, so they finish in milliseconds and are safe to run on
    /// every commit. The real player build lives in <see cref="RealBuild"/> and is marked
    /// <c>[Explicit]</c> because it takes minutes.
    /// </summary>
    [TestFixture]
    internal sealed class BuildRunnerIntegrationTests
    {
        private const string k_TempFolder = "Assets/BuildManagerKitTempTests";

        private BuildTargetProfile m_Profile;
        private BuildEnvironment m_Environment;
        private string m_OutputRoot;

        [SetUp]
        public void SetUp()
        {
            // Several cases deliberately provoke build failures, and the runner reports those
            // through Debug.LogError, which the test framework would otherwise treat as a failure.
            LogAssert.ignoreFailingMessages = true;

            // Every case below builds the project's real scene list; without one there is nothing
            // meaningful to assert.
            if (!EditorBuildSettings.scenes.Any(scene => scene.enabled))
                Assert.Ignore("The project has no enabled scenes in Build Settings.");

            ProjectPaths.EnsureAssetFolder(k_TempFolder);

            m_OutputRoot = Path.Combine(ProjectPaths.ProjectRoot, "Temp/BuildManagerKitTests");

            // Configure before CreateAsset: creating the asset writes the current state to disk,
            // and a later import would otherwise reload the defaults over unsaved edits.
            m_Environment = ScriptableObject.CreateInstance<BuildEnvironment>();

            var environment = new SerializedObject(m_Environment);
            environment.FindProperty("m_Id").stringValue = "test";
            environment.FindProperty("m_DisplayName").stringValue = "Test";

            var defines = environment.FindProperty("m_ScriptingDefines");
            defines.arraySize = 0;

            var variables = environment.FindProperty("m_Variables");
            variables.arraySize = 1;
            variables.GetArrayElementAtIndex(0).FindPropertyRelative("key").stringValue = "api_url";
            variables.GetArrayElementAtIndex(0).FindPropertyRelative("value").stringValue = "https://test.example";
            environment.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(m_Environment, k_TempFolder + "/Env_Test.asset");

            m_Profile = ScriptableObject.CreateInstance<BuildTargetProfile>();

            var profile = new SerializedObject(m_Profile);
            profile.FindProperty("m_Id").stringValue = "integration";
            profile.FindProperty("m_DisplayName").stringValue = "Integration";
            profile.FindProperty("m_Target").intValue = (int)EditorUserBuildSettings.activeBuildTarget;
            profile.FindProperty("m_OutputDirectoryTemplate").stringValue =
                "{projectRoot}/Temp/BuildManagerKitTests/{env}/{targetShort}";
            profile.FindProperty("m_ExecutableNameTemplate").stringValue = "BmkTest";
            profile.FindProperty("m_VersionSource").intValue = (int)VersionSource.Profile;
            profile.FindProperty("m_Version").stringValue = "1.2.3";
            profile.FindProperty("m_BuildNumberPolicy").intValue = (int)BuildNumberPolicy.Manual;
            profile.FindProperty("m_BuildNumber").intValue = 77;
            profile.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(m_Profile, k_TempFolder + "/Profile_Test.asset");

            AssetDatabase.SaveAssets();

            Assume.That(m_Environment.Id, Is.EqualTo("test"), "The test environment was not configured.");
            Assume.That(m_Profile.Id, Is.EqualTo("integration"), "The test profile was not configured.");
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            AssetDatabase.DeleteAsset(k_TempFolder);

            if (Directory.Exists(m_OutputRoot))
                Directory.Delete(m_OutputRoot, true);
        }

        [Test]
        public void DryRun_ResolvesOutputPathFromTokens()
        {
            var result = Run(dryRun: true);

            Assert.AreEqual(BuildRunStatus.Succeeded, result.status, result.message);
            Assert.AreEqual("1.2.3", result.version);
            Assert.AreEqual(77, result.buildNumber);

            StringAssert.Contains("/Temp/BuildManagerKitTests/test/", result.outputPath);
            StringAssert.Contains("BmkTest", result.outputPath);
        }

        [Test]
        public void DryRun_DoesNotWriteAnything()
        {
            Run(dryRun: true);
            Assert.IsFalse(Directory.Exists(m_OutputRoot), "A dry run must not create the output folder.");
        }

        [Test]
        public void DryRun_RunsActionsInOrder()
        {
            var order = new System.Collections.Generic.List<string>();
            RecordingStep.Recorder = order.Add;

            try
            {
                AddStep(m_Profile, "m_PreBuildSteps", new RecordingStep { Tag = "profile-pre" });
                AddStep(m_Environment, "m_PreBuildSteps", new RecordingStep { Tag = "env-pre" });
                AddStep(m_Profile, "m_PostBuildSteps", new RecordingStep { Tag = "profile-post" });
                AddStep(m_Environment, "m_PostBuildSteps", new RecordingStep { Tag = "env-post" });

                var result = Run(dryRun: true);
                Assert.AreEqual(BuildRunStatus.Succeeded, result.status, result.message);

                // Pre build widens from general to specific, post build unwinds the other way.
                CollectionAssert.AreEqual(
                    new[] { "env-pre", "profile-pre", "profile-post", "env-post" },
                    order);
            }
            finally
            {
                RecordingStep.Recorder = null;
            }
        }

        [Test]
        public void DryRun_FailingActionAbortsAndIsReported()
        {
            // The runner reports failures through Debug.LogError, which the framework would
            // otherwise turn into a test failure.
            LogAssert.ignoreFailingMessages = true;

            AddStep(m_Profile, "m_PreBuildSteps", new ThrowingStep());

            var result = Run(dryRun: true);

            Assert.AreEqual(BuildRunStatus.Failed, result.status);
            StringAssert.Contains("deliberate failure", result.message);
        }

        [Test]
        public void DryRun_DisabledActionIsSkipped()
        {
            var step = new ThrowingStep { Enabled = false };
            AddStep(m_Profile, "m_PreBuildSteps", step);

            Assert.AreEqual(BuildRunStatus.Succeeded, Run(dryRun: true).status);
        }

        [Test]
        public void DryRun_WarnAndContinueDoesNotAbort()
        {
            AddStep(m_Profile, "m_PreBuildSteps", new ThrowingStep { PolicyIsWarn = true });

            Assert.AreEqual(BuildRunStatus.Succeeded, Run(dryRun: true).status);
        }

        [Test]
        public void Validate_ReportsAnUnsupportedEnvironment()
        {
            var other = ScriptableObject.CreateInstance<BuildEnvironment>();
            other.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                var allowed = new SerializedObject(m_Profile).FindProperty("m_AllowedEnvironments");
                allowed.arraySize = 1;
                allowed.GetArrayElementAtIndex(0).objectReferenceValue = other;
                allowed.serializedObject.ApplyModifiedPropertiesWithoutUndo();

                var report = BuildRunner.Validate(m_Profile, m_Environment);

                Assert.IsTrue(report.HasErrors);
                Assert.IsTrue(report.Issues.Any(issue => issue.Message.Contains("does not allow environment")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        [Test]
        [Explicit("Runs a real player build; takes minutes.")]
        public void RealBuild()
        {
            var result = Run(dryRun: false);

            Assert.AreEqual(BuildRunStatus.Succeeded, result.status, result.message);
            Assert.Greater(result.outputSizeBytes, 0, "The build produced no output.");
            Assert.IsTrue(File.Exists(result.outputPath) || Directory.Exists(result.outputPath),
                $"'{result.outputPath}' does not exist.");

            var manifest = Path.Combine(Path.GetDirectoryName(result.outputPath) ?? string.Empty,
                "build_manifest.json");
            Assert.IsTrue(File.Exists(manifest), "build_manifest.json was not written.");
        }

        private BuildRunResult Run(bool dryRun) => BuildRunner.Run(new BuildRunRequest
        {
            Profile = m_Profile,
            Environment = m_Environment,
            DryRun = dryRun,
            Interactive = false,
            AllowPlatformSwitch = false
        });

        private static void AddStep(ScriptableObject owner, string listPath, BuildStep step)
        {
            var serialized = new SerializedObject(owner);
            var list = serialized.FindProperty(listPath);
            var index = list.arraySize;

            list.arraySize++;
            list.GetArrayElementAtIndex(index).managedReferenceValue = step;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Records the order actions execute in.</summary>
        [Serializable]
        internal sealed class RecordingStep : BuildStep
        {
            internal static Action<string> Recorder;

            [SerializeField] private string m_Tag = string.Empty;

            internal string Tag
            {
                get => m_Tag;
                set => m_Tag = value;
            }

            public override void Execute(BuildContext context) => Recorder?.Invoke(m_Tag);
        }

        /// <summary>Always throws, to exercise the failure policies.</summary>
        [Serializable]
        internal sealed class ThrowingStep : BuildStep
        {
            [SerializeField] private bool m_PolicyIsWarn;

            internal bool PolicyIsWarn
            {
                get => m_PolicyIsWarn;
                set => m_PolicyIsWarn = value;
            }

            public override StepFailurePolicy OnError =>
                m_PolicyIsWarn ? StepFailurePolicy.WarnAndContinue : StepFailurePolicy.FailBuild;

            public override void Execute(BuildContext context) =>
                throw new BuildStepException("deliberate failure");
        }
    }
}
