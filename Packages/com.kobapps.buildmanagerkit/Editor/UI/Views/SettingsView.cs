using System.Collections.Generic;
using System.Linq;
using EditorCoreKit.Editor;
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
            "m_Common",
            "m_GlobalOnActivateSteps",
            "m_GlobalPreBuildSteps",
            "m_GlobalPostBuildSteps"
        };

        /// <inheritdoc />
        internal override string Title => "Settings";

        /// <inheritdoc />
        internal override VisualElement Build()
        {
            var page = KUILayout.Page();

            var serializedObject = new SerializedObject(Settings);

            page.Add(BuildCommonConfigurationCard());

            var behaviour = new KUICard(
                "Behaviour",
                "Stored in the settings asset and shared by the Editor and CI.");
            KUIProperty.DrawChildren(behaviour, serializedObject.GetIterator(), serializedObject, k_HiddenFields);

            var assetRow = KUILayout.Row(
                KUIButton.Secondary("Ping Settings Asset", () => EditorGUIUtility.PingObject(Settings)),
                KUIButton.Secondary("Rescan Project", () =>
                {
                    var added = Settings.DiscoverAssets();
                    Debug.Log($"[BuildManagerKit] Registered {added} new asset(s).");
                    Window.RefreshCurrentView();
                }));

            assetRow.style.marginTop = 6;
            behaviour.Add(assetRow);

            page.Add(behaviour);

            page.Add(new KUICard(
                "Global actions",
                "These three lists run for every environment and every profile in the project. Put shared "
                + "work here once rather than repeating it on each environment or profile asset."));

            var activateCard = new KUICard();
            activateCard.Add(new StepListView(serializedObject, "m_GlobalOnActivateSteps",
                BuildStepScope.EnvironmentActivate,
                "Global on activate actions",
                "Run whenever any environment is activated, before that environment's own actions."));
            page.Add(activateCard);

            var preCard = new KUICard();
            preCard.Add(new StepListView(serializedObject, "m_GlobalPreBuildSteps", BuildStepScope.PreBuild,
                "Global pre build actions",
                "Run first, before the environment and profile actions, for every build in this project."));
            page.Add(preCard);

            var postCard = new KUICard();
            postCard.Add(new StepListView(serializedObject, "m_GlobalPostBuildSteps", BuildStepScope.PostBuild,
                "Global post build actions",
                "Run last, after the profile and environment actions."));
            page.Add(postCard);

            page.Add(BuildAgentSkillCard());
            page.Add(BuildExtensionCard());
            page.Add(BuildRuntimeCard());

            page.Bind(serializedObject);
            return page;
        }

        /// <summary>
        /// A read-only summary of the common configuration, pointing at the Environments tab where it
        /// is edited.
        ///
        /// It is summarised rather than repeated here: two editable copies of the same block invite
        /// the question of which one is authoritative, and the values belong next to the environments
        /// that override them.
        /// </summary>
        private VisualElement BuildCommonConfigurationCard()
        {
            var common = Settings.Common;

            var card = new KUICard(
                "Common configuration",
                "The settings that are the same in every environment — product and company name, bundle "
                + "identifier, icon, shared runtime variables and versioning. Every environment starts from "
                + "them and overrides only what differs, so a rename is one edit. A profile that versions "
                + "itself still wins over both.");

            card.Add(KUIText.KeyValue("Shared values",
                common.IsConfigured ? common.Describe() : "nothing shared yet"));

            var edit = KUIButton.Secondary("Edit In Environments Tab",
                    () => BuildManagerWindow.Open("Environments"))
                .Tip("The panel at the top of the Environments tab, above the environment list.");

            edit.style.alignSelf = Align.FlexStart;
            edit.style.marginTop = 6;
            card.Add(edit);

            return card;
        }

        private VisualElement BuildAgentSkillCard()
        {
            var card = new KUICard(
                "AI assistant skill",
                "Ships with the package. Teaches a coding agent to manage this project's environments, "
                + "config assets and builds through the validated command line instead of editing the "
                + ".asset files as text — which silently drops action lists and asset references.");

            var project = AgentSkill.GetState(AgentSkillScope.Project);
            var user = AgentSkill.GetState(AgentSkillScope.User);

            card.Add(KUIText.KeyValue("This project", Describe(project)));
            card.Add(KUIText.KeyValue("This machine", Describe(user)));

            var needsAttention = project != AgentSkillState.UpToDate && user != AgentSkillState.UpToDate;

            var row = KUILayout.Row(needsAttention
                ? KUIButton.Primary("Install Skill…", AgentSkillWindow.Open)
                : KUIButton.Secondary("Manage Skill…", AgentSkillWindow.Open));

            row.style.marginTop = 6;
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
            var card = new KUICard(
                "Extending the pipeline",
                "Two ways to add your own logic. Both are picked up automatically — no registration needed.");

            card.Add(KUIText.SectionTitle("1 · A configurable action"));
            card.Add(KUIText.Code(@"[Serializable]
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
}"));

            card.Add(KUIText.SectionTitle("2 · A plain code hook"));
            card.Add(KUIText.Code(@"[BuildHook(BuildStepScope.PreBuild, Order = -100)]
static void StampLicences(BuildContext context)
{
    LicenceBaker.Bake(context.Version, context.Environment.Id);
}"));

            card.Add(KUIText.SectionTitle("Registered right now"));
            card.Add(KUIText.KeyValue("Action types", BuildStepRegistry.Descriptors.Count.ToString()));
            card.Add(KUIText.KeyValue("Code hooks",
                BuildStepRegistry.Hooks.Count == 0
                    ? "none"
                    : string.Join(", ", BuildStepRegistry.Hooks.Select(hook => hook.DisplayName))));

            return card;
        }

        private VisualElement BuildRuntimeCard()
        {
            var card = new KUICard(
                "Runtime access",
                "The generated BuildInfo asset lets shipped code read the environment it was built with. It is "
                + "regenerated on every build and on every Editor environment switch, so play mode agrees with "
                + "the player.");

            card.Add(KUIText.Code(@"using BuildManagerKit;

if (BuildInfo.Current.IsEnvironment(""prod""))
    Analytics.Enable();

string api = BuildInfo.Current.GetVariable(""api_url"", ""https://localhost:8080"");
Debug.Log(BuildInfo.Current.ShortVersionString);   // 1.4.2+118 (prod)"));

            var info = BuildInfoWriter.Load();
            card.Add(KUIText.KeyValue("Generated asset",
                info != null ? ProjectPaths.BuildInfoAssetPath : "not generated yet"));

            if (info != null)
            {
                card.Add(KUIText.KeyValue("Current contents", info.ToString()));
                card.Add(KUIButton.Danger("Delete Generated Asset", BuildInfoWriter.Delete));
            }

            return card;
        }
    }
}
