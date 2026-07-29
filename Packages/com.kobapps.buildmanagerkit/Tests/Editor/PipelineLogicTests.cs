using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    [TestFixture]
    internal sealed class VersionServiceTests
    {
        [TestCase("1.4.2", VersionComponent.Patch, "1.4.3")]
        [TestCase("1.4.2", VersionComponent.Minor, "1.5.0")]
        [TestCase("1.4.2", VersionComponent.Major, "2.0.0")]
        [TestCase("1.4", VersionComponent.Patch, "1.4.1")]
        [TestCase("1", VersionComponent.Minor, "1.1.0")]
        public void Bump_IncrementsAndZeroesLowerComponents(string input, VersionComponent component,
            string expected)
        {
            Assert.AreEqual(expected, VersionService.Bump(input, component));
        }

        [Test]
        public void Bump_PreservesSuffix()
        {
            Assert.AreEqual("1.4.3-beta", VersionService.Bump("1.4.2-beta", VersionComponent.Patch));
        }

        [Test]
        public void Bump_LeavesUnparsableInputAlone()
        {
            Assert.AreEqual("nightly", VersionService.Bump("nightly", VersionComponent.Patch));
        }

        [Test]
        public void Bump_HandlesEmptyInput()
        {
            Assert.AreEqual("0.0.1", VersionService.Bump(string.Empty, VersionComponent.Patch));
        }

        [TestCase("1.0.0", true)]
        [TestCase("1.0", true)]
        [TestCase("1.0.0-rc1", true)]
        [TestCase("", false)]
        [TestCase("v1.0.0", false)]
        [TestCase("latest", false)]
        public void IsValid_MatchesSemanticVersions(string input, bool expected)
        {
            Assert.AreEqual(expected, VersionService.IsValid(input));
        }
    }

    [TestFixture]
    internal sealed class ScriptingDefineUtilityTests
    {
        private BuildEnvironment m_Dev;
        private BuildEnvironment m_Prod;

        [SetUp]
        public void SetUp()
        {
            m_Dev = CreateEnvironment("dev", new[] { "DEBUG_MENU" });
            m_Prod = CreateEnvironment("prod", new[] { "ANALYTICS" });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Dev);
            Object.DestroyImmediate(m_Prod);
        }

        [Test]
        public void Compose_AddsIncomingEnvironmentDefines()
        {
            var result = Compose(new[] { "EXISTING" }, m_Prod);

            CollectionAssert.Contains(result, "ANALYTICS");
            CollectionAssert.Contains(result, "ENV_PROD");
            CollectionAssert.Contains(result, "EXISTING");
        }

        [Test]
        public void Compose_StripsDefinesOwnedByOtherEnvironments()
        {
            // Simulate switching dev → prod: nothing from dev may survive.
            var result = Compose(new[] { "DEBUG_MENU", "ENV_DEV", "UNRELATED" }, m_Prod);

            CollectionAssert.DoesNotContain(result, "DEBUG_MENU");
            CollectionAssert.DoesNotContain(result, "ENV_DEV");
            CollectionAssert.Contains(result, "UNRELATED");
            CollectionAssert.Contains(result, "ENV_PROD");
        }

        [Test]
        public void Compose_KeepsADefineSharedByTwoEnvironments()
        {
            // The shipped agent skill documents sharing a define across environments as supported —
            // it is how "non production" is expressed. Strip-all-then-add-current is what makes it
            // work, so pin it: reordering those two steps would silently break the pattern.
            var qa = CreateEnvironment("qa", new[] { "NON_PROD" });
            var staging = CreateEnvironment("staging", new[] { "NON_PROD" });

            try
            {
                var result = ScriptingDefineUtility.Compose(
                    new[] { "NON_PROD", "ENV_STAGING" }, qa, new[] { m_Dev, m_Prod, qa, staging });

                CollectionAssert.Contains(result, "NON_PROD",
                    "A define the incoming environment also declares must survive the strip.");
                CollectionAssert.DoesNotContain(result, "ENV_STAGING");
            }
            finally
            {
                Object.DestroyImmediate(qa);
                Object.DestroyImmediate(staging);
            }
        }

        [Test]
        public void Compose_DropsASharedDefineWhenTheIncomingEnvironmentDoesNotDeclareIt()
        {
            var shared = CreateEnvironment("shared", new[] { "NON_PROD" });

            try
            {
                var result = ScriptingDefineUtility.Compose(
                    new[] { "NON_PROD" }, m_Prod, new[] { m_Dev, m_Prod, shared });

                CollectionAssert.DoesNotContain(result, "NON_PROD");
            }
            finally
            {
                Object.DestroyImmediate(shared);
            }
        }

        [Test]
        public void Compose_IsIdempotent()
        {
            var once = Compose(new[] { "UNRELATED" }, m_Prod);
            var twice = Compose(once, m_Prod);

            CollectionAssert.AreEqual(once, twice);
        }

        [Test]
        public void Compose_AppliesExtraDefines()
        {
            var result = Compose(System.Array.Empty<string>(), m_Prod, new[] { "PROFILE_DEFINE" });
            CollectionAssert.Contains(result, "PROFILE_DEFINE");
        }

        [Test]
        public void Normalize_SortsAndDeduplicates()
        {
            var result = ScriptingDefineUtility.Normalize(new[] { "B", "A", "B", " C ", string.Empty, null });
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, result);
        }

        [Test]
        public void Split_HandlesBothSeparatorsAndBlanks()
        {
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, ScriptingDefineUtility.Split("A;B, C ;;"));
            CollectionAssert.IsEmpty(ScriptingDefineUtility.Split(null));
        }

        private string[] Compose(IEnumerable<string> current, BuildEnvironment environment,
            IEnumerable<string> extra = null) =>
            ScriptingDefineUtility.Compose(current, environment, new[] { m_Dev, m_Prod }, extra);

        private static BuildEnvironment CreateEnvironment(string id, string[] defines)
        {
            var environment = ScriptableObject.CreateInstance<BuildEnvironment>();
            environment.hideFlags = HideFlags.HideAndDontSave;

            var serialized = new SerializedObject(environment);
            serialized.FindProperty("m_Id").stringValue = id;

            var array = serialized.FindProperty("m_ScriptingDefines");
            array.arraySize = defines.Length;
            for (var i = 0; i < defines.Length; i++)
                array.GetArrayElementAtIndex(i).stringValue = defines[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return environment;
        }
    }

    [TestFixture]
    internal sealed class CommandLineArgsTests
    {
        [Test]
        public void Parse_AcceptsEverySupportedSpelling()
        {
            var args = new CommandLineArgs(new[]
            {
                "-bmkProfile", "android",
                "--bmk-env=prod",
                "-bmk.build-number", "42",
                "-bmkDryRun"
            });

            Assert.AreEqual("android", args.GetString("bmkProfile"));
            Assert.AreEqual("prod", args.GetString("bmkEnv"));
            Assert.AreEqual(42, args.GetInt("bmkBuildNumber"));
            Assert.IsTrue(args.GetBool("bmkDryRun"));
        }

        [Test]
        public void Parse_TreatsTrailingFlagAsTrue()
        {
            var args = new CommandLineArgs(new[] { "-bmkNoExit" });
            Assert.IsTrue(args.GetBool("bmkNoExit"));
        }

        [Test]
        public void GetBool_HonoursExplicitFalse()
        {
            var args = new CommandLineArgs(new[] { "-bmkDryRun=false", "-other", "0" });

            Assert.IsFalse(args.GetBool("bmkDryRun"));
            Assert.IsFalse(args.GetBool("other"));
        }

        [Test]
        public void GetList_SplitsOnBothSeparators()
        {
            var args = new CommandLineArgs(new[] { "-bmkDefines", "A;B,C" });
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, args.GetList("bmkDefines"));
        }

        [Test]
        public void Missing_ArgumentsFallBack()
        {
            var args = new CommandLineArgs(new[] { "-unrelated" });

            Assert.AreEqual("fallback", args.GetString("bmkProfile", "fallback"));
            Assert.IsNull(args.GetInt("bmkBuildNumber"));
            Assert.IsFalse(args.Has("bmkProfile"));
        }
    }

    [TestFixture]
    internal sealed class BuildTargetUtilityTests
    {
        [TestCase(BuildTarget.StandaloneWindows64, "MyGame", false, "MyGame.exe")]
        [TestCase(BuildTarget.StandaloneOSX, "MyGame", false, "MyGame.app")]
        [TestCase(BuildTarget.StandaloneLinux64, "MyGame", false, "MyGame.x86_64")]
        [TestCase(BuildTarget.Android, "MyGame", false, "MyGame.apk")]
        [TestCase(BuildTarget.Android, "MyGame", true, "MyGame.aab")]
        [TestCase(BuildTarget.iOS, "MyGame", false, "MyGame")]
        [TestCase(BuildTarget.WebGL, "My Game", false, "My_Game")]
        public void GetPlayerFileName_AppliesPlatformExtension(BuildTarget target, string baseName,
            bool appBundle, string expected)
        {
            Assert.AreEqual(expected, BuildTargetUtility.GetPlayerFileName(target, baseName, appBundle));
        }

        [Test]
        public void GetPlayerFileName_FallsBackWhenNameIsUnusable()
        {
            Assert.AreEqual("Player.exe", BuildTargetUtility.GetPlayerFileName(
                BuildTarget.StandaloneWindows64, "///", false));
        }

        [TestCase(0L, "0 B")]
        [TestCase(512L, "512 B")]
        [TestCase(2048L, "2 KB")]
        [TestCase(1572864L, "1.5 MB")]
        public void FormatSize_IsHumanReadable(long bytes, string expected)
        {
            Assert.AreEqual(expected, BuildTargetUtility.FormatSize(bytes));
        }

        [Test]
        public void FormatDuration_SwitchesUnits()
        {
            Assert.AreEqual("4.5s", BuildTargetUtility.FormatDuration(System.TimeSpan.FromSeconds(4.5)));
            Assert.AreEqual("2m 30s", BuildTargetUtility.FormatDuration(System.TimeSpan.FromSeconds(150)));
            Assert.AreEqual("1h 1m 0s", BuildTargetUtility.FormatDuration(System.TimeSpan.FromMinutes(61)));
        }
    }

    [TestFixture]
    internal sealed class BuildStepRegistryTests
    {
        [Test]
        public void Descriptors_IncludeTheBuiltInActions()
        {
            var menuPaths = new List<string>();
            foreach (var descriptor in BuildStepRegistry.Descriptors)
                menuPaths.Add(descriptor.MenuPath);

            CollectionAssert.Contains(menuPaths, "Files/Copy Files");
            CollectionAssert.Contains(menuPaths, "Automation/Run Shell Command");
            CollectionAssert.Contains(menuPaths, "Versioning/Increment Version");
        }

        [Test]
        public void GetDescriptors_HonoursScope()
        {
            foreach (var descriptor in BuildStepRegistry.GetDescriptors(BuildStepScope.PostBuild))
                Assert.AreNotEqual(0, descriptor.Scope & BuildStepScope.PostBuild,
                    $"{descriptor.MenuPath} was offered for a post build list it does not support.");
        }

        [Test]
        public void TestOnlyStepsAreNotOfferedInTheAddActionMenu()
        {
            // This fixture's own assembly defines several BuildStep subclasses, and test
            // assemblies are loaded in the Editor. Without filtering they appear in the user's
            // Add Action menu — and one picked by mistake gets serialised into a real profile.
            var leaked = BuildStepRegistry.Descriptors
                .Where(descriptor => descriptor.Type.Assembly
                    .GetReferencedAssemblies()
                    .Any(reference => reference.Name.IndexOf("nunit", System.StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(descriptor => descriptor.Type.FullName)
                .ToArray();

            Assert.IsEmpty(leaked, "Step types from test assemblies leaked into the menu: "
                                   + string.Join(", ", leaked));
        }

        [Test]
        public void TestOnlyHooksDoNotRunDuringABuild()
        {
            var leaked = BuildStepRegistry.Hooks
                .Where(hook => hook.Method.DeclaringType != null && hook.Method.DeclaringType.Assembly
                    .GetReferencedAssemblies()
                    .Any(reference => reference.Name.IndexOf("nunit", System.StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(hook => hook.DisplayName)
                .ToArray();

            Assert.IsEmpty(leaked, "Build hooks from test assemblies would run in real builds: "
                                   + string.Join(", ", leaked));
        }

        [Test]
        public void EveryDescriptor_CanBeInstantiated()
        {
            foreach (var descriptor in BuildStepRegistry.Descriptors)
                Assert.IsNotNull(descriptor.CreateInstance(), descriptor.MenuPath);
        }

        [Test]
        public void GetDisplayName_FallsBackToASplitClassName()
        {
            Assert.AreEqual("Copy Files", BuildStepRegistry.GetDisplayName(typeof(CopyFilesStep)));
        }
    }
}
