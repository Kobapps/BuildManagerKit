using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    [TestFixture]
    internal sealed class AgentSkillTests
    {
        private string m_Temp;

        [SetUp]
        public void SetUp()
        {
            m_Temp = Path.Combine(Path.GetTempPath(), "bmk-skill-" + Path.GetRandomFileName());
            Directory.CreateDirectory(m_Temp);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Temp))
                Directory.Delete(m_Temp, true);
        }

        [Test]
        public void ShippedSkill_IsPresentAndDeclaresTheExpectedName()
        {
            var source = AgentSkill.SourcePath;

            Assert.IsNotNull(source, "The package must ship Skills~/buildmanagerkit/SKILL.md.");
            Assert.IsTrue(AgentSkill.IsOurSkillFolder(source));
        }

        [Test]
        public void ShippedSkill_HasTheReferencesTheSkillLinksTo()
        {
            var files = AgentSkill.GetFileList();

            CollectionAssert.Contains(files, "SKILL.md");
            CollectionAssert.Contains(files, "references/cli.md");
            CollectionAssert.Contains(files, "references/recipes.md");
        }

        [Test]
        public void ShippedSkill_DoesNotShipMetaFiles()
        {
            // Skills~ is not imported by Unity, so a .meta in there would be stale noise that ends
            // up copied into the user's .claude folder.
            Assert.IsEmpty(AgentSkill.GetFileList().Where(file => file.EndsWith(".meta")).ToArray());
        }

        [Test]
        public void InstallPaths_LandUnderDotClaudeSkills()
        {
            foreach (AgentSkillScope scope in System.Enum.GetValues(typeof(AgentSkillScope)))
            {
                var path = AgentSkill.GetInstallPath(scope).Replace('\\', '/');
                StringAssert.EndsWith(".claude/skills/" + AgentSkill.SkillName, path);
            }
        }

        [Test]
        public void ProjectAndUserScopes_DoNotResolveToTheSamePlace()
        {
            Assert.AreNotEqual(
                AgentSkill.GetInstallPath(AgentSkillScope.Project),
                AgentSkill.GetInstallPath(AgentSkillScope.User));
        }

        [TestCase("---\nname: buildmanagerkit\ndescription: x\n---\nbody", true)]
        [TestCase("---\nname: \"buildmanagerkit\"\n---\n", true)]
        [TestCase("---\ndescription: x\nname: buildmanagerkit\n---\n", true)]
        [TestCase("---\nname: something-else\n---\n", false)]
        [TestCase("---\ndescription: x\n---\nname: buildmanagerkit", false)]
        [TestCase("name: buildmanagerkit", false)]
        [TestCase("", false)]
        public void DeclaresSkillName_OnlyMatchesTheFrontMatter(string content, bool expected)
        {
            Assert.AreEqual(expected, AgentSkill.DeclaresSkillName(content, "buildmanagerkit"));
        }

        [Test]
        public void IsOurSkillFolder_RejectsSomebodyElsesSkill()
        {
            File.WriteAllText(Path.Combine(m_Temp, "SKILL.md"), "---\nname: someone-elses\n---\nhello");
            Assert.IsFalse(AgentSkill.IsOurSkillFolder(m_Temp));
        }

        [Test]
        public void IsOurSkillFolder_RejectsAnEmptyFolder()
        {
            Assert.IsFalse(AgentSkill.IsOurSkillFolder(m_Temp));
        }

        [Test]
        public void Uninstall_RefusesAFolderThatIsNotOurs()
        {
            // The install path is derived, not user typed, but a refusal here is what stops a
            // stray .claude/skills/buildmanagerkit written by somebody else from being deleted.
            var foreign = Path.Combine(m_Temp, "foreign");
            Directory.CreateDirectory(foreign);
            File.WriteAllText(Path.Combine(foreign, "SKILL.md"), "---\nname: not-ours\n---\n");

            Assert.IsFalse(AgentSkill.IsOurSkillFolder(foreign));
            Assert.IsTrue(Directory.Exists(foreign), "The folder must survive the check.");
        }

        [Test]
        public void Fingerprint_IgnoresLineEndings()
        {
            var unix = Path.Combine(m_Temp, "unix");
            var windows = Path.Combine(m_Temp, "windows");
            Directory.CreateDirectory(unix);
            Directory.CreateDirectory(windows);

            File.WriteAllText(Path.Combine(unix, "SKILL.md"), "---\nname: x\n---\nline one\nline two\n");
            File.WriteAllText(Path.Combine(windows, "SKILL.md"), "---\r\nname: x\r\n---\r\nline one\r\nline two\r\n");

            Assert.AreEqual(AgentSkill.Fingerprint(unix), AgentSkill.Fingerprint(windows),
                "A checkout with CRLF endings must not read as a permanently pending update.");
        }

        [Test]
        public void Fingerprint_ChangesWithContentAndWithFileSet()
        {
            var a = Path.Combine(m_Temp, "a");
            var b = Path.Combine(m_Temp, "b");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);

            File.WriteAllText(Path.Combine(a, "SKILL.md"), "one");
            File.WriteAllText(Path.Combine(b, "SKILL.md"), "two");
            Assert.AreNotEqual(AgentSkill.Fingerprint(a), AgentSkill.Fingerprint(b));

            File.WriteAllText(Path.Combine(b, "SKILL.md"), "one");
            Assert.AreEqual(AgentSkill.Fingerprint(a), AgentSkill.Fingerprint(b));

            File.WriteAllText(Path.Combine(b, "extra.md"), string.Empty);
            Assert.AreNotEqual(AgentSkill.Fingerprint(a), AgentSkill.Fingerprint(b),
                "An added file must make the fingerprint differ even when it is empty.");
        }

        [Test]
        public void Fingerprint_OfAMissingFolderIsEmpty()
        {
            Assert.AreEqual(string.Empty, AgentSkill.Fingerprint(Path.Combine(m_Temp, "nope")));
        }
    }

    [TestFixture]
    internal sealed class ConfigCLITests
    {
        [TestCase("dev", true)]
        [TestCase("qa", true)]
        [TestCase("prod_eu", true)]
        [TestCase("env2", true)]
        [TestCase("my-env", false)]      // ENV_MY-ENV is not a legal preprocessor symbol
        [TestCase("my env", false)]
        [TestCase("2fast", false)]       // cannot start with a digit
        [TestCase(" dev", false)]
        [TestCase("dev ", false)]
        [TestCase("", false)]
        [TestCase("dev.eu", false)]
        public void ValidateId_AcceptsOnlyIdentifiersThatSurviveBothUses(string id, bool expected)
        {
            Assert.AreEqual(expected, ConfigCLI.ValidateId(id, out _), id);
        }

        [Test]
        public void ValidateId_ExplainsTheRejection()
        {
            Assert.IsFalse(ConfigCLI.ValidateId("my-env", out var reason));
            Assert.IsNotEmpty(reason);
            StringAssert.Contains("my_env", reason, "The message should show what would be used instead.");
        }

        [TestCase("#FF8800", true)]
        [TestCase("FF8800", true)]
        [TestCase("#f80", true)]
        [TestCase("#FF8800AA", true)]
        [TestCase("orange", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void TryParseColor_AcceptsHtmlColoursWithOrWithoutTheHash(string raw, bool expected)
        {
            Assert.AreEqual(expected, ConfigCLI.TryParseColor(raw, out _), raw ?? "null");
        }

        [Test]
        public void TryParseColor_ReadsTheChannels()
        {
            Assert.IsTrue(ConfigCLI.TryParseColor("#FF8000", out var color));
            Assert.AreEqual(1f, color.r, 0.01f);
            Assert.AreEqual(0.5f, color.g, 0.01f);
            Assert.AreEqual(0f, color.b, 0.01f);
        }

        [Test]
        public void SetIcon_PointsTheOverrideAtATextureAndClearsItAgain()
        {
            const string folder = "Assets/BmkIconTest";
            const string texturePath = folder + "/icon.png";

            UnityEditor.AssetDatabase.CreateFolder("Assets", "BmkIconTest");

            var texture = new Texture2D(4, 4);
            System.IO.File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            UnityEditor.AssetDatabase.ImportAsset(texturePath);

            var environment = ScriptableObject.CreateInstance<BuildEnvironment>();
            environment.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                Assert.IsTrue(ConfigCLI.ApplyEnvironmentArguments(
                    environment, new CommandLineArgs(new[] { "-bmkIcon", texturePath }), out var error), error);

                Assert.IsNotNull(environment.ApplicationIconOverride,
                    "The override must be switched on, not just the reference assigned.");

                Assert.IsTrue(ConfigCLI.ApplyEnvironmentArguments(
                    environment, new CommandLineArgs(new[] { "-bmkIcon", "" }), out error), error);

                Assert.IsNull(environment.ApplicationIconOverride,
                    "An empty -bmkIcon must clear the override, which is the only way to undo it.");
            }
            finally
            {
                Object.DestroyImmediate(environment);
                UnityEditor.AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void SetIcon_RejectsAMissingAssetWithAUsableMessage()
        {
            var environment = ScriptableObject.CreateInstance<BuildEnvironment>();
            environment.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                Assert.IsFalse(ConfigCLI.ApplyEnvironmentArguments(
                    environment,
                    new CommandLineArgs(new[] { "-bmkIcon", "Assets/NoSuchIcon.png" }),
                    out var error));

                StringAssert.Contains("does not exist", error);
                Assert.IsNull(environment.ApplicationIconOverride);
            }
            finally
            {
                Object.DestroyImmediate(environment);
            }
        }

        [Test]
        public void ApplyEnvironmentArguments_LeavesUnmentionedFieldsAlone()
        {
            var environment = ScriptableObject.CreateInstance<BuildEnvironment>();
            environment.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                Assert.IsTrue(ConfigCLI.ApplyEnvironmentArguments(environment,
                    new CommandLineArgs(new[] { "-bmkProductName", "Game QA", "-bmkVars", "a=1" }), out _));

                // A second call naming only one field must not reset the other.
                Assert.IsTrue(ConfigCLI.ApplyEnvironmentArguments(environment,
                    new CommandLineArgs(new[] { "-bmkVars", "b=2" }), out _));

                Assert.AreEqual("Game QA", environment.ProductNameOverride);
                Assert.AreEqual("1", environment.GetVariable("a"), "Variables must merge, not replace.");
                Assert.AreEqual("2", environment.GetVariable("b"));
            }
            finally
            {
                Object.DestroyImmediate(environment);
            }
        }

        [Test]
        public void Describe_ReportsTheProjectWithoutThrowing()
        {
            var description = ConfigCLI.BuildDescription();

            Assert.IsNotNull(description);
            Assert.IsNotNull(description.environments);
            Assert.IsNotNull(description.profiles);

            // It has to survive JsonUtility, which is what Describe actually emits.
            var json = JsonUtility.ToJson(description, true);
            StringAssert.Contains("environments", json);
            StringAssert.Contains("activeBuildTarget", json);
        }

        [Test]
        public void Describe_ListsEveryEnvironmentWithItsGeneratedDefine()
        {
            var description = ConfigCLI.BuildDescription();

            foreach (var environment in description.environments)
            {
                Assert.IsNotEmpty(environment.id);
                Assert.IsNotNull(environment.addedDefines);
                Assert.IsNotNull(environment.configKeys);
            }
        }
    }
}
