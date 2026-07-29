using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Master/detail editor for build profiles: the catalogue on the left, the full configuration
    /// and the two action lists on the right.
    /// </summary>
    internal sealed class ProfilesView : BuildManagerView
    {
        private static readonly HashSet<string> k_HiddenFields = new HashSet<string>
        {
            "m_Script",
            "m_PreBuildSteps",
            "m_PostBuildSteps"
        };

        private BuildTargetProfile m_Selected;

        /// <inheritdoc />
        internal override string Title => "Profiles";

        /// <inheritdoc />
        internal override string Badge => Settings.Profiles.Count.ToString();

        /// <inheritdoc />
        internal override VisualElement Build()
        {
            m_Selected ??= Window.SelectedProfile;

            var root = new VisualElement();
            root.AddToClassList("bmk-split");

            root.Add(BuildMasterList());
            root.Add(BuildDetail());

            return root;
        }

        private VisualElement BuildMasterList()
        {
            var master = new VisualElement();
            master.AddToClassList("bmk-master");

            var toolbar = new VisualElement();
            toolbar.AddToClassList("bmk-toolbar");

            var add = new Button(ShowCreateMenu) { text = "+ New" };
            var rescan = new Button(() =>
            {
                Settings.DiscoverAssets();
                Window.RefreshCurrentView();
            }) { text = "Rescan" };

            toolbar.Add(add);
            toolbar.Add(rescan);
            master.Add(toolbar);

            // The catalogue scrolls on its own so a long profile list never stretches the page.
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            master.Add(scroll);

            var list = new VisualElement();
            list.AddToClassList("bmk-list");

            foreach (var profile in Settings.Profiles.Where(profile => profile != null))
            {
                var captured = profile;

                var item = new VisualElement();
                item.AddToClassList("bmk-list-item");
                if (profile == m_Selected)
                    item.AddToClassList("bmk-list-item--selected");

                var icon = new Image
                {
                    image = BuildTargetIcons.Get(profile.Target, profile.StandaloneSubtarget),
                    scaleMode = ScaleMode.ScaleToFit
                };
                icon.AddToClassList("bmk-pill__icon");

                var label = new Label(profile.DisplayName);
                label.AddToClassList("bmk-grow");
                label.style.fontSize = 11;
                label.style.overflow = Overflow.Hidden;
                if (!profile.Enabled)
                    label.style.opacity = 0.5f;

                var target = new Label(BuildTargetUtility.GetShortName(profile.Target));
                target.AddToClassList("bmk-muted");
                target.style.flexShrink = 0;

                item.Add(icon);
                item.Add(label);
                item.Add(target);

                item.RegisterCallback<MouseDownEvent>(_ =>
                {
                    m_Selected = captured;
                    Window.SelectedProfile = captured;
                    Window.RefreshCurrentView();
                });

                list.Add(item);
            }

            if (Settings.Profiles.Count == 0)
                list.Add(BuildManagerUI.Muted("No profiles yet."));

            scroll.Add(list);
            return master;
        }

        private VisualElement BuildDetail()
        {
            var detail = new ScrollView();
            detail.AddToClassList("bmk-detail");

            if (m_Selected == null)
            {
                detail.Add(BuildManagerUI.EmptyState(
                    "Select a profile, or create a starter set for the common platforms.",
                    "Create Starter Profiles",
                    () =>
                    {
                        BuildManagerBootstrap.CreateDefaultProfiles();
                        Window.RefreshCurrentView();
                    }));

                return detail;
            }

            var serializedObject = new SerializedObject(m_Selected);

            var header = BuildManagerUI.Card();
            var headerRow = new VisualElement();
            headerRow.AddToClassList("bmk-row");

            var titleIcon = new Image
            {
                image = BuildTargetIcons.Get(m_Selected.Target, m_Selected.StandaloneSubtarget),
                scaleMode = ScaleMode.ScaleToFit
            };
            titleIcon.AddToClassList("bmk-pill__icon");
            headerRow.Add(titleIcon);

            var title = new Label(m_Selected.DisplayName);
            title.AddToClassList("bmk-card__title");
            title.style.marginBottom = 0;

            var buildButton = BuildManagerUI.PrimaryButton("Build",
                () => Window.BuildProfile(m_Selected, Settings.ActiveEnvironment, false));
            buildButton.SetEnabled(!BuildRunner.IsRunning);

            var validate = new Button(() =>
            {
                var report = BuildRunner.Validate(m_Selected, Settings.ActiveEnvironment);
                EditorUtility.DisplayDialog(
                    $"Validation — {m_Selected.DisplayName}",
                    report.Issues.Count == 0 ? "No problems found." : report.ToString(),
                    "OK");
            }) { text = "Validate" };

            var ping = new Button(() => EditorGUIUtility.PingObject(m_Selected)) { text = "Ping Asset" };

            headerRow.Add(title);
            headerRow.Add(BuildManagerUI.Spacer());
            headerRow.Add(validate);
            headerRow.Add(ping);
            headerRow.Add(buildButton);
            header.Add(headerRow);

            if (!BuildTargetUtility.IsTargetInstalled(m_Selected.Target))
            {
                var warning = BuildManagerUI.Muted(
                    $"⚠ The {m_Selected.Target} platform module is not installed in this Editor.");
                warning.style.color = new Color(0.82f, 0.60f, 0.13f);
                header.Add(warning);
            }

            detail.Add(header);

            var settingsCard = BuildManagerUI.Card("Configuration");
            BuildManagerUI.DrawChildren(settingsCard, serializedObject.GetIterator(), serializedObject,
                k_HiddenFields);
            detail.Add(settingsCard);

            detail.Add(BuildManagerUI.GlobalActionsBanner(Settings, "profile", includeActivate: false));

            var preCard = BuildManagerUI.Card();
            preCard.Add(new StepListView(serializedObject, "m_PreBuildSteps", BuildStepScope.PreBuild,
                "Pre build actions",
                "Run after the global and environment actions, immediately before the player build.",
                BuildStepScopeLevel.Profile));
            detail.Add(preCard);

            var postCard = BuildManagerUI.Card();
            postCard.Add(new StepListView(serializedObject, "m_PostBuildSteps", BuildStepScope.PostBuild,
                "Post build actions",
                "Run immediately after the player build, before the environment and global actions.",
                BuildStepScopeLevel.Profile));
            detail.Add(postCard);

            detail.Bind(serializedObject);
            return detail;
        }

        private void ShowCreateMenu()
        {
            var menu = new GenericMenu();

            foreach (var target in BuildTargetUtility.CommonTargets)
            {
                var captured = target;
                menu.AddItem(new GUIContent(BuildTargetUtility.GetShortName(target)), false, () =>
                {
                    m_Selected = BuildManagerBootstrap.CreateProfile(captured);
                    Window.SelectedProfile = m_Selected;
                    Window.RefreshCurrentView();
                });
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Dedicated Server (Linux)"), false, () =>
            {
                m_Selected = BuildManagerBootstrap.CreateProfile(BuildTarget.StandaloneLinux64, server: true);
                Window.SelectedProfile = m_Selected;
                Window.RefreshCurrentView();
            });

            menu.ShowAsContext();
        }
    }
}
