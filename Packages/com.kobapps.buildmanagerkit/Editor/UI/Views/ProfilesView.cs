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
    /// Master/detail editor for build profiles: the catalogue on the left, the full configuration
    /// and the two action lists on the right.
    /// </summary>
    internal sealed class ProfilesView : BuildManagerView
    {
        private static readonly HashSet<string> k_HiddenFields = new HashSet<string>
        {
            "m_Script",
            "m_OverrideVersioning",
            "m_Versioning",
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

            // The divider position is remembered, so someone who works mostly in the detail pane
            // does not narrow the catalogue again every time the window is reopened.
            var split = new KUISplitView(220f, false, "BuildManagerKit.Profiles");
            split.First.Add(BuildMasterList());
            split.Second.Add(BuildDetail());

            return split;
        }

        private VisualElement BuildMasterList()
        {
            // The pane owns the width, so the column inside it only has to fill the height it is
            // given; carrying KUIClass.Master here as well would fight the divider.
            var master = new VisualElement();
            master.style.flexGrow = 1;
            master.style.minHeight = 0;
            master.style.marginRight = 6;

            var toolbar = new KUIToolbar();
            toolbar.Add(KUIDropdownButton.Create(KUIIcons.Plus + " New", BuildCreateMenu));
            toolbar.Add(KUIButton.Secondary("Rescan", () =>
            {
                Settings.DiscoverAssets();
                Window.RefreshCurrentView();
            }));
            master.Add(toolbar);

            // The catalogue scrolls on its own so a long profile list never stretches the page.
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            master.Add(scroll);

            if (Settings.Profiles.Count == 0)
            {
                scroll.Add(KUIEmptyState.Line("No profiles yet."));
                return master;
            }

            var list = new VisualElement();
            list.AddToClassList(KUIClass.List);

            foreach (var profile in Settings.Profiles.Where(profile => profile != null))
            {
                var captured = profile;

                var row = new KUIListRow(profile.DisplayName, () => Select(captured))
                    .WithIcon(BuildTargetIcons.Get(profile.Target, profile.StandaloneSubtarget))
                    .WithSublabel(BuildTargetUtility.GetShortName(profile.Target));

                row.Selected = profile == m_Selected;

                if (!profile.Enabled)
                    row.style.opacity = 0.5f;

                // Right-click straight on the row, which is where deleting one of a long list is
                // actually convenient.
                row.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    evt.menu.AppendAction("Select", _ => Select(captured));
                    evt.menu.AppendAction("Ping Asset", _ => EditorGUIUtility.PingObject(captured));
                    evt.menu.AppendSeparator();
                    evt.menu.AppendAction("Delete…", _ =>
                    {
                        m_Selected = captured;
                        DeleteSelected();
                    });
                }));

                list.Add(row);
            }

            scroll.Add(list);
            return master;
        }

        private void Select(BuildTargetProfile profile)
        {
            m_Selected = profile;
            Window.SelectedProfile = profile;
            Window.RefreshCurrentView();
        }

        private VisualElement BuildDetail()
        {
            var detail = new ScrollView();
            detail.AddToClassList(KUIClass.Detail);

            if (m_Selected == null)
            {
                detail.Add(new KUIEmptyState(
                    "No profile selected",
                    "Pick one from the catalogue, or create a starter set for the common platforms.",
                    "Create Starter Profiles",
                    () =>
                    {
                        BuildManagerBootstrap.CreateDefaultProfiles();
                        Window.RefreshCurrentView();
                    }));

                return detail;
            }

            var serializedObject = new SerializedObject(m_Selected);

            var header = new KUICard(m_Selected.DisplayName);

            var icon = new Image
            {
                image = BuildTargetIcons.Get(m_Selected.Target, m_Selected.StandaloneSubtarget),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList(KUIClass.ListItemIcon);
            header.Header.Insert(0, icon);

            var buildButton = KUIButton.Primary("Build",
                () => Window.BuildProfile(m_Selected, Settings.ActiveEnvironment, false));
            buildButton.SetEnabled(!BuildRunner.IsRunning);

            var runButton = KUIButton.Secondary("Build and Run",
                () => Window.BuildProfile(m_Selected, Settings.ActiveEnvironment, false, true));
            runButton.SetEnabled(!BuildRunner.IsRunning);
            runButton.tooltip = "Build, then launch the player — on the connected device for a mobile target.";

            header.WithHeaderAction(KUIButton.Secondary("Open Output Folder",
                () => BuildManagerUI.RevealOutputFolder(m_Selected, Settings.ActiveEnvironment)));

            // The rest go behind the ⋮: six buttons across a header is a row nobody reads, and
            // Build is the only one that has to be visible.
            header.WithHeaderAction(KUIDropdownButton.Overflow(menu => menu
                .Item("Validate", () =>
                {
                    var report = BuildRunner.Validate(m_Selected, Settings.ActiveEnvironment);
                    EditorUtility.DisplayDialog(
                        $"Validation — {m_Selected.DisplayName}",
                        report.Issues.Count == 0 ? "No problems found." : report.ToString(),
                        "OK");
                })
                .Item("Dry Run", () => Window.BuildProfile(m_Selected, Settings.ActiveEnvironment, true))
                .Separator()
                .Item("Ping Asset", () => EditorGUIUtility.PingObject(m_Selected))
                .Separator()
                .Item("Delete…", DeleteSelected)));

            header.WithHeaderAction(runButton);
            header.WithHeaderAction(buildButton);

            if (!BuildTargetUtility.IsTargetInstalled(m_Selected.Target))
            {
                header.Add(new KUIBanner(KUITone.Warning,
                    $"The {m_Selected.Target} platform module is not installed in this Editor."));
            }

            detail.Add(header);

            var settingsCard = new KUICard("Configuration");
            KUIProperty.DrawChildren(settingsCard, serializedObject.GetIterator(), serializedObject,
                k_HiddenFields);
            detail.Add(settingsCard);

            detail.Add(BuildVersioningCard(serializedObject));

            detail.Add(BuildManagerUI.GlobalActionsBanner(Settings, "profile", includeActivate: false));

            var preCard = new KUICard();
            preCard.Add(new StepListView(serializedObject, "m_PreBuildSteps", BuildStepScope.PreBuild,
                "Pre build actions",
                "Run after the global and environment actions, immediately before the player build.",
                BuildStepScopeLevel.Profile));
            detail.Add(preCard);

            var postCard = new KUICard();
            postCard.Add(new StepListView(serializedObject, "m_PostBuildSteps", BuildStepScope.PostBuild,
                "Post build actions",
                "Run immediately after the player build, before the environment and global actions.",
                BuildStepScopeLevel.Profile));
            detail.Add(postCard);

            detail.Bind(serializedObject);
            return detail;
        }

        /// <summary>
        /// Versioning for this profile: inherited from the project's common configuration unless the
        /// profile says otherwise.
        ///
        /// Drawn by hand so the inherited case can say what it resolves to — a profile that inherits
        /// shows the answer instead of an empty block, which is the difference between "versioning is
        /// handled elsewhere" and "versioning is not configured".
        /// </summary>
        private VisualElement BuildVersioningCard(SerializedObject serializedObject)
        {
            var overrideProperty = serializedObject.FindProperty("m_OverrideVersioning");
            var versioningProperty = serializedObject.FindProperty("m_Versioning");

            var card = new KUICard(
                "Versioning",
                "Where the version string and the build number of a build of this profile come from. "
                + "Leave the override off to use the project's common configuration — set that on the base "
                + "environment in the Environments tab.");

            var toggle = new PropertyField(overrideProperty, "Version this profile differently");
            toggle.Bind(serializedObject);
            card.Add(toggle);

            var inherited = KUIText.Muted(string.Empty);
            inherited.AddToClassList("bmk-inherited__label");
            card.Add(inherited);

            var own = new PropertyField(versioningProperty, string.Empty);
            own.Bind(serializedObject);
            card.Add(own);

            void Refresh()
            {
                serializedObject.Update();

                var overrides = overrideProperty.boolValue;
                own.style.display = overrides ? DisplayStyle.Flex : DisplayStyle.None;
                inherited.style.display = overrides ? DisplayStyle.None : DisplayStyle.Flex;

                if (overrides)
                    return;

                // Resolved against the active environment: that is the pairing a Build press uses.
                var resolved = ConfigResolver.ResolveVersioning(Settings, Settings.ActiveEnvironment, null);

                inherited.text = resolved.IsOwned
                    ? $"Inherited from {resolved.OwnerLabel}: {resolved.Config.Describe()}."
                    : "Nothing manages versioning in this project, so the version and build number are left "
                      + "exactly as the project has them. Switch this on, or set the common configuration on "
                      + "the base environment.";
            }

            card.TrackPropertyValue(overrideProperty, _ => Refresh());
            card.schedule.Execute(Refresh);

            return card;
        }

        private void DeleteSelected()
        {
            var profile = m_Selected;
            if (profile == null)
                return;

            var usedByQueues = Settings.Queues
                .Where(queue => queue?.entries != null)
                .SelectMany(queue => queue.entries)
                .Count(entry => entry != null && entry.profile == profile);

            var message = $"Delete the profile '{profile.DisplayName}'?\n\n"
                          + $"The asset {AssetDatabase.GetAssetPath(profile)} is deleted and the profile is "
                          + "removed from the settings"
                          + (usedByQueues > 0
                              ? $" and from {usedByQueues} queue entr(y/ies) that build it."
                              : ".")
                          + "\n\nThis cannot be undone.";

            if (!EditorUtility.DisplayDialog("Delete profile", message, "Delete", "Cancel"))
                return;

            if (!BuildManagerBootstrap.DeleteProfile(profile))
                return;

            m_Selected = Settings.Profiles.FirstOrDefault(candidate => candidate != null);

            // The window remembers the selection by id, so the stored id has to be replaced rather
            // than left pointing at a profile that no longer exists — otherwise a later profile
            // created with the same id would silently inherit the selection.
            Window.SelectedProfile = m_Selected;

            Window.RefreshHeader();
            Window.RefreshCurrentView();
        }

        private void BuildCreateMenu(KUIMenu menu)
        {
            menu.Items(
                    BuildTargetUtility.CommonTargets,
                    BuildTargetUtility.GetShortName,
                    target => Select(BuildManagerBootstrap.CreateProfile(target)))
                .Separator()
                .Item("Dedicated Server (Linux)",
                    () => Select(BuildManagerBootstrap.CreateProfile(BuildTarget.StandaloneLinux64, server: true)));
        }
    }
}
