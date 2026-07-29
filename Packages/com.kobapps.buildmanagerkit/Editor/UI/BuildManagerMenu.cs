using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Main menu entries. The environment switcher is generated from the settings asset, so a
    /// keyboard-driven workflow ("⌘⇧E to cycle") is available without opening any window.
    /// </summary>
    internal static class BuildManagerMenu
    {
        private const string k_Root = "Tools/Build Manager Kit/";

        [MenuItem(k_Root + "Build Manager %#k", priority = 0)]
        private static void OpenWindow() => BuildManagerWindow.Open();

        [MenuItem(k_Root + "Build Selected Profile %#&b", priority = 1)]
        private static void BuildSelected()
        {
            var settings = BuildManagerSettings.Instance;
            var profile = settings.GetEnabledProfiles().FirstOrDefault();

            if (profile == null)
            {
                EditorUtility.DisplayDialog("Build Manager Kit",
                    "No build profiles are configured. Open the Build Manager to create one.", "OK");
                return;
            }

            BuildManagerWindow.Open().BuildSelected(false);
        }

        [MenuItem(k_Root + "Switch To Next Environment %#e", priority = 2)]
        private static void NextEnvironment() => EnvironmentManager.ActivateNext();

        [MenuItem(k_Root + "Switch To Next Environment %#e", validate = true)]
        private static bool NextEnvironmentValidate() =>
            BuildManagerSettings.InstanceOrNull != null &&
            BuildManagerSettings.Instance.Environments.Count > 0;

        [MenuItem(k_Root + "Validate All Profiles", priority = 20)]
        private static void ValidateAll()
        {
            var settings = BuildManagerSettings.Instance;
            var problems = 0;
            var report = new System.Text.StringBuilder();

            foreach (var profile in settings.GetEnabledProfiles())
            {
                var result = BuildRunner.Validate(profile, profile.DefaultEnvironment ?? settings.ActiveEnvironment);
                report.AppendLine($"{(result.HasErrors ? "FAIL" : result.HasWarnings ? "WARN" : "OK  ")} {profile.Id}");

                foreach (var issue in result.Issues)
                    report.AppendLine("        " + issue);

                if (result.HasErrors)
                    problems++;
            }

            if (settings.Profiles.Count == 0)
                report.AppendLine("No profiles configured.");

            EditorUtility.DisplayDialog(
                problems == 0 ? "Validation passed" : $"Validation failed ({problems} profile(s))",
                report.ToString(),
                "OK");
        }

        [MenuItem(k_Root + "Run Project Health Check", priority = 19)]
        private static void HealthCheck()
        {
            var report = BuildManagerIntegrity.Check();

            EditorUtility.DisplayDialog(
                report.HasErrors
                    ? $"Health check failed ({report.ErrorCount} error(s))"
                    : report.HasWarnings
                        ? $"Health check passed with {report.WarningCount} warning(s)"
                        : "Health check passed",
                report.Issues.Count == 0 ? "No problems found." : report.ToString(),
                "OK");
        }

        [MenuItem(k_Root + "Create Starter Setup", priority = 21)]
        private static void CreateStarterSetup()
        {
            if (!EditorUtility.DisplayDialog(
                    "Build Manager Kit",
                    "Create the settings asset, the dev / stage / prod environments and a profile for every "
                    + "installed platform?",
                    "Create",
                    "Cancel"))
                return;

            BuildManagerBootstrap.CreateEverything();
            BuildManagerWindow.Open();
        }

        [MenuItem(k_Root + "AI Assistant Skill…", priority = 23)]
        private static void AgentSkillSettings() => AgentSkillWindow.Open();

        [MenuItem(k_Root + "Select Settings Asset", priority = 22)]
        private static void SelectSettings()
        {
            var settings = BuildManagerSettings.Instance;
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem(k_Root + "Open Build Log Folder", priority = 40)]
        private static void OpenLogFolder()
        {
            var folder = ProjectPaths.MakeAbsolute(BuildManagerSettings.Instance.LogFolder);
            System.IO.Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }

        [MenuItem(k_Root + "Copy Command Line For Selected Profile", priority = 41)]
        private static void CopyCommandLine()
        {
            var settings = BuildManagerSettings.Instance;
            var profile = settings.GetEnabledProfiles().FirstOrDefault();
            var environment = settings.ActiveEnvironment;

            var command = "-batchmode -nographics -quit=false "
                          + $"-projectPath \"{ProjectPaths.ProjectRoot}\" "
                          + "-executeMethod BuildManagerKit.Editor.BuildCLI.Build "
                          + $"-bmkProfile {(profile != null ? profile.Id : "<profile>")} "
                          + $"-bmkEnv {(environment != null ? environment.Id : "<env>")} "
                          + "-bmkResultFile build-result.json";

            EditorGUIUtility.systemCopyBuffer = command;
            Debug.Log("[BuildManagerKit] Command line copied to the clipboard:\n" + command);
        }
    }
}
