using System.IO;
using NUnit.Framework;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    /// <summary>
    /// Guards on where a build may write or delete.
    ///
    /// These are the highest consequence checks in the package: a template that resolves into
    /// <c>Assets</c> makes Unity import the entire player, and a clean step aimed at the project
    /// root deletes the project. Both are far more likely in a large project, where output
    /// templates are long, token-heavy and edited by several people.
    /// </summary>
    [TestFixture]
    internal sealed class PathSafetyTests
    {
        private static string Root => ProjectPaths.ProjectRoot;

        [TestCase("Assets")]
        [TestCase("Assets/Builds")]
        [TestCase("Library")]
        [TestCase("Library/Bee")]
        [TestCase("Packages")]
        [TestCase("ProjectSettings")]
        [TestCase("UserSettings")]
        public void ProtectedFolders_AreRefused(string relative)
        {
            var path = Path.Combine(Root, relative);

            Assert.IsTrue(ProjectPaths.IsProtectedOutputPath(path, out var reason),
                $"'{relative}' should be refused as a build output.");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void ProjectRoot_IsRefused()
        {
            Assert.IsTrue(ProjectPaths.IsProtectedOutputPath(Root, out var reason));
            StringAssert.Contains("project root", reason);
        }

        [Test]
        public void AncestorOfTheProject_IsRefused()
        {
            // Cleaning here would delete the project along with everything beside it.
            var parent = Directory.GetParent(Root)?.FullName;
            Assume.That(parent, Is.Not.Null);

            Assert.IsTrue(ProjectPaths.IsProtectedOutputPath(parent, out var reason));
            StringAssert.Contains("contains the project", reason);
        }

        [Test]
        public void EmptyPath_IsRefused()
        {
            Assert.IsTrue(ProjectPaths.IsProtectedOutputPath(string.Empty, out _));
            Assert.IsTrue(ProjectPaths.IsProtectedOutputPath(null, out _));
            Assert.IsTrue(ProjectPaths.IsProtectedOutputPath("   ", out _));
        }

        [TestCase("Builds")]
        [TestCase("Builds/prod/Android")]
        [TestCase("BuildOutput")]
        public void OrdinaryOutputFolders_AreAllowed(string relative)
        {
            var path = Path.Combine(Root, relative);
            Assert.IsFalse(ProjectPaths.IsProtectedOutputPath(path, out var reason), reason);
        }

        [TestCase("Temp")]
        [TestCase("Temp/Scratch")]
        [TestCase("obj")]
        [TestCase("Logs")]
        public void ScratchFolders_AreAllowedButFlagged(string relative)
        {
            // Unity clears these, so they are a poor choice — but CI legitimately builds into
            // throwaway locations, so refusing outright would block real workflows.
            var path = Path.Combine(Root, relative);

            Assert.IsFalse(ProjectPaths.IsProtectedOutputPath(path, out _), "Scratch folders must not be refused.");
            Assert.IsTrue(ProjectPaths.IsDiscouragedOutputPath(path, out var reason), "…but they should warn.");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void OrdinaryBuildFolders_AreNotEvenFlagged()
        {
            Assert.IsFalse(ProjectPaths.IsDiscouragedOutputPath(Path.Combine(Root, "Builds/prod"), out _));
        }

        [Test]
        public void FoldersOutsideTheProject_AreAllowed()
        {
            Assert.IsFalse(ProjectPaths.IsProtectedOutputPath(Path.Combine(Path.GetTempPath(), "bmk"), out _));
        }

        [Test]
        public void FolderNamesThatMerelyStartWithAProtectedName_AreAllowed()
        {
            // "AssetsBackup" is not "Assets"; a naive StartsWith check would reject it.
            Assert.IsFalse(ProjectPaths.IsProtectedOutputPath(Path.Combine(Root, "AssetsBackup"), out _));
            Assert.IsFalse(ProjectPaths.IsProtectedOutputPath(Path.Combine(Root, "Library2"), out _));
        }

        [Test]
        public void MakeAbsolute_CollapsesParentSegments()
        {
            // Without collapsing, a "../.." template would pass the containment checks while
            // actually writing outside the project.
            var escaped = ProjectPaths.MakeAbsolute("Builds/../../elsewhere");

            StringAssert.DoesNotContain("..", escaped);
            Assert.IsFalse(ProjectPaths.IsSameOrUnder(escaped, Root),
                "A path that climbs above the project must not report as inside it.");
        }

        [Test]
        public void MakeAbsolute_KeepsPathsInsideTheProjectInside()
        {
            var inside = ProjectPaths.MakeAbsolute("Builds/prod/../dev");

            Assert.IsTrue(ProjectPaths.IsSameOrUnder(inside, Root));
            StringAssert.EndsWith("Builds/dev", inside);
        }

        [Test]
        public void IsSameOrUnder_DoesNotFallForPrefixes()
        {
            Assert.IsTrue(ProjectPaths.IsSameOrUnder("/a/b/c", "/a/b"));
            Assert.IsTrue(ProjectPaths.IsSameOrUnder("/a/b", "/a/b"));

            // "/a/bc" is not under "/a/b" even though the string starts with it.
            Assert.IsFalse(ProjectPaths.IsSameOrUnder("/a/bc", "/a/b"));
            Assert.IsFalse(ProjectPaths.IsSameOrUnder("/a", "/a/b"));
            Assert.IsFalse(ProjectPaths.IsSameOrUnder(null, "/a"));
            Assert.IsFalse(ProjectPaths.IsSameOrUnder("/a", null));
        }

        [Test]
        public void Normalize_HandlesTrailingSeparatorsAndBackslashes()
        {
            Assert.AreEqual("/a/b", ProjectPaths.Normalize("/a/b/"));
            Assert.AreEqual("/a/b", ProjectPaths.Normalize("\\a\\b"));
            Assert.AreEqual(string.Empty, ProjectPaths.Normalize(null));
        }

        [Test]
        public void PathLengthCeilings_AreOrdered()
        {
            Assert.Less(ProjectPaths.MaxRecommendedPathLength, ProjectPaths.MaxPathLength);
            Assert.Less(ProjectPaths.MaxPathLength, 260, "Must stay under the Windows MAX_PATH limit.");
        }

        [Test]
        public void NearestExistingDirectory_ReturnsThePathItselfWhenItExists()
        {
            Assert.AreEqual(ProjectPaths.Normalize(Root), ProjectPaths.NearestExistingDirectory(Root));
        }

        /// <summary>
        /// The case the "open output folder" command exists for: a template that resolves several
        /// folders deep into somewhere nothing has been built yet.
        /// </summary>
        [Test]
        public void NearestExistingDirectory_ClimbsToTheDeepestFolderThatExists()
        {
            var missing = Path.Combine(Root, "Temp", "bmk-no-such-build", "Android", "prod");

            Assert.AreEqual(
                ProjectPaths.Normalize(Path.Combine(Root, "Temp")),
                ProjectPaths.NearestExistingDirectory(missing));
        }

        [Test]
        public void NearestExistingDirectory_CreatesNothing()
        {
            var missing = Path.Combine(Root, "Temp", "bmk-no-such-build", "Android");

            ProjectPaths.NearestExistingDirectory(missing);

            Assert.IsFalse(Directory.Exists(missing), "Revealing a folder must never create one.");
        }

        [Test]
        public void NearestExistingDirectory_HandlesNothingUsableWithoutLooping()
        {
            Assert.IsNull(ProjectPaths.NearestExistingDirectory(null));
            Assert.IsNull(ProjectPaths.NearestExistingDirectory(string.Empty));
            Assert.IsNull(ProjectPaths.NearestExistingDirectory("   "));
        }
    }

    /// <summary>
    /// The two file-touching steps must refuse to operate on protected locations, whatever the
    /// tokens in their templates resolve to.
    /// </summary>
    [TestFixture]
    internal sealed class DestructiveStepSafetyTests
    {
        private BuildContext m_Context;

        [SetUp]
        public void SetUp()
        {
            m_Context = new BuildContext(new BuildLog { MirrorToConsole = false })
            {
                OutputDirectory = Path.Combine(ProjectPaths.ProjectRoot, "BuildsForTests/BmkSafetyTests"),
                OutputPath = Path.Combine(ProjectPaths.ProjectRoot, "BuildsForTests/BmkSafetyTests/Player")
            };

            m_Context.RefreshTokens();
        }

        [TestCase("Assets")]
        [TestCase("Assets/Art")]
        [TestCase("Library")]
        [TestCase("Packages")]
        [TestCase("ProjectSettings")]
        [TestCase("UserSettings")]
        public void CleanOutputStep_RefusesProtectedFolders(string relative)
        {
            var step = CreateCleanStep(relative, allowOutsideProject: true);

            var exception = Assert.Throws<BuildStepException>(() => step.Execute(m_Context));
            StringAssert.Contains("Refusing to delete", exception.Message);
        }

        [Test]
        public void CleanOutputStep_RefusesTheProjectRoot()
        {
            var step = CreateCleanStep(ProjectPaths.ProjectRoot, allowOutsideProject: true);

            var exception = Assert.Throws<BuildStepException>(() => step.Execute(m_Context));
            StringAssert.Contains("project root", exception.Message);
        }

        [Test]
        public void CleanOutputStep_RefusesPathsThatClimbOutOfTheProject()
        {
            // "Allow outside project" is off, and the ".." must be collapsed before the check.
            var step = CreateCleanStep("Builds/../../somewhere-else", allowOutsideProject: false);

            var exception = Assert.Throws<BuildStepException>(() => step.Execute(m_Context));
            StringAssert.Contains("outside the project", exception.Message);
        }

        [Test]
        public void CleanOutputStep_AllowsAnOrdinaryBuildFolder()
        {
            var step = CreateCleanStep("BuildsForTests/BmkSafetyTests/DoesNotExist", allowOutsideProject: false);

            // Nothing to delete, but importantly it must not throw.
            Assert.DoesNotThrow(() => step.Execute(m_Context));
        }

        [Test]
        public void CopyFilesStep_RejectsADestinationThatEscapesTheOutputFolder()
        {
            var step = CreateCopyStep("Config", "../../../escaped");
            var report = new BuildValidationReport();

            step.Validate(m_Context, report);

            Assert.IsTrue(report.HasErrors, "A destination climbing out of the output folder must be an error.");
        }

        [Test]
        public void CopyFilesStep_RejectsADestinationInsideAssets()
        {
            var step = CreateCopyStep("Config", Path.Combine(ProjectPaths.ProjectRoot, "Assets/Injected"));
            var report = new BuildValidationReport();

            step.Validate(m_Context, report);

            Assert.IsTrue(report.HasErrors);
        }

        [Test]
        public void CopyFilesStep_AcceptsANormalRelativeDestination()
        {
            var step = CreateCopyStep("Config", "Config");
            var report = new BuildValidationReport();

            step.Validate(m_Context, report);

            Assert.IsFalse(report.HasErrors, report.ToString());
        }

        private static CleanOutputStep CreateCleanStep(string folder, bool allowOutsideProject)
        {
            var step = new CleanOutputStep();
            SetPrivate(step, "m_FolderOverride", folder);
            SetPrivate(step, "m_AllowOutsideProject", allowOutsideProject);
            return step;
        }

        private static CopyFilesStep CreateCopyStep(string source, string destination)
        {
            var step = new CopyFilesStep();
            SetPrivate(step, "m_Source", source);
            SetPrivate(step, "m_Destination", destination);
            return step;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.IsNotNull(info, $"Field '{field}' no longer exists on {target.GetType().Name}.");
            info.SetValue(target, value);
        }
    }
}
