using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Project-wide behaviour plus the two global action lists that wrap every build, and a
    /// reference for the code based extension points.
    /// </summary>
    internal sealed class SettingsView : BuildManagerView
    {
        private static readonly HashSet<string> k_HiddenFields = new HashSet<string>
        {
            "m_Script",
            "m_Profiles",
            "m_Environments",
            "m_Queues",
            "m_GlobalOnActivateSteps",
            "m_GlobalPreBuildSteps",
            "m_GlobalPostBuildSteps"
        };

        /// <inheritdoc />
        internal override string Title => "Settings";

        /// <inheritdoc />
        internal override VisualElement Build()
        {
            var root = new ScrollView();
            root.AddToClassList("bmk-scroll");

            var serializedObject = new SerializedObject(Settings);

            var behaviour = BuildManagerUI.Card(
                "Behaviour",
                "Stored in the settings asset and shared by the Editor and CI.");
            BuildManagerUI.DrawChildren(behaviour, serializedObject.GetIterator(), serializedObject, k_HiddenFields);

            var assetRow = new VisualElement();
            assetRow.AddToClassList("bmk-row");
            assetRow.style.marginTop = 6;
            assetRow.Add(new Button(() => EditorGUIUtility.PingObject(Settings)) { text = "Ping Settings Asset" });
            assetRow.Add(new Button(() =>
            {
                var added = Settings.DiscoverAssets();
                Debug.Log($"[BuildManagerKit] Registered {added} new asset(s).");
                Window.RefreshCurrentView();
            }) { text = "Rescan Project" });
            behaviour.Add(assetRow);

            root.Add(behaviour);

            root.Add(BuildManagerUI.Card(
                "Global actions",
                "These three lists run for every environment and every profile in the project. Put shared "
                + "work here once rather than repeating it on each environment or profile asset."));

            var activateCard = BuildManagerUI.Card();
            activateCard.Add(new StepListView(serializedObject, "m_GlobalOnActivateSteps",
                BuildStepScope.EnvironmentActivate,
                "Global on activate actions",
                "Run whenever any environment is activated, before that environment's own actions."));
            root.Add(activateCard);

            var preCard = BuildManagerUI.Card();
            preCard.Add(new StepListView(serializedObject, "m_GlobalPreBuildSteps", BuildStepScope.PreBuild,
                "Global pre build actions",
                "Run first, before the environment and profile actions, for every build in this project."));
            root.Add(preCard);

            var postCard = BuildManagerUI.Card();
            postCard.Add(new StepListView(serializedObject, "m_GlobalPostBuildSteps", BuildStepScope.PostBuild,
                "Global post build actions",
                "Run last, after the profile and environment actions."));
            root.Add(postCard);

            root.Add(BuildAgentSkillCard());
            root.Add(BuildExtensionCard());
            root.Add(BuildRuntimeCard());

            root.Bind(serializedObject);
            return root;
        }

        private VisualElement BuildAgentSkillCard()
        {
            var card = BuildManagerUI.Card(
                "AI assistant skill",
                "Ships with the package. Teaches a coding agent to manage this project's environments, "
                + "config assets and builds through the validated command line instead of editing the "
                + ".asset files as text — which silently drops action lists and asset references.");

            var project = AgentSkill.GetState(AgentSkillScope.Project);
            var user = AgentSkill.GetState(AgentSkillScope.User);

            card.Add(BuildManagerUI.KeyValue("This project", Describe(project)));
            card.Add(BuildManagerUI.KeyValue("This machine", Describe(user)));

            var row = new VisualElement();
            row.AddToClassList("bmk-row");
            row.style.marginTop = 6;

            var needsAttention = project != AgentSkillState.UpToDate && user != AgentSkillState.UpToDate;
            row.Add(needsAttention
                ? BuildManagerUI.PrimaryButton("Install Skill…", AgentSkillWindow.Open)
                : new Button(AgentSkillWindow.Open) { text = "Manage Skill…" });

            card.Add(row);
            return card;
        }

        private static string Describe(AgentSkillState state)
        {
            switch (state)
            {
                case AgentSkillState.UpToDate: return "installed";
                case AgentSkillState.Outdated: return "installed — update available";
                case AgentSkillState.SourceMissing: return "the package copy is missing";
                default: return "not installed";
            }
        }

        private VisualElement BuildExtensionCard()
        {
            var card = BuildManagerUI.Card(
                "Extending the pipeline",
                "Two ways to add your own logic. Both are picked up automatically — no registration needed.");

            card.Add(BuildManagerUI.SectionTitle("1 · A configurable action"));
            var stepExample = new TextField
            {
                multiline = true,
                isReadOnly = true,
                value = @"[Serializable]
[BuildStepMenu(""Custom/Upload To CDN"", Tooltip = ""Uploads the archive to the CDN."")]
public sealed class UploadToCdnStep : BuildStep
{
    [SerializeField] private string m_Bucket = ""releases"";

    public override string Summary => m_Bucket;

    public override void Validate(BuildContext context, BuildValidationReport report)
    {
        if (string.IsNullOrEmpty(context.GetVariable(""CDN_TOKEN"")))
            report.AddError(""CDN_TOKEN is not set."");
    }

    public override void Execute(BuildContext context)
    {
        context.Log.Info($""Uploading {context.OutputPath} to {m_Bucket}…"");
        // throw new BuildStepException(""…"") to fail the build
        context.AddArtifact(context.OutputPath);
    }
}"
            };
            stepExample.AddToClassList("bmk-code");
            card.Add(stepExample);

            card.Add(BuildManagerUI.SectionTitle("2 · A plain code hook"));
            var hookExample = new TextField
            {
                multiline = true,
                isReadOnly = true,
                value = @"[BuildHook(BuildStepScope.PreBuild, Order = -100)]
static void StampLicences(BuildContext context)
{
    LicenceBaker.Bake(context.Version, context.Environment.Id);
}"
            };
            hookExample.AddToClassList("bmk-code");
            card.Add(hookExample);

            card.Add(BuildManagerUI.SectionTitle("Registered right now"));
            card.Add(BuildManagerUI.KeyValue("Action types", BuildStepRegistry.Descriptors.Count.ToString()));
            card.Add(BuildManagerUI.KeyValue("Code hooks",
                BuildStepRegistry.Hooks.Count == 0
                    ? "none"
                    : string.Join(", ", BuildStepRegistry.Hooks.Select(hook => hook.DisplayName))));

            return card;
        }

        private VisualElement BuildRuntimeCard()
        {
            var card = BuildManagerUI.Card(
                "Runtime access",
                "The generated BuildInfo asset lets shipped code read the environment it was built with. It is "
                + "regenerated on every build and on every Editor environment switch, so play mode agrees with "
                + "the player.");

            var example = new TextField
            {
                multiline = true,
                isReadOnly = true,
                value = @"using BuildManagerKit;

if (BuildInfo.Current.IsEnvironment(""prod""))
    Analytics.Enable();

string api = BuildInfo.Current.GetVariable(""api_url"", ""https://localhost:8080"");
Debug.Log(BuildInfo.Current.ShortVersionString);   // 1.4.2+118 (prod)"
            };
            example.AddToClassList("bmk-code");
            card.Add(example);

            var info = BuildInfoWriter.Load();
            card.Add(BuildManagerUI.KeyValue("Generated asset",
                info != null ? ProjectPaths.BuildInfoAssetPath : "not generated yet"));

            if (info != null)
            {
                card.Add(BuildManagerUI.KeyValue("Current contents", info.ToString()));
                card.Add(new Button(BuildInfoWriter.Delete) { text = "Delete Generated Asset" });
            }

            return card;
        }
    }
}
