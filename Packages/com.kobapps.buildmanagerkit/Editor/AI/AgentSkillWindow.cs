using System.IO;
using EditorCoreKit.Editor;
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

            var page = EckLayout.Page();
            rootVisualElement.Add(page);

            m_Body = new VisualElement();
            page.Add(m_Body);

            Rebuild();
        }

        private void Rebuild()
        {
            m_Body.Clear();
            m_Body.Add(BuildIntroCard());

            if (AgentSkill.SourcePath == null)
            {
                m_Body.Add(new EckBanner(
                    EckTone.Error,
                    "Skill not found in the package",
                    "This copy of Build Manager Kit does not contain Skills~/buildmanagerkit. If you are "
                    + "working from a partial checkout, restore the package and reopen this window."));

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
            var card = new EckCard(
                "AI assistant skill",
                "Teaches a coding agent — Claude Code and other agents that read .claude/skills — how to "
                + "manage this project's environments, config assets and builds correctly.");

            card.Add(EckText.Body(
                "Without it an agent will try to edit the .asset files as text. That looks like it works "
                + "and silently drops action lists and asset references, because they are "
                + "[SerializeReference] entries and GUID pairs rather than plain values. The skill routes "
                + "the agent through the command line instead, which validates every change and runs the "
                + "project health check afterwards."));

            card.Add(EckText.KeyValue("Skill name", AgentSkill.SkillName));
            card.Add(EckText.KeyValue("Ships with", "Build Manager Kit " + AgentSkill.PackageVersion));

            return card;
        }

        private VisualElement BuildTargetCard(AgentSkillScope scope, string title, string explanation)
        {
            var state = AgentSkill.GetState(scope);
            var path = AgentSkill.GetInstallPath(scope);

            var card = new EckCard(title, explanation);
            card.Header.Insert(0, StateBadge(state));

            card.Add(EckText.Code(path));

            var buttons = EckLayout.Row();
            buttons.style.marginTop = 8;

            switch (state)
            {
                case AgentSkillState.NotInstalled:
                    buttons.Add(EckButton.Primary("Install", () => Install(scope)));
                    break;

                case AgentSkillState.Outdated:
                    buttons.Add(EckButton.Primary("Update", () => Install(scope)));
                    buttons.Add(EckButton.Secondary("Remove", () => Remove(scope)));
                    break;

                case AgentSkillState.UpToDate:
                    buttons.Add(EckButton.Secondary("Reinstall", () => Install(scope)));
                    buttons.Add(EckButton.Secondary("Remove", () => Remove(scope)));
                    break;
            }

            if (Directory.Exists(path))
                buttons.Add(EckButton.Secondary("Reveal", () => EditorUtility.RevealInFinder(path)));

            card.Add(buttons);

            if (state == AgentSkillState.Outdated)
            {
                card.Add(EckText.Muted(
                    "The installed copy differs from the one in the package — either it came from an older "
                    + "version, or it has local edits. Updating overwrites it."));
            }

            return card;
        }

        private VisualElement BuildContentsCard()
        {
            var card = new EckCard(
                "What gets written",
                "Plain markdown. Nothing executable, and nothing inside Assets/.");

            card.Add(EckText.Code(string.Join("\n", AgentSkill.GetFileList())));

            card.Add(EckText.Muted(
                "Installing replaces the whole folder, so a file dropped in a later version of the package "
                + "does not linger. Removing deletes it — but only after checking the folder really is this "
                + "skill."));

            return card;
        }

        private static EckBadge StateBadge(AgentSkillState state)
        {
            switch (state)
            {
                case AgentSkillState.UpToDate: return new EckBadge("Installed", EckTone.Success);
                case AgentSkillState.Outdated: return new EckBadge("Update available", EckTone.Warning);
                case AgentSkillState.SourceMissing: return new EckBadge("Package copy missing", EckTone.Error);
                default: return new EckBadge("Not installed");
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
