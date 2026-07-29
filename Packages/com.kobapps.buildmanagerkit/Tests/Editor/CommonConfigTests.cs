using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    /// <summary>
    /// The common configuration on the settings asset, the environments that override parts of it,
    /// and the precedence between profile, environment and common.
    ///
    /// These are the rules a wrong answer breaks quietly — a staging build that ships the production
    /// bundle identifier looks fine until it reaches a store — so each level of the chain is asserted
    /// on its own rather than through a whole build.
    /// </summary>
    [TestFixture]
    internal sealed class CommonConfigTests
    {
        private readonly List<UnityEngine.Object> m_Created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in m_Created)
                UnityEngine.Object.DestroyImmediate(asset);

            m_Created.Clear();
        }

        // ---------------------------------------------------------------- player settings

        [Test]
        public void AnEnvironmentTakesTheCommonValues()
        {
            var settings = CreateSettings();
            var stage = AddEnvironment(settings, "stage");

            settings.Common.companyName = "Kobapps";
            settings.Common.applicationIdentifier = "com.kobapps.game";

            Assert.AreEqual("Kobapps", ConfigResolver.ResolveCompanyName(settings, stage));
            Assert.AreEqual("com.kobapps.game", ConfigResolver.ResolveApplicationIdentifier(settings, stage));
        }

        [Test]
        public void AnEnvironmentsOwnValueWinsOverTheCommonOne()
        {
            var settings = CreateSettings();
            var stage = AddEnvironment(settings, "stage");

            settings.Common.applicationIdentifier = "com.kobapps.game";
            SetValue(stage, "m_ApplicationIdentifier", "com.kobapps.game.stage");

            Assert.AreEqual("com.kobapps.game.stage",
                ConfigResolver.ResolveApplicationIdentifier(settings, stage));
        }

        [Test]
        public void WithNothingSharedNothingIsResolved()
        {
            var settings = CreateSettings();
            var stage = AddEnvironment(settings, "stage");

            Assert.IsNull(ConfigResolver.ResolveCompanyName(settings, stage));
            Assert.IsNull(ConfigResolver.ResolveProductName(settings, stage));
            Assert.IsNull(ConfigResolver.ResolveApplicationIdentifier(settings, stage));
            Assert.IsNull(ConfigResolver.ResolveApplicationIcon(settings, stage));
        }

        [Test]
        public void AValueSetOnlyByTheCommonConfigurationSurvivesAMissingEnvironment()
        {
            // The Editor with no active environment still has to resolve the shared values, which is
            // what the header and the dashboard read.
            var settings = CreateSettings();
            settings.Common.productName = "Game";

            Assert.AreEqual("Game", ConfigResolver.ResolveProductName(settings, null));
        }

        [Test]
        public void ClearingAnEnvironmentsFieldGoesBackToTheCommonValue()
        {
            // The whole point of dropping the override checkboxes: an empty field means "use the
            // shared value", so clearing one is how an override is undone.
            var settings = CreateSettings();
            var qa = AddEnvironment(settings, "qa");

            settings.Common.productName = "Game";
            SetValue(qa, "m_ProductName", "Game QA");

            Assert.AreEqual("Game QA", ConfigResolver.ResolveProductName(settings, qa));

            SetValue(qa, "m_ProductName", string.Empty);
            Assert.AreEqual("Game", ConfigResolver.ResolveProductName(settings, qa));
        }

        [Test]
        public void AWhitespaceOnlyFieldCountsAsEmpty()
        {
            // Otherwise a stray space would silently override the shared value with nothing.
            var settings = CreateSettings();
            var qa = AddEnvironment(settings, "qa");

            settings.Common.companyName = "Kobapps";
            SetValue(qa, "m_CompanyName", "   ");

            Assert.AreEqual("Kobapps", ConfigResolver.ResolveCompanyName(settings, qa));
        }

        [Test]
        public void AnEnvironmentIconOverridesTheSharedOne()
        {
            var settings = CreateSettings();
            var qa = AddEnvironment(settings, "qa");

            var shared = CreateTexture();
            var own = CreateTexture();
            settings.Common.applicationIcon = shared;

            Assert.AreSame(shared, ConfigResolver.ResolveApplicationIcon(settings, qa));

            var serialized = new SerializedObject(qa);
            serialized.FindProperty("m_ApplicationIcon").objectReferenceValue = own;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreSame(own, ConfigResolver.ResolveApplicationIcon(settings, qa));
        }

        [Test]
        public void ForceDevelopmentBuildFallsBackToTheCommonConfiguration()
        {
            var settings = CreateSettings();
            var stage = AddEnvironment(settings, "stage");

            settings.Common.forceDevelopmentBuild = OptionalBool.Enabled;

            Assert.AreEqual(OptionalBool.Enabled, ConfigResolver.ResolveForceDevelopmentBuild(settings, stage));

            SetEnum(stage, "m_ForceDevelopmentBuild", (int)OptionalBool.Disabled);
            Assert.AreEqual(OptionalBool.Disabled, ConfigResolver.ResolveForceDevelopmentBuild(settings, stage));
        }

        // ---------------------------------------------------------------- variables

        [Test]
        public void VariablesMergeWithTheEnvironmentWinningOnAKey()
        {
            var settings = CreateSettings();
            var prod = AddEnvironment(settings, "prod");

            settings.Common.variables.Add(new BuildVariable("api_url", "https://dev.example"));
            settings.Common.variables.Add(new BuildVariable("tier", "shared"));
            SetVariables(prod, ("api_url", "https://prod.example"));

            var resolved = ConfigResolver.ResolveVariables(settings, prod);

            Assert.AreEqual(2, resolved.Count, "Shared keys must not be duplicated.");
            Assert.AreEqual("https://prod.example", resolved.First(v => v.key == "api_url").value);
            Assert.AreEqual("shared", resolved.First(v => v.key == "tier").value);
        }

        [Test]
        public void SharedVariablesReachAnEnvironmentThatDeclaresNone()
        {
            var settings = CreateSettings();
            var qa = AddEnvironment(settings, "qa");

            settings.Common.variables.Add(new BuildVariable("log_level", "verbose"));

            var resolved = ConfigResolver.ResolveVariables(settings, qa);

            Assert.AreEqual(1, resolved.Count);
            Assert.AreEqual("verbose", resolved[0].value);
        }

        // ---------------------------------------------------------------- versioning

        [Test]
        public void VersioningComesFromTheCommonConfigurationWhenNothingElseClaimsIt()
        {
            var settings = CreateSettings();
            var stage = AddEnvironment(settings, "stage");
            var profile = AddProfile(settings, "android");

            settings.Common.versioning.manageVersion = true;
            settings.Common.versioning.source = VersionSource.Profile;
            settings.Common.versioning.version = "2.0.0";

            var resolved = ConfigResolver.ResolveVersioning(settings, stage, profile);

            Assert.AreSame(settings, resolved.Owner, "The settings asset owns the common counter.");
            Assert.AreEqual("2.0.0", resolved.Config.version);
            StringAssert.Contains("common configuration", resolved.OwnerLabel);
        }

        [Test]
        public void AnEnvironmentsVersioningWinsOverTheCommonOne()
        {
            var settings = CreateSettings();
            var stage = AddEnvironment(settings, "stage");

            settings.Common.versioning.version = "2.0.0";
            SetVersioning(stage, config => config.version = "2.1.0-rc");

            var resolved = ConfigResolver.ResolveVersioning(settings, stage, null);

            Assert.AreSame(stage, resolved.Owner);
            Assert.AreEqual("2.1.0-rc", resolved.Config.version);
        }

        [Test]
        public void AProfilesVersioningWinsOverTheEnvironmentsAndTheCommonOne()
        {
            var settings = CreateSettings();
            var stage = AddEnvironment(settings, "stage");
            var profile = AddProfile(settings, "android");

            settings.Common.versioning.version = "2.0.0";
            SetVersioning(stage, config => config.version = "2.1.0-rc");
            SetVersioning(profile, config => config.version = "9.9.9");

            var resolved = ConfigResolver.ResolveVersioning(settings, stage, profile);

            Assert.AreSame(profile, resolved.Owner);
            Assert.AreEqual("9.9.9", resolved.Config.version);
        }

        [Test]
        public void WithoutASettingsAssetNothingIsManaged()
        {
            var resolved = ConfigResolver.ResolveVersioning(null, null, null);

            Assert.IsFalse(resolved.IsOwned);
            Assert.IsFalse(resolved.Config.manageVersion);
            Assert.IsFalse(resolved.Config.manageBuildNumber);
        }

        [Test]
        public void AnUnmanagedVersionResolvesToWhateverThePlayerSettingsHold()
        {
            var config = VersioningConfig.Unmanaged;
            config.source = VersionSource.Profile;
            config.version = "5.5.5";

            Assert.AreEqual(PlayerSettings.bundleVersion, VersionService.Resolve(config, GitInfo.None, null),
                "A block that does not manage the version must not impose one.");

            Assert.AreEqual(PlayerSettings.Android.bundleVersionCode,
                VersionService.ResolveBuildNumber(config, GitInfo.None),
                "A block that does not manage the build number must resolve to the current one.");
        }

        [Test]
        public void AVersionFileIsReadWhenTheToggleIsOn()
        {
            var path = Path.Combine("Temp", "bmk-version-test.txt");
            var absolute = ProjectPaths.MakeAbsolute(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllText(absolute, "\n  4.5.6  \n");

            try
            {
                var config = new VersioningConfig
                {
                    manageVersion = true,
                    useVersionFile = true,
                    versionFilePath = path,
                    source = VersionSource.Profile,
                    version = "ignored"
                };

                Assert.AreEqual("4.5.6", VersionService.Resolve(config, GitInfo.None, null),
                    "The file wins over the source, and its first non-empty line is trimmed.");

                VersionService.WriteVersionFile(config, "4.5.7");
                StringAssert.Contains("4.5.7", File.ReadAllText(absolute));
            }
            finally
            {
                File.Delete(absolute);
            }
        }

        [Test]
        public void AMissingVersionFileFallsBackInsteadOfFailing()
        {
            var config = new VersioningConfig
            {
                manageVersion = true,
                useVersionFile = true,
                versionFilePath = "Temp/bmk-no-such-version-file.txt"
            };

            Assert.AreEqual(PlayerSettings.bundleVersion, VersionService.Resolve(config, GitInfo.None, null));
        }

        [Test]
        public void WriteVersionFileDoesNothingWhenTheFileIsSwitchedOff()
        {
            var path = Path.Combine("Temp", "bmk-unused-version.txt");
            var absolute = ProjectPaths.MakeAbsolute(path);
            File.Delete(absolute);

            VersionService.WriteVersionFile(
                new VersioningConfig { manageVersion = true, useVersionFile = false, versionFilePath = path },
                "1.0.0");

            Assert.IsFalse(File.Exists(absolute), "A version file that is not in use must not be created.");
        }

        [Test]
        public void TheLegacyVersionFileSourceStillReadsTheFile()
        {
            // Belt and braces: an asset that somehow never migrated must still read its file.
            var config = new VersioningConfig
            {
                manageVersion = true,
                useVersionFile = false,
                source = VersionSource.VersionFile,
                versionFilePath = "Temp/bmk-legacy-version.txt"
            };

            Assert.IsTrue(config.ReadsVersionFile);
        }

        // ---------------------------------------------------------------- the build counter

        [Test]
        public void TheCounterIsIncrementedOnTheAssetThatOwnsIt()
        {
            var settings = CreateSettings();
            var profile = AddProfile(settings, "android");

            settings.Common.versioning.manageBuildNumber = true;
            settings.Common.versioning.buildNumberPolicy = BuildNumberPolicy.AutoIncrementOnSuccess;
            settings.Common.versioning.buildNumber = 41;

            var context = CreateContext(settings, profile, null);

            Assert.AreEqual(42, VersionService.CommitBuildNumber(context));
            Assert.AreEqual(42, settings.Common.versioning.buildNumber, "The owning asset holds the counter.");
            Assert.AreEqual(1, profile.Versioning.buildNumber, "The profile's own counter is untouched.");
        }

        [Test]
        public void AProfileThatVersionsItselfKeepsItsOwnCounter()
        {
            var settings = CreateSettings();
            var profile = AddProfile(settings, "android");

            settings.Common.versioning.buildNumber = 41;
            SetVersioning(profile, config =>
            {
                config.buildNumberPolicy = BuildNumberPolicy.AutoIncrementOnSuccess;
                config.buildNumber = 7;
            });

            var context = CreateContext(settings, profile, null);

            Assert.AreEqual(8, VersionService.CommitBuildNumber(context));
            Assert.AreEqual(41, settings.Common.versioning.buildNumber, "The shared counter must not move.");
        }

        [Test]
        public void TheCounterIsLeftAloneWhenThePolicyDoesNotIncrement()
        {
            var settings = CreateSettings();
            var profile = AddProfile(settings, "android");

            SetVersioning(profile, config =>
            {
                config.manageBuildNumber = true;
                config.buildNumberPolicy = BuildNumberPolicy.Manual;
                config.buildNumber = 12;
            });

            var context = CreateContext(settings, profile, null);

            Assert.AreEqual(12, VersionService.CommitBuildNumber(context));
            Assert.AreEqual(12, profile.Versioning.buildNumber);
        }

        [Test]
        public void TheCounterIsLeftAloneWhenTheBuildNumberIsNotManaged()
        {
            var settings = CreateSettings();
            var profile = AddProfile(settings, "android");

            SetVersioning(profile, config =>
            {
                config.manageBuildNumber = false;
                config.buildNumberPolicy = BuildNumberPolicy.AutoIncrementOnSuccess;
                config.buildNumber = 7;
            });

            var context = CreateContext(settings, profile, null);

            VersionService.CommitBuildNumber(context);
            Assert.AreEqual(7, profile.Versioning.buildNumber);
        }

        [Test]
        public void TheCounterIsLeftAloneWhenTheNumberWasSuppliedByTheCaller()
        {
            // CI passing -bmkBuildNumber owns the numbering for that run, so the stored counter did
            // not produce it and must not drift away from what was shipped.
            var settings = CreateSettings();
            var profile = AddProfile(settings, "android");

            SetVersioning(profile, config =>
            {
                config.manageBuildNumber = true;
                config.buildNumberPolicy = BuildNumberPolicy.AutoIncrementOnSuccess;
                config.buildNumber = 5;
            });

            var context = CreateContext(settings, profile, null);
            context.BuildNumberWasSupplied = true;

            Assert.AreEqual(5, VersionService.CommitBuildNumber(context));
            Assert.AreEqual(5, profile.Versioning.buildNumber);
        }

        // ---------------------------------------------------------------- migration

        [Test]
        public void APreMigrationProfileKeepsItsOwnVersioning()
        {
            var settings = CreateSettings();
            var profile = AddProfile(settings, "legacy");

            // Exactly what a 1.1 asset holds on disk: the flat fields set and the migration flag
            // absent, which is what the migration has to recognise.
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_VersioningMigrated").boolValue = false;
            serialized.FindProperty("m_OverrideVersioning").boolValue = false;
            serialized.FindProperty("m_VersionSource").intValue = (int)VersionSource.Profile;
            serialized.FindProperty("m_Version").stringValue = "3.4.5";
            serialized.FindProperty("m_BuildNumberPolicy").intValue = (int)BuildNumberPolicy.AutoIncrementOnSuccess;
            serialized.FindProperty("m_BuildNumber").intValue = 118;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            profile.MigrateVersioning();

            Assert.IsTrue(profile.OverridesVersioning,
                "A profile that versioned itself must keep doing so rather than start sharing.");

            Assert.IsTrue(profile.Versioning.manageVersion);
            Assert.AreEqual(VersionSource.Profile, profile.Versioning.source);
            Assert.AreEqual("3.4.5", profile.Versioning.version);
            Assert.AreEqual(118, profile.Versioning.buildNumber, "The build counter must survive the migration.");
            Assert.AreEqual(BuildNumberPolicy.AutoIncrementOnSuccess, profile.Versioning.buildNumberPolicy);
        }

        [Test]
        public void ALegacyVersionFileSourceBecomesTheVersionFileToggle()
        {
            var settings = CreateSettings();
            var profile = AddProfile(settings, "legacy-file");

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_VersioningMigrated").boolValue = false;
            serialized.FindProperty("m_VersionSource").intValue = (int)VersionSource.VersionFile;
            serialized.FindProperty("m_VersionFilePath").stringValue = "release/version.txt";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            profile.MigrateVersioning();

            Assert.IsTrue(profile.Versioning.useVersionFile);
            Assert.AreEqual("release/version.txt", profile.Versioning.versionFilePath);
            Assert.IsTrue(profile.Versioning.ReadsVersionFile);
        }

        [Test]
        public void MigrationRunsOnceAndThenLeavesTheAssetAlone()
        {
            var settings = CreateSettings();
            var profile = AddProfile(settings, "once");

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_VersioningMigrated").boolValue = false;
            serialized.FindProperty("m_Version").stringValue = "1.0.0";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            profile.MigrateVersioning();
            profile.Versioning.version = "edited-after-migration";
            profile.MigrateVersioning();

            Assert.AreEqual("edited-after-migration", profile.Versioning.version,
                "A second migration would overwrite edits made since the first.");
        }

        [Test]
        public void AnUncheckedOverrideIsClearedWhenTheCheckboxesGoAway()
        {
            // A pre-1.2 environment could hold a value with its box unchecked — abandoned text that
            // contributed nothing. Left in place it would start overriding the shared value the moment
            // the boxes disappeared.
            var settings = CreateSettings();
            var qa = AddEnvironment(settings, "qa");
            settings.Common.productName = "Game";

            var serialized = new SerializedObject(qa);
            serialized.FindProperty("m_OverridesMigrated").boolValue = false;
            serialized.FindProperty("m_OverrideProductName").boolValue = false;
            serialized.FindProperty("m_ProductName").stringValue = "Abandoned";
            serialized.FindProperty("m_OverrideCompanyName").boolValue = true;
            serialized.FindProperty("m_CompanyName").stringValue = "Kept";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            qa.MigrateOverrides();

            Assert.IsNull(qa.ProductNameOverride, "An unchecked override must not survive as a value.");
            Assert.AreEqual("Game", ConfigResolver.ResolveProductName(settings, qa));
            Assert.AreEqual("Kept", qa.CompanyNameOverride, "A checked override keeps its value.");
        }

        [Test]
        public void OverrideMigrationRunsOnceAndThenLeavesTheAssetAlone()
        {
            var settings = CreateSettings();
            var qa = AddEnvironment(settings, "qa");

            var serialized = new SerializedObject(qa);
            serialized.FindProperty("m_OverridesMigrated").boolValue = false;
            serialized.FindProperty("m_OverrideProductName").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            qa.MigrateOverrides();
            SetValue(qa, "m_ProductName", "Typed afterwards");
            qa.MigrateOverrides();

            Assert.AreEqual("Typed afterwards", qa.ProductNameOverride,
                "A second migration would clear a value the user has since typed.");
        }

        [Test]
        public void AFreshlyCreatedEnvironmentNeedsNoOverrideMigration()
        {
            var environment = ScriptableObject.CreateInstance<BuildEnvironment>();
            environment.hideFlags = HideFlags.HideAndDontSave;
            m_Created.Add(environment);

            var serialized = new SerializedObject(environment);
            serialized.FindProperty("m_ProductName").stringValue = "Typed straight away";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            environment.MigrateOverrides();

            Assert.AreEqual("Typed straight away", environment.ProductNameOverride,
                "A new environment has no checkboxes to migrate, so its value must stand.");
        }

        [Test]
        public void AFreshlyCreatedProfileTakesTheCommonVersioning()
        {
            // A new instance is not a pre-1.2 asset: their field values are identical, so this is the
            // case that would break if the migration keyed off the data instead of Awake.
            var profile = ScriptableObject.CreateInstance<BuildTargetProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            m_Created.Add(profile);

            Assert.IsFalse(profile.OverridesVersioning,
                "A profile created from the Assets menu must share the project's versioning.");
        }

        // ---------------------------------------------------------------- health check

        [Test]
        public void DuplicateSharedVariableKeysAreAWarning()
        {
            var settings = CreateSettings();
            settings.Common.variables.Add(new BuildVariable("api_url", "a"));
            settings.Common.variables.Add(new BuildVariable("API_URL", "b"));

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.Issues.Any(issue => issue.Message.Contains("declared twice")));
        }

        [Test]
        public void AMissingCommonVersionFileIsAWarning()
        {
            var settings = CreateSettings();
            settings.Common.versioning.manageVersion = true;
            settings.Common.versioning.useVersionFile = true;
            settings.Common.versioning.versionFilePath = "Temp/bmk-absent-version.txt";

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsTrue(report.Issues.Any(issue => issue.Message.Contains("does not exist")));
        }

        [Test]
        public void ACleanCommonConfigurationIsNotReported()
        {
            var settings = CreateSettings();
            settings.Common.companyName = "Kobapps";

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsFalse(report.Issues.Any(issue => issue.Message.Contains("common")),
                string.Join("\n", report.Issues.Select(issue => issue.ToString())));
        }

        // ---------------------------------------------------------------- deleting a profile

        [Test]
        public void DeletingAProfileUnregistersItAndClearsTheQueuesThatUsedIt()
        {
            var settings = CreateSettings();
            var android = AddProfile(settings, "android");
            var windows = AddProfile(settings, "windows");

            var queue = new BuildQueue { id = "nightly" };
            queue.entries.Add(new BuildQueueEntry { profile = android });
            queue.entries.Add(new BuildQueueEntry { profile = windows });
            settings.QueuesMutable.Add(queue);

            Assert.IsTrue(BuildManagerBootstrap.DeleteProfile(settings, android));

            CollectionAssert.DoesNotContain(settings.Profiles, android);
            Assert.AreEqual(1, queue.entries.Count, "A queue entry pointing at a deleted profile has to go too.");
            Assert.AreSame(windows, queue.entries[0].profile);
        }

        [Test]
        public void DeletingNothingIsRefusedRatherThanThrowing()
        {
            Assert.IsFalse(BuildManagerBootstrap.DeleteProfile(CreateSettings(), null));
            Assert.IsFalse(BuildManagerBootstrap.DeleteProfile(null, null));
        }

        [Test]
        public void DeletingAProfileLeavesTheHealthCheckClean()
        {
            var settings = CreateSettings();
            var android = AddProfile(settings, "android");
            AddProfile(settings, "windows");

            var queue = new BuildQueue { id = "nightly" };
            queue.entries.Add(new BuildQueueEntry { profile = android });
            settings.QueuesMutable.Add(queue);

            BuildManagerBootstrap.DeleteProfile(settings, android);

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsFalse(report.Issues.Any(issue => issue.Message.Contains("deleted asset")),
                "Deleting must not leave a dangling slot behind: " +
                string.Join("\n", report.Issues.Select(issue => issue.ToString())));
        }

        // ---------------------------------------------------------------- deleting an environment

        [Test]
        public void DeletingAnEnvironmentClearsEveryReferenceToIt()
        {
            var settings = CreateSettings();
            var qa = AddEnvironment(settings, "qa");
            var prod = AddEnvironment(settings, "prod");
            var profile = AddProfile(settings, "android");

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_DefaultEnvironment").objectReferenceValue = qa;
            var allowed = serialized.FindProperty("m_AllowedEnvironments");
            allowed.arraySize = 2;
            allowed.GetArrayElementAtIndex(0).objectReferenceValue = qa;
            allowed.GetArrayElementAtIndex(1).objectReferenceValue = prod;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var queue = new BuildQueue { id = "nightly", defaultEnvironment = qa };
            queue.entries.Add(new BuildQueueEntry { profile = profile, environmentOverride = qa });
            settings.QueuesMutable.Add(queue);

            Assert.IsTrue(BuildManagerBootstrap.DeleteEnvironment(settings, qa));

            CollectionAssert.DoesNotContain(settings.Environments, qa);
            Assert.IsNull(profile.DefaultEnvironment, "A dangling default environment fails every build.");
            CollectionAssert.DoesNotContain(profile.AllowedEnvironments, qa);
            CollectionAssert.Contains(profile.AllowedEnvironments, prod, "Other entries must survive.");
            Assert.IsNull(queue.defaultEnvironment);
            Assert.IsNull(queue.entries[0].environmentOverride);
            Assert.AreSame(profile, queue.entries[0].profile, "Only the environment reference is cleared.");
        }

        [Test]
        public void DeletingAnEnvironmentLeavesTheCommonConfigurationAlone()
        {
            var settings = CreateSettings();
            var qa = AddEnvironment(settings, "qa");
            AddEnvironment(settings, "prod");

            settings.Common.companyName = "Kobapps";

            BuildManagerBootstrap.DeleteEnvironment(settings, qa);

            Assert.AreEqual("Kobapps", ConfigResolver.ResolveCompanyName(settings, null),
                "The shared values belong to the project, not to any one environment.");
        }

        [Test]
        public void DeletingAnEnvironmentLeavesTheHealthCheckClean()
        {
            var settings = CreateSettings();
            var qa = AddEnvironment(settings, "qa");
            AddEnvironment(settings, "prod");
            var profile = AddProfile(settings, "android");

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_DefaultEnvironment").objectReferenceValue = qa;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            BuildManagerBootstrap.DeleteEnvironment(settings, qa);

            var report = BuildManagerIntegrity.Check(settings);

            Assert.IsFalse(report.Issues.Any(issue => issue.Message.Contains("deleted asset")
                                                      || issue.Message.Contains("does not allow it")),
                string.Join("\n", report.Issues.Select(issue => issue.ToString())));
        }

        [Test]
        public void DeletingNoEnvironmentIsRefusedRatherThanThrowing()
        {
            Assert.IsFalse(BuildManagerBootstrap.DeleteEnvironment(CreateSettings(), null));
            Assert.IsFalse(BuildManagerBootstrap.DeleteEnvironment(null, null));
        }

        // ---------------------------------------------------------------- helpers

        private BuildManagerSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<BuildManagerSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            m_Created.Add(settings);
            return settings;
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

        private BuildTargetProfile AddProfile(BuildManagerSettings settings, string id)
        {
            var profile = ScriptableObject.CreateInstance<BuildTargetProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            profile.name = "Profile_" + id;
            m_Created.Add(profile);

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_Id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            settings.ProfilesMutable.Add(profile);
            return profile;
        }

        /// <summary>
        /// A context that has been through the same versioning resolution a real run performs, which
        /// is what <see cref="VersionService.CommitBuildNumber"/> reads.
        /// </summary>
        private static BuildContext CreateContext(
            BuildManagerSettings settings,
            BuildTargetProfile profile,
            BuildEnvironment environment)
        {
            return new BuildContext(new BuildLog { MirrorToConsole = false })
            {
                Settings = settings,
                Profile = profile,
                Environment = environment,
                ResolvedVersioning = ConfigResolver.ResolveVersioning(settings, environment, profile)
            };
        }

        private static void SetValue(BuildEnvironment environment, string field, string value)
        {
            var serialized = new SerializedObject(environment);
            serialized.FindProperty(field).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private Texture2D CreateTexture()
        {
            var texture = new Texture2D(4, 4) { hideFlags = HideFlags.HideAndDontSave };
            m_Created.Add(texture);
            return texture;
        }

        private static void SetEnum(BuildEnvironment environment, string field, int value)
        {
            var serialized = new SerializedObject(environment);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVariables(BuildEnvironment environment, params (string Key, string Value)[] variables)
        {
            var serialized = new SerializedObject(environment);
            var array = serialized.FindProperty("m_Variables");
            array.arraySize = variables.Length;

            for (var i = 0; i < variables.Length; i++)
            {
                var entry = array.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("key").stringValue = variables[i].Key;
                entry.FindPropertyRelative("value").stringValue = variables[i].Value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Turns the versioning override on and configures the block. The block itself is written
        /// through the object rather than a SerializedObject, because it is the instance the resolver
        /// hands back — so the assertions read what the production code reads.
        /// </summary>
        private static void SetVersioning(BuildEnvironment environment, Action<VersioningConfig> configure)
        {
            var serialized = new SerializedObject(environment);
            serialized.FindProperty("m_OverrideVersioning").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            environment.Versioning.manageVersion = true;
            environment.Versioning.source = VersionSource.Profile;
            configure(environment.Versioning);
        }

        private static void SetVersioning(BuildTargetProfile profile, Action<VersioningConfig> configure)
        {
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_OverrideVersioning").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            profile.Versioning.manageVersion = true;
            profile.Versioning.source = VersionSource.Profile;
            configure(profile.Versioning);
        }
    }
}
