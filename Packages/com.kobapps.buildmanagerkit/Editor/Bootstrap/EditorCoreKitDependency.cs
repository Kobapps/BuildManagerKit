using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace BuildManagerKit.Bootstrap
{
    /// <summary>
    /// Notices that EditorCoreKit is missing and offers to add it.
    ///
    /// This exists because of one Package Manager rule: a package's own <c>dependencies</c> cannot
    /// name a git URL, so a git-installed package cannot pull in a git-installed dependency. Build
    /// Manager Kit's window is built on EditorCoreKit, which means a project that installs only
    /// this package has half a tool — and used to have a screen of <c>CS0246</c> errors, because
    /// every type the window is made of had vanished.
    ///
    /// The fix has two halves. <c>BuildManagerKit.Editor</c> carries a define constraint, so
    /// without the kit it is not compiled at all rather than compiled and broken. This assembly is
    /// the other half: it carries the opposite constraint, so it exists only while the kit is
    /// missing, and its whole job is to add it. Once the dependency is in place, none of this is
    /// compiled into the project.
    ///
    /// It adds the package rather than asking first, because this is a *required* dependency of a
    /// package the project deliberately installed — the Package Manager would have resolved it
    /// itself if a git URL were allowed to be a dependency, and a tool that sits switched off
    /// behind a dialog nobody read is not better behaved, only quieter.
    /// </summary>
    internal static class EditorCoreKitDependency
    {
        /// <summary>Name of the package this one cannot work without.</summary>
        internal const string PackageName = "com.kobapps.editorcorekit";

        /// <summary>Where the Package Manager should fetch it from.</summary>
        internal const string GitUrl =
            "https://github.com/Kobapps/EditorCoreKit.git?path=Packages/com.kobapps.editorcorekit";

        private const string k_MenuPath = "Tools/Build Manager Kit/Install EditorCoreKit (required)…";
        private const string k_AttemptedKey = "BuildManagerKit.AttemptedEditorCoreKitInstall";

        private static AddRequest s_Request;

        private static string Explanation =>
            "Build Manager Kit's window is built on EditorCoreKit, which is not in this project, so the "
            + "window and everything else in its editor assembly are switched off.\n\n"
            + "EditorCoreKit is distributed over git, and the Package Manager does not allow a package to "
            + "declare a git dependency of its own — only the project manifest may hold one — so it has to "
            + "be added to the project:\n\n"
            + GitUrl;

        [InitializeOnLoadMethod]
        private static void Check()
        {
            // The constraint on this assembly already means the kit was absent when scripts were
            // compiled. Checking the registry as well covers the moment after it has been added,
            // while this now-stale assembly is still loaded.
            if (IsInstalled())
                return;

            // Once per session: a domain reload happens on every script change, and re-running a
            // git fetch on each of them would make every recompile slow while it kept failing. A
            // later session tries again, which is what makes it recover once the network is back.
            if (SessionState.GetBool(k_AttemptedKey, false))
                return;

            SessionState.SetBool(k_AttemptedKey, true);

            // A build server gets the explanation but no package added behind its back: CI resolves
            // what its manifest says, and a build that quietly rewrites its own dependencies is not
            // reproducible.
            if (Application.isBatchMode)
            {
                Debug.LogError("[BuildManagerKit] " + Explanation);
                return;
            }

            Debug.Log($"[BuildManagerKit] {Explanation}\n\nAdding it now.");

            // Deferred: starting package work while the domain is still loading is how an Editor
            // ends up wedged at startup.
            EditorApplication.delayCall += Install;
        }

        [MenuItem(k_MenuPath, priority = 0)]
        private static void InstallFromMenu()
        {
            if (IsInstalled())
            {
                Debug.Log("[BuildManagerKit] EditorCoreKit is already installed.");
                return;
            }

            Install();
        }

        /// <summary>
        /// Adds the package. Nothing is written to the manifest by this code itself — the Package
        /// Manager owns that file, and a hand-edited manifest is how a project ends up with a
        /// dependency its lock file does not know about.
        /// </summary>
        private static void Install()
        {
            if (s_Request != null || IsInstalled())
                return;

            Debug.Log($"[BuildManagerKit] Adding {PackageName}…");

            s_Request = Client.Add(GitUrl);
            EditorApplication.update += PollRequest;
        }

        private static void PollRequest()
        {
            if (s_Request == null || !s_Request.IsCompleted)
                return;

            EditorApplication.update -= PollRequest;

            if (s_Request.Status == StatusCode.Success)
            {
                Debug.Log($"[BuildManagerKit] Installed {s_Request.Result.packageId}. "
                          + "The Build Manager window is available once scripts finish compiling: "
                          + "Tools ▸ Build Manager Kit ▸ Build Manager.");
            }
            else
            {
                // The likely causes are all worth naming: no git on PATH, no network, or no access
                // to the repository. Retrying is a menu item rather than a loop.
                Debug.LogError(
                    $"[BuildManagerKit] Could not add {PackageName}: {s_Request.Error?.message}\n\n"
                    + "Check that git is on your PATH and that you can reach the repository, then retry with "
                    + $"“{k_MenuPath}” or add it by hand in "
                    + $"Window ▸ Package Manager ▸ + ▸ Install package from git URL:\n{GitUrl}");
            }

            s_Request = null;
        }

        // Fully qualified: UnityEditor has its own unrelated PackageInfo, and the two are ambiguous
        // the moment both namespaces are in scope.
        private static bool IsInstalled() =>
            UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Any(package => package.name == PackageName);
    }
}
