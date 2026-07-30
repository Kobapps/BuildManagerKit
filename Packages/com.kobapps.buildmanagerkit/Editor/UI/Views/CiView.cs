using System.Linq;
using EditorCoreKit.Editor;
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

            return EckLayout.Page(
                BuildSelectionCard(),
                BuildCommandCard(),
                BuildTemplateCard(),
                BuildTokenCard());
        }

        private VisualElement BuildSelectionCard()
        {
            var card = new EckCard(
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
                card.Add(EckEmptyState.Line("Create a build profile first."));
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

            var card = new EckCard(
                "Command line",
                "Exit codes: 0 success · 1 build failed · 2 usage error · 3 cancelled. "
                + "The JSON result carries the status, duration, size, error counts and the full log.");

            card.Add(EckText.Code(command));

            var row = EckLayout.Row(
                EckButton.Secondary("Copy Command", () => EditorGUIUtility.systemCopyBuffer = command),
                EckButton.Secondary("Copy Arguments Only", () => EditorGUIUtility.systemCopyBuffer =
                    $"-bmkProfile {profileId} -bmkEnv {environmentId}"));

            row.style.marginTop = 6;
            card.Add(row);

            card.Add(EckText.SectionTitle("Other entry points"));
            card.Add(EckText.KeyValue("Queue", "BuildCLI.BuildQueue  -bmkQueue <id>"));
            card.Add(EckText.KeyValue("Environment", "BuildCLI.SwitchEnvironment  -bmkEnv <id>"));
            card.Add(EckText.KeyValue("Platform", "BuildCLI.SwitchPlatform  -bmkTarget <BuildTarget>"));
            card.Add(EckText.KeyValue("Inventory", "BuildCLI.List"));
            card.Add(EckText.KeyValue("Pull request check", "BuildCLI.ValidateAll"));

            return card;
        }

        private VisualElement BuildTemplateCard()
        {
            var card = new EckCard(
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

            // A scrolling well rather than a tall block: a Jenkinsfile is longer than the pane, and
            // a card that grows to fit it pushes everything below off the page. The code class goes
            // on the well, not on the text inside it, or the frame is drawn twice.
            var text = new Label(content) { selection = { isSelectable = true } };
            text.style.whiteSpace = WhiteSpace.Normal;

            var scroll = new ScrollView();
            scroll.AddToClassList(EckClass.Code);
            scroll.style.maxHeight = 320;
            scroll.Add(text);
            card.Add(scroll);

            var fileName = CiTemplateGenerator.GetDefaultFileName(m_Provider);

            var row = EckLayout.Row(
                EckButton.Secondary("Copy", () => EditorGUIUtility.systemCopyBuffer = content),
                EckButton.Primary($"Write {fileName}", () =>
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

            row.style.marginTop = 6;
            card.Add(row);

            return card;
        }

        private VisualElement BuildTokenCard()
        {
            var card = new EckCard(
                "Tokens",
                "Available in output paths, file names, shell commands, written files and notification messages.");

            foreach (var (token, description) in BuildTokens.Documentation)
                card.Add(EckText.KeyValue(token, description));

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
