using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Everything needed to take the same build to a build server: the exact command line for the
    /// current selection, and generated pipeline definitions for the common CI systems.
    /// </summary>
    internal sealed class CiView : BuildManagerView
    {
        private CiProvider m_Provider = CiProvider.GitHubActions;
        private BuildTargetProfile m_Profile;
        private BuildEnvironment m_Environment;

        /// <inheritdoc />
        internal override string Title => "CI / CD";

        /// <inheritdoc />
        internal override VisualElement Build()
        {
            m_Profile ??= Window.SelectedProfile;
            m_Environment ??= Settings.ActiveEnvironment;

            var root = new ScrollView();
            root.AddToClassList("bmk-scroll");

            root.Add(BuildSelectionCard());
            root.Add(BuildCommandCard());
            root.Add(BuildTemplateCard());
            root.Add(BuildTokenCard());

            return root;
        }

        private VisualElement BuildSelectionCard()
        {
            var card = BuildManagerUI.Card(
                "Target",
                "Pick what the generated command line and pipeline should build.");

            var profiles = Settings.Profiles.Where(profile => profile != null).ToList();
            if (profiles.Count > 0)
            {
                if (!profiles.Contains(m_Profile))
                    m_Profile = profiles[0];

                var profileField = new PopupField<BuildTargetProfile>(
                    "Profile", profiles, m_Profile, FormatProfile, FormatProfile);

                profileField.RegisterValueChangedCallback(evt =>
                {
                    m_Profile = evt.newValue;
                    Window.RefreshCurrentView();
                });

                card.Add(profileField);
            }
            else
            {
                card.Add(BuildManagerUI.Muted("Create a build profile first."));
            }

            var environments = Settings.GetSortedEnvironments().ToList();
            if (environments.Count > 0)
            {
                if (!environments.Contains(m_Environment))
                    m_Environment = environments[0];

                var environmentField = new PopupField<BuildEnvironment>(
                    "Environment", environments, m_Environment, FormatEnvironment, FormatEnvironment);

                environmentField.RegisterValueChangedCallback(evt =>
                {
                    m_Environment = evt.newValue;
                    Window.RefreshCurrentView();
                });

                card.Add(environmentField);
            }

            return card;
        }

        private VisualElement BuildCommandCard()
        {
            var profileId = m_Profile != null ? m_Profile.Id : "<profile>";
            var environmentId = m_Environment != null ? m_Environment.Id : "<env>";

            var command =
                $"{UnityExecutablePath()} \\\n"
                + "  -batchmode -nographics -quit=false \\\n"
                + $"  -projectPath \"{ProjectPaths.ProjectRoot}\" \\\n"
                + "  -executeMethod BuildManagerKit.Editor.BuildCLI.Build \\\n"
                + $"  -bmkProfile {profileId} \\\n"
                + $"  -bmkEnv {environmentId} \\\n"
                + "  -bmkResultFile build-result.json \\\n"
                + "  -logFile -";

            var card = BuildManagerUI.Card(
                "Command line",
                "Exit codes: 0 success · 1 build failed · 2 usage error · 3 cancelled. "
                + "The JSON result carries the status, duration, size, error counts and the full log.");

            var field = new TextField { multiline = true, value = command, isReadOnly = true };
            field.AddToClassList("bmk-code");
            card.Add(field);

            var row = new VisualElement();
            row.AddToClassList("bmk-row");
            row.style.marginTop = 6;

            row.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = command) { text = "Copy Command" });
            row.Add(new Button(() => EditorGUIUtility.systemCopyBuffer =
                $"-bmkProfile {profileId} -bmkEnv {environmentId}") { text = "Copy Arguments Only" });

            card.Add(row);

            card.Add(BuildManagerUI.SectionTitle("Other entry points"));
            card.Add(BuildManagerUI.KeyValue("Queue", "BuildCLI.BuildQueue  -bmkQueue <id>"));
            card.Add(BuildManagerUI.KeyValue("Environment", "BuildCLI.SwitchEnvironment  -bmkEnv <id>"));
            card.Add(BuildManagerUI.KeyValue("Platform", "BuildCLI.SwitchPlatform  -bmkTarget <BuildTarget>"));
            card.Add(BuildManagerUI.KeyValue("Inventory", "BuildCLI.List"));
            card.Add(BuildManagerUI.KeyValue("Pull request check", "BuildCLI.ValidateAll"));

            return card;
        }

        private VisualElement BuildTemplateCard()
        {
            var card = BuildManagerUI.Card(
                "Pipeline template",
                "Generated for this project, wired to the selection above.");

            var providerField = new EnumField("Provider", m_Provider);
            providerField.RegisterValueChangedCallback(evt =>
            {
                m_Provider = (CiProvider)evt.newValue;
                Window.RefreshCurrentView();
            });
            card.Add(providerField);

            var content = CiTemplateGenerator.Generate(m_Provider, m_Profile, m_Environment);

            var field = new TextField { multiline = true, value = content, isReadOnly = true };
            field.AddToClassList("bmk-code");
            field.style.minHeight = 260;
            card.Add(field);

            var row = new VisualElement();
            row.AddToClassList("bmk-row");
            row.style.marginTop = 6;

            row.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = content) { text = "Copy" });

            var fileName = CiTemplateGenerator.GetDefaultFileName(m_Provider);
            row.Add(BuildManagerUI.PrimaryButton($"Write {fileName}", () =>
            {
                var path = CiTemplateGenerator.Write(m_Provider, m_Profile, m_Environment);

                if (path == null)
                {
                    var overwrite = EditorUtility.DisplayDialog(
                        "File exists",
                        $"'{fileName}' already exists. Overwrite it?",
                        "Overwrite",
                        "Cancel");

                    if (!overwrite)
                        return;

                    path = CiTemplateGenerator.Write(m_Provider, m_Profile, m_Environment, true);
                }

                Debug.Log($"[BuildManagerKit] Wrote {path}.");
                EditorUtility.RevealInFinder(path);
            }));

            card.Add(row);
            return card;
        }

        private VisualElement BuildTokenCard()
        {
            var card = BuildManagerUI.Card(
                "Tokens",
                "Available in output paths, file names, shell commands, written files and notification messages.");

            foreach (var (token, description) in BuildTokens.Documentation)
                card.Add(BuildManagerUI.KeyValue(token, description));

            return card;
        }

        private static string UnityExecutablePath()
        {
#if UNITY_EDITOR_WIN
            return "\"C:\\Program Files\\Unity\\Hub\\Editor\\" + Application.unityVersion + "\\Editor\\Unity.exe\"";
#elif UNITY_EDITOR_OSX
            return "\"/Applications/Unity/Hub/Editor/" + Application.unityVersion + "/Unity.app/Contents/MacOS/Unity\"";
#else
            return "\"$HOME/Unity/Hub/Editor/" + Application.unityVersion + "/Editor/Unity\"";
#endif
        }

        private static string FormatProfile(BuildTargetProfile profile) =>
            profile == null ? "—" : $"{profile.DisplayName} ({profile.Id})";

        private static string FormatEnvironment(BuildEnvironment environment) =>
            environment == null ? "—" : $"{environment.DisplayName} ({environment.Id})";
    }
}
