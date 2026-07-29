using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    /// <summary>
    /// Per-run build overrides and the command line that feeds them.
    ///
    /// The point of these is that a build server can reach every setting the Editor exposes
    /// without editing the profile assets, so the profiles in version control stay exactly as the
    /// team authored them. An override that silently fails to apply would be invisible until
    /// someone noticed a shipped build had the wrong scripting backend.
    /// </summary>
    [TestFixture]
    internal sealed class BuildOverridesTests
    {
        // ---------------------------------------------------------------- flag folding

        [Test]
        public void Apply_LeavesFlagsAloneWhenNothingIsOverridden()
        {
            var options = BuildOptions.Development | BuildOptions.StrictMode;

            Assert.AreEqual(options, new BuildOverrides().Apply(options, developmentBuild: true));
        }

        [Test]
        public void Apply_TurnsFlagsOnAndOff()
        {
            var overrides = new BuildOverrides { StrictMode = false, CleanBuildCache = true };
            var result = overrides.Apply(BuildOptions.StrictMode, developmentBuild: false);

            Assert.IsFalse(result.HasFlag(BuildOptions.StrictMode));
            Assert.IsTrue(result.HasFlag(BuildOptions.CleanBuildCache));
        }

        [Test]
        public void Apply_SetsTheDevelopmentFlag()
        {
            var result = new BuildOverrides { DevelopmentBuild = true }
                .Apply(BuildOptions.None, developmentBuild: true);

            Assert.IsTrue(result.HasFlag(BuildOptions.Development));
        }

        [Test]
        public void Apply_StripsProfilerFlagsFromANonDevelopmentBuild()
        {
            // Unity rejects these on a release player, so a profile that had them must not leak
            // them through when the command line turns development off.
            var options = BuildOptions.Development | BuildOptions.ConnectWithProfiler
                          | BuildOptions.EnableDeepProfilingSupport | BuildOptions.AllowDebugging;

            var result = new BuildOverrides { DevelopmentBuild = false }
                .Apply(options, developmentBuild: false);

            Assert.IsFalse(result.HasFlag(BuildOptions.Development));
            Assert.IsFalse(result.HasFlag(BuildOptions.ConnectWithProfiler));
            Assert.IsFalse(result.HasFlag(BuildOptions.EnableDeepProfilingSupport));
            Assert.IsFalse(result.HasFlag(BuildOptions.AllowDebugging));
        }

        [Test]
        public void Apply_HonoursProfilerFlagsOnADevelopmentBuild()
        {
            var overrides = new BuildOverrides { AutoConnectProfiler = true, DeepProfiling = true };
            var result = overrides.Apply(BuildOptions.Development, developmentBuild: true);

            Assert.IsTrue(result.HasFlag(BuildOptions.ConnectWithProfiler));
            Assert.IsTrue(result.HasFlag(BuildOptions.EnableDeepProfilingSupport));
        }

        [Test]
        public void Apply_ReplacesTheCompressionModeRatherThanCombiningIt()
        {
            var result = new BuildOverrides { Compression = BuildCompression.Lz4HC }
                .Apply(BuildOptions.CompressWithLz4, developmentBuild: false);

            Assert.IsFalse(result.HasFlag(BuildOptions.CompressWithLz4),
                "Two compression flags at once is not a valid combination.");
            Assert.IsTrue(result.HasFlag(BuildOptions.CompressWithLz4HC));
        }

        [Test]
        public void Apply_DefaultCompressionClearsBothFlags()
        {
            var result = new BuildOverrides { Compression = BuildCompression.Default }
                .Apply(BuildOptions.CompressWithLz4HC, developmentBuild: false);

            Assert.IsFalse(result.HasFlag(BuildOptions.CompressWithLz4));
            Assert.IsFalse(result.HasFlag(BuildOptions.CompressWithLz4HC));
        }

        [Test]
        public void HasAny_IsFalseForAnEmptySet()
        {
            Assert.IsFalse(new BuildOverrides().HasAny);
            Assert.AreEqual("none", new BuildOverrides().Describe());
        }

        [Test]
        public void HasAny_NoticesEveryKindOfOverride()
        {
            Assert.IsTrue(new BuildOverrides { DevelopmentBuild = false }.HasAny,
                "An explicit false is still an override.");
            Assert.IsTrue(new BuildOverrides { ExecutableName = "Game" }.HasAny);
            Assert.IsTrue(new BuildOverrides { Scenes = new[] { "a.unity" } }.HasAny);
            Assert.IsTrue(new BuildOverrides { StrippingLevel = ManagedStrippingLevel.High }.HasAny);
        }

        [Test]
        public void Describe_ListsWhatWasOverridden()
        {
            var text = new BuildOverrides
            {
                DevelopmentBuild = true,
                ScriptingBackend = ScriptingImplementation.IL2CPP,
                AppleTeamId = "ABC123"
            }.Describe();

            StringAssert.Contains("development=True", text);
            StringAssert.Contains("scriptingBackend=IL2CPP", text);
            StringAssert.Contains("appleTeamId=ABC123", text);
        }

        // ---------------------------------------------------------------- command line

        private static BuildOverrides Parse(params string[] arguments)
        {
            var overrides = BuildCLI.ReadOverrides(new CommandLineArgs(arguments), out var failed);
            Assert.IsFalse(failed, "The arguments should have parsed cleanly.");
            return overrides;
        }

        [Test]
        public void CommandLine_ReadsEveryBuildOptionFlag()
        {
            var overrides = Parse(
                "-bmkDevelopment", "true",
                "-bmkAutoConnectProfiler", "true",
                "-bmkDeepProfiling", "false",
                "-bmkScriptDebugging", "true",
                "-bmkStrictMode", "false",
                "-bmkCleanBuild", "true",
                "-bmkDetailedReport", "false");

            Assert.IsTrue(overrides.DevelopmentBuild);
            Assert.IsTrue(overrides.AutoConnectProfiler);
            Assert.IsFalse(overrides.DeepProfiling);
            Assert.IsTrue(overrides.ScriptDebugging);
            Assert.IsFalse(overrides.StrictMode);
            Assert.IsTrue(overrides.CleanBuildCache);
            Assert.IsFalse(overrides.DetailedBuildReport);
        }

        [Test]
        public void CommandLine_LeavesUnsuppliedOptionsNull()
        {
            // Null is what preserves the profile's own value; defaulting to false would silently
            // turn settings off on every CI build.
            var overrides = Parse("-bmkProfile", "android");

            Assert.IsNull(overrides.DevelopmentBuild);
            Assert.IsNull(overrides.StrictMode);
            Assert.IsNull(overrides.Compression);
            Assert.IsNull(overrides.ScriptingBackend);
            Assert.IsFalse(overrides.HasAny);
        }

        [Test]
        public void CommandLine_ReadsPlayerSettingEnums()
        {
            var overrides = Parse(
                "-bmkScriptingBackend", "IL2CPP",
                "-bmkIl2CppConfig", "Master",
                "-bmkStripping", "High",
                "-bmkCompression", "Lz4HC");

            Assert.AreEqual(ScriptingImplementation.IL2CPP, overrides.ScriptingBackend);
            Assert.AreEqual(Il2CppCompilerConfiguration.Master, overrides.Il2CppConfiguration);
            Assert.AreEqual(ManagedStrippingLevel.High, overrides.StrippingLevel);
            Assert.AreEqual(BuildCompression.Lz4HC, overrides.Compression);
        }

        [Test]
        public void CommandLine_ParsesEnumsCaseInsensitively()
        {
            Assert.AreEqual(ScriptingImplementation.IL2CPP, Parse("-bmkScriptingBackend", "il2cpp").ScriptingBackend);
            Assert.AreEqual(ManagedStrippingLevel.Minimal, Parse("-bmkStripping", "MINIMAL").StrippingLevel);
        }

        [Test]
        public void CommandLine_RejectsAnUnknownEnumValue()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                BuildCLI.ReadOverrides(
                    new CommandLineArgs(new[] { "-bmkScriptingBackend", "Wobble" }), out var failed);

                Assert.IsTrue(failed, "An unparsable value must abort rather than be ignored.");
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void CommandLine_ReadsAndroidOptions()
        {
            var overrides = Parse(
                "-bmkAppBundle", "true",
                "-bmkSplitBinary", "false",
                "-bmkAndroidArchitectures", "ARM64",
                "-bmkKeystore", "keys/release.keystore",
                "-bmkKeyalias", "release");

            Assert.IsTrue(overrides.AndroidAppBundle);
            Assert.IsFalse(overrides.AndroidSplitBinary);
            Assert.AreEqual(AndroidArchitecture.ARM64, overrides.AndroidArchitectures);
            Assert.AreEqual("keys/release.keystore", overrides.AndroidKeystorePath);
            Assert.AreEqual("release", overrides.AndroidKeyaliasName);
        }

        [Test]
        public void CommandLine_ReadsCombinedAndroidArchitectures()
        {
            // AndroidArchitecture is a flags enum, so a combined value has to survive parsing.
            var overrides = Parse("-bmkAndroidArchitectures", "ARMv7,ARM64");

            Assert.IsTrue(overrides.AndroidArchitectures.Value.HasFlag(AndroidArchitecture.ARMv7));
            Assert.IsTrue(overrides.AndroidArchitectures.Value.HasFlag(AndroidArchitecture.ARM64));
        }

        [Test]
        public void CommandLine_ReadsTheSubtargetAndTheServerShorthand()
        {
            Assert.AreEqual(StandaloneBuildSubtarget.Server, Parse("-bmkSubtarget", "Server").StandaloneSubtarget);
            Assert.AreEqual(StandaloneBuildSubtarget.Player, Parse("-bmkSubtarget", "Player").StandaloneSubtarget);
            Assert.AreEqual(StandaloneBuildSubtarget.Server, Parse("-bmkServer").StandaloneSubtarget);
        }

        [Test]
        public void CommandLine_ReadsContentOverrides()
        {
            var overrides = Parse(
                "-bmkExecutable", "MyGame",
                "-bmkScenes", "Assets/A.unity;Assets/B.unity",
                "-bmkAppleTeamId", "TEAM123");

            Assert.AreEqual("MyGame", overrides.ExecutableName);
            CollectionAssert.AreEqual(new[] { "Assets/A.unity", "Assets/B.unity" }, overrides.Scenes);
            Assert.AreEqual("TEAM123", overrides.AppleTeamId);
        }

        [Test]
        public void CommandLine_AcceptsEveryArgumentSpelling()
        {
            var overrides = Parse("--bmk-scripting-backend=Mono2x", "-bmk.development", "true");

            Assert.AreEqual(ScriptingImplementation.Mono2x, overrides.ScriptingBackend);
            Assert.IsTrue(overrides.DevelopmentBuild);
        }

        /// <summary>
        /// Every documented override should be readable from the command line. A field added to
        /// <see cref="BuildOverrides"/> without a matching flag would silently be unreachable from
        /// CI, which is exactly the gap this suite exists to prevent.
        /// </summary>
        [Test]
        public void EveryOverrideFieldIsReachableFromTheCommandLine()
        {
            var arguments = new[]
            {
                "-bmkDevelopment", "true",
                "-bmkAutoConnectProfiler", "true",
                "-bmkDeepProfiling", "true",
                "-bmkScriptDebugging", "true",
                "-bmkStrictMode", "true",
                "-bmkCleanBuild", "true",
                "-bmkDetailedReport", "true",
                "-bmkCompression", "Lz4",
                "-bmkScriptingBackend", "IL2CPP",
                "-bmkIl2CppConfig", "Release",
                "-bmkStripping", "Low",
                "-bmkAppBundle", "true",
                "-bmkSplitBinary", "true",
                "-bmkAndroidArchitectures", "ARM64",
                "-bmkKeystore", "k.keystore",
                "-bmkKeyalias", "alias",
                "-bmkAppleTeamId", "TEAM",
                "-bmkSubtarget", "Server",
                "-bmkExecutable", "Game",
                "-bmkScenes", "Assets/A.unity"
            };

            var overrides = Parse(arguments);

            var unset = typeof(BuildOverrides)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(field => field.GetValue(overrides) == null)
                .Select(field => field.Name)
                .ToArray();

            Assert.IsEmpty(unset,
                "These BuildOverrides fields have no command line flag: " + string.Join(", ", unset));
        }
    }
}
