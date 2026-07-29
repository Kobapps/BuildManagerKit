using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// The install popup for the agent skill. Opened from the Settings tab of the Build Manager
    /// window and from <c>Tools ▸ Build Manager Kit</c>.
    ///
    /// Kept as a small utility window rather than a section inside the main window: installing
    /// writes outside <c>Assets/</c> — into <c>.claude/skills/</c> — and that deserves an explicit
    /// screen that names the destination before anything is written.
    /// </summary>
    internal sealed class AgentSkillWindow : EditorWindow
    {
        private VisualElement m_Body;

        /// <summary>Shows the popup, focusing an existing one rather than stacking copies.</summary>
        internal static void Open()
        {
            var window = GetWindow<AgentSkillWindow>(true, "AI Assistant Skill", true);
            window.minSize = new Vector2(520, 460);
            window.maxSize = new Vector2(900, 900);
            window.Show();
        }

        private void CreateGUI()
        {
            BuildManagerUI.ApplyStyles(rootVisualElement);
            rootVisualElement.AddToClassList("bmk-root");

            var scroll = new ScrollView();
            scroll.AddToClassList("bmk-scroll");
            rootVisualElement.Add(scroll);

            m_Body = new VisualElement();
            scroll.Add(m_Body);

            Rebuild();
        }

        private void Rebuild()
        {
            m_Body.Clear();
            m_Body.Add(BuildIntroCard());

            if (AgentSkill.SourcePath == null)
            {
                var missing = BuildManagerUI.Card(
                    "Skill not found in the package",
                    "This copy of Build Manager Kit does not contain Skills~/buildmanagerkit. If you are "
                    + "working from a partial checkout, restore the package and reopen this window.");

                m_Body.Add(missing);
                return;
            }

            m_Body.Add(BuildTargetCard(
                AgentSkillScope.Project,
                "This project",
                "Commit .claude/skills/ and every agent working in this repository picks the skill up — "
                + "including your teammates' and CI's."));

            m_Body.Add(BuildTargetCard(
                AgentSkillScope.User,
                "This machine",
                "Available in every Unity project you open locally. Not shared with the team."));

            m_Body.Add(BuildContentsCard());
        }

        private VisualElement BuildIntroCard()
        {
            var card = BuildManagerUI.Card(
                "AI assistant skill",
                "Teaches a coding agent — Claude Code and other agents that read .claude/skills — how to "
                + "manage this project's environments, config assets and builds correctly.");

            card.Add(BuildManagerUI.Muted(
                "Without it an agent will try to edit the .asset files as text. That looks like it works "
                + "and silently drops action lists and asset references, because they are "
                + "[SerializeReference] entries and GUID pairs rather than plain values. The skill routes "
                + "the agent through the command line instead, which validates every change and runs the "
                + "project health check afterwards."));

            card.Add(BuildManagerUI.KeyValue("Skill name", AgentSkill.SkillName));
            card.Add(BuildManagerUI.KeyValue("Ships with", "Build Manager Kit " + AgentSkill.PackageVersion));

            return card;
        }

        private VisualElement BuildTargetCard(AgentSkillScope scope, string title, string explanation)
        {
            var state = AgentSkill.GetState(scope);
            var path = AgentSkill.GetInstallPath(scope);

            var card = BuildManagerUI.Card(title, explanation);

            var header = new VisualElement();
            header.AddToClassList("bmk-row");
            header.Add(StateBadge(state));
            header.Add(BuildManagerUI.Spacer());
            card.Add(header);

            var pathLabel = new Label(path);
            pathLabel.AddToClassList("bmk-mono");
            pathLabel.AddToClassList("bmk-muted");
            pathLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(pathLabel);

            var buttons = new VisualElement();
            buttons.AddToClassList("bmk-row");
            buttons.style.marginTop = 8;

            switch (state)
            {
                case AgentSkillState.NotInstalled:
                    buttons.Add(BuildManagerUI.PrimaryButton("Install", () => Install(scope)));
                    break;

                case AgentSkillState.Outdated:
                    buttons.Add(BuildManagerUI.PrimaryButton("Update", () => Install(scope)));
                    buttons.Add(new Button(() => Remove(scope)) { text = "Remove" });
                    break;

                case AgentSkillState.UpToDate:
                    buttons.Add(new Button(() => Install(scope)) { text = "Reinstall" });
                    buttons.Add(new Button(() => Remove(scope)) { text = "Remove" });
                    break;
            }

            if (Directory.Exists(path))
                buttons.Add(new Button(() => EditorUtility.RevealInFinder(path)) { text = "Reveal" });

            card.Add(buttons);

            if (state == AgentSkillState.Outdated)
            {
                card.Add(BuildManagerUI.Muted(
                    "The installed copy differs from the one in the package — either it came from an older "
                    + "version, or it has local edits. Updating overwrites it."));
            }

            return card;
        }

        private VisualElement BuildContentsCard()
        {
            var card = BuildManagerUI.Card(
                "What gets written",
                "Plain markdown. Nothing executable, and nothing inside Assets/.");

            foreach (var file in AgentSkill.GetFileList())
            {
                var label = new Label("  " + file);
                label.AddToClassList("bmk-mono");
                card.Add(label);
            }

            card.Add(BuildManagerUI.Muted(
                "Installing replaces the whole folder, so a file dropped in a later version of the package "
                + "does not linger. Removing deletes it — but only after checking the folder really is this "
                + "skill."));

            return card;
        }

        private static Label StateBadge(AgentSkillState state)
        {
            switch (state)
            {
                case AgentSkillState.UpToDate:
                    return BuildManagerUI.Badge("Installed", "bmk-badge--success");
                case AgentSkillState.Outdated:
                    return BuildManagerUI.Badge("Update available", "bmk-badge--warning");
                case AgentSkillState.SourceMissing:
                    return BuildManagerUI.Badge("Package copy missing", "bmk-badge--error");
                default:
                    return BuildManagerUI.Badge("Not installed", "bmk-badge--neutral");
            }
        }

        private void Install(AgentSkillScope scope)
        {
            if (AgentSkill.Install(scope, out var error))
            {
                Debug.Log($"[BuildManagerKit] Agent skill installed to {AgentSkill.GetInstallPath(scope)}");

                if (scope == AgentSkillScope.Project)
                    AssetDatabase.Refresh();
            }
            else
            {
                EditorUtility.DisplayDialog("Build Manager Kit", "Could not install the skill:\n\n" + error, "OK");
            }

            Rebuild();
        }

        private void Remove(AgentSkillScope scope)
        {
            var path = AgentSkill.GetInstallPath(scope);

            if (!EditorUtility.DisplayDialog(
                    "Remove the agent skill?",
                    $"This deletes:\n\n{path}",
                    "Remove",
                    "Cancel"))
                return;

            if (!AgentSkill.Uninstall(scope, out var error))
                EditorUtility.DisplayDialog("Build Manager Kit", "Could not remove the skill:\n\n" + error, "OK");

            Rebuild();
        }
    }
}
