using System;
using System.Collections.Generic;
using NUnit.Framework;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    [TestFixture]
    internal sealed class BuildTokensTests
    {
        private static readonly DateTime k_Timestamp = new DateTime(2026, 7, 29, 14, 5, 9, DateTimeKind.Local);

        private static Dictionary<string, string> Values() => new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["productName"] = "MyGame",
            ["env"] = "prod",
            ["version"] = "1.4.2",
            ["buildNumber"] = "118"
        };

        [Test]
        public void Resolve_ReplacesKnownTokens()
        {
            var result = BuildTokens.Resolve("{productName}_{version}+{buildNumber}_{env}", Values(), k_Timestamp);
            Assert.AreEqual("MyGame_1.4.2+118_prod", result);
        }

        [Test]
        public void Resolve_IsCaseInsensitiveForValues()
        {
            Assert.AreEqual("MyGame", BuildTokens.Resolve("{PRODUCTNAME}", Values(), k_Timestamp));
        }

        [Test]
        public void Resolve_LeavesUnknownTokensIntact()
        {
            // Typos must stay visible rather than silently collapsing to an empty string.
            Assert.AreEqual("{prodcutName}", BuildTokens.Resolve("{prodcutName}", Values(), k_Timestamp));
        }

        [Test]
        public void Resolve_FormatsDateTokensWithDefaults()
        {
            Assert.AreEqual("2026-07-29", BuildTokens.Resolve("{date}", Values(), k_Timestamp));
            Assert.AreEqual("140509", BuildTokens.Resolve("{time}", Values(), k_Timestamp));
            Assert.AreEqual("2026-07-29_140509", BuildTokens.Resolve("{datetime}", Values(), k_Timestamp));
        }

        [Test]
        public void Resolve_HonoursExplicitDateFormats()
        {
            Assert.AreEqual("260729", BuildTokens.Resolve("{date:yyMMdd}", Values(), k_Timestamp));
            Assert.AreEqual("14-05", BuildTokens.Resolve("{time:HH-mm}", Values(), k_Timestamp));
        }

        [Test]
        public void Resolve_HandlesNullAndEmptyInput()
        {
            Assert.AreEqual(string.Empty, BuildTokens.Resolve(null, Values(), k_Timestamp));
            Assert.AreEqual(string.Empty, BuildTokens.Resolve(string.Empty, Values(), k_Timestamp));
        }

        [Test]
        public void Sanitize_CollapsesIllegalCharacters()
        {
            Assert.AreEqual("My_Game_v1", BuildTokens.Sanitize("My Game: v1"));
            Assert.AreEqual("a_b", BuildTokens.Sanitize("a///b"));
            Assert.AreEqual("clean", BuildTokens.Sanitize("  clean  "));
            Assert.AreEqual(string.Empty, BuildTokens.Sanitize(null));
        }

        [TestCase("prod", "prod")]
        [TestCase("my-env", "my_env")]
        [TestCase("my env", "my_env")]
        [TestCase("my.env", "my_env")]
        [TestCase("a--b", "a_b")]
        [TestCase("_leading", "leading")]
        [TestCase("2fast", "_2fast")]
        [TestCase("", "")]
        [TestCase("---", "")]
        public void SanitizeIdentifier_ProducesLegalScriptingSymbols(string input, string expected)
        {
            // A define is pasted straight into PlayerSettings, so anything that is not a legal
            // C# identifier breaks compilation for the whole project.
            var result = BuildTokens.SanitizeIdentifier(input);

            Assert.AreEqual(expected, result);

            if (result.Length > 0)
                Assert.IsTrue(BuildTokens.IsValidIdentifier(result), $"'{result}' is not a legal symbol.");
        }

        [TestCase("PROD", true)]
        [TestCase("ENV_PROD", true)]
        [TestCase("_private", true)]
        [TestCase("with-hyphen", false)]
        [TestCase("with space", false)]
        [TestCase("with.dot", false)]
        [TestCase("2leading", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsValidIdentifier_MatchesTheCompilerRules(string input, bool expected)
        {
            Assert.AreEqual(expected, BuildTokens.IsValidIdentifier(input));
        }

        [Test]
        public void SanitizeIdentifier_DiffersFromFileNameSanitisingWhereItMatters()
        {
            // Sanitize keeps hyphens because they are legal in file names; SanitizeIdentifier
            // must not, because they are not legal in defines.
            Assert.AreEqual("my-env", BuildTokens.Sanitize("my-env"));
            Assert.AreEqual("my_env", BuildTokens.SanitizeIdentifier("my-env"));
        }

        [Test]
        public void GetReferencedTokens_ListsEveryToken()
        {
            CollectionAssert.AreEquivalent(
                new[] { "env", "version" },
                BuildTokens.GetReferencedTokens("Builds/{env}/{version}"));
        }
    }
}
