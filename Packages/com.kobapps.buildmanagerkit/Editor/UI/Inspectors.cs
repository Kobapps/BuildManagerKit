using System.Collections.Generic;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Inspector for build profiles, so editing one from the Project window offers the same action
    /// list editor as the Build Manager window.
    /// </summary>
    [CustomEditor(typeof(BuildTargetProfile))]
    internal sealed class BuildTargetProfileEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> k_Hidden = new HashSet<string>
        {
            "m_Script",
            "m_PreBuildSteps",
            "m_PostBuildSteps"
        };

        /// <inheritdoc />
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            BuildManagerUI.ApplyStyles(root);

            var profile = (BuildTargetProfile)target;

            root.Add(EckLayout.WrapRow(
                EckButton.Primary("Build", () =>
                    BuildManagerWindow.Open().BuildProfile(profile, BuildManagerSettings.Instance.ActiveEnvironment,
                        false)),
                EckButton.Secondary("Build and Run", () =>
                        BuildManagerWindow.Open().BuildProfile(profile,
                            BuildManagerSettings.Instance.ActiveEnvironment, false, true))
                    .Tip("Build, then launch the player — on the connected device for a mobile target."),
                EckButton.Secondary("Open Output Folder",
                    () => BuildManagerUI.RevealOutputFolder(profile)),
                EckButton.Secondary("Open Build Manager", () => BuildManagerWindow.Open("Profiles"))));

            EckProperty.DrawChildren(root, serializedObject.GetIterator(), serializedObject, k_Hidden);

            root.Add(new StepListView(serializedObject, "m_PreBuildSteps", BuildStepScope.PreBuild,
                "Pre build actions", null, BuildStepScopeLevel.Profile));
            root.Add(new StepListView(serializedObject, "m_PostBuildSteps", BuildStepScope.PostBuild,
                "Post build actions", null, BuildStepScopeLevel.Profile));

            root.Bind(serializedObject);
            return root;
        }
    }

    /// <summary>Inspector for environments.</summary>
    [CustomEditor(typeof(BuildEnvironment))]
    internal sealed class BuildEnvironmentEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> k_Hidden = new HashSet<string>
        {
            "m_Script",
            "m_OnActivateSteps",
            "m_PreBuildSteps",
            "m_PostBuildSteps"
        };

        /// <inheritdoc />
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            BuildManagerUI.ApplyStyles(root);

            var environment = (BuildEnvironment)target;

            root.Add(EckLayout.Row(
                EckButton.Primary("Make Active", () => EnvironmentManager.Activate(environment, true)),
                EckButton.Secondary("Open Build Manager", () => BuildManagerWindow.Open("Environments"))));

            EckProperty.DrawChildren(root, serializedObject.GetIterator(), serializedObject, k_Hidden);

            root.Add(new StepListView(serializedObject, "m_OnActivateSteps", BuildStepScope.EnvironmentActivate,
                "On activate actions", null, BuildStepScopeLevel.Environment));
            root.Add(new StepListView(serializedObject, "m_PreBuildSteps", BuildStepScope.PreBuild,
                "Pre build actions", null, BuildStepScopeLevel.Environment));
            root.Add(new StepListView(serializedObject, "m_PostBuildSteps", BuildStepScope.PostBuild,
                "Post build actions", null, BuildStepScopeLevel.Environment));

            root.Bind(serializedObject);
            return root;
        }
    }

    /// <summary>Inspector for the settings asset.</summary>
    [CustomEditor(typeof(BuildManagerSettings))]
    internal sealed class BuildManagerSettingsEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> k_Hidden = new HashSet<string>
        {
            "m_Script",
            "m_GlobalOnActivateSteps",
            "m_GlobalPreBuildSteps",
            "m_GlobalPostBuildSteps"
        };

        /// <inheritdoc />
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            BuildManagerUI.ApplyStyles(root);

            root.Add(EckButton.Primary("Open Build Manager", () => BuildManagerWindow.Open()));

            EckProperty.DrawChildren(root, serializedObject.GetIterator(), serializedObject, k_Hidden);

            root.Add(new StepListView(serializedObject, "m_GlobalOnActivateSteps",
                BuildStepScope.EnvironmentActivate, "Global on activate actions"));
            root.Add(new StepListView(serializedObject, "m_GlobalPreBuildSteps", BuildStepScope.PreBuild,
                "Global pre build actions"));
            root.Add(new StepListView(serializedObject, "m_GlobalPostBuildSteps", BuildStepScope.PostBuild,
                "Global post build actions"));

            root.Bind(serializedObject);
            return root;
        }
    }

    /// <summary>
    /// Adds a Build Manager Kit page to Project Settings, which is where people look for
    /// project-wide build configuration.
    /// </summary>
    internal static class BuildManagerSettingsProvider
    {
        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Build Manager Kit", SettingsScope.Project)
            {
                label = "Build Manager Kit",
                keywords = new HashSet<string>
                {
                    "build", "pipeline", "ci", "cd", "environment", "profile", "automation", "jenkins", "github"
                },
                activateHandler = (_, root) =>
                {
                    BuildManagerUI.ApplyStyles(root);
                    root.style.paddingLeft = 10;
                    root.style.paddingTop = 8;
                    root.style.paddingRight = 10;

                    var settings = BuildManagerSettings.Instance;
                    var serializedObject = new SerializedObject(settings);

                    var open = EckButton.Primary("Open Build Manager", () => BuildManagerWindow.Open());
                    open.style.alignSelf = Align.FlexStart;
                    root.Add(open);

                    var active = settings.ActiveEnvironment;
                    root.Add(EckText.KeyValue("Active environment",
                        active != null ? active.DisplayName : "none"));
                    root.Add(EckText.KeyValue("Profiles", settings.Profiles.Count.ToString()));
                    root.Add(EckText.KeyValue("Environments", settings.Environments.Count.ToString()));
                    root.Add(EckLayout.Separator());

                    var hidden = new HashSet<string> { "m_Script" };
                    EckProperty.DrawChildren(root, serializedObject.GetIterator(), serializedObject, hidden);

                    root.Bind(serializedObject);
                }
            };
        }
    }
}
