using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>Base class for the tabs of <see cref="BuildManagerWindow"/>.</summary>
    internal abstract class BuildManagerView
    {
        /// <summary>The window hosting this view.</summary>
        protected BuildManagerWindow Window { get; private set; }

        /// <summary>The project settings asset.</summary>
        protected BuildManagerSettings Settings => BuildManagerSettings.Instance;

        /// <summary>Label shown in the sidebar.</summary>
        internal abstract string Title { get; }

        /// <summary>Optional count shown to the right of the sidebar label.</summary>
        internal virtual string Badge => null;

        /// <summary>Builds the content of the tab.</summary>
        internal abstract VisualElement Build();

        /// <summary>Called for every log line while a build runs.</summary>
        internal virtual void OnBuildLog(BuildLogEntry entry)
        {
        }

        /// <summary>Called when a build starts or finishes.</summary>
        internal virtual void OnBuildStateChanged()
        {
        }

        internal void Attach(BuildManagerWindow window) => Window = window;
    }

    /// <summary>
    /// The Build Manager window: one place to configure profiles, environments, queues and CI, to
    /// launch builds, and to watch them run.
    /// </summary>
    public sealed class BuildManagerWindow : EditorWindow
    {
        private const string k_SelectedProfileKey = "BuildManagerKit.SelectedProfile";
        private const string k_SelectedTabKey = "BuildManagerKit.SelectedTab";

        private readonly List<BuildManagerView> m_Views = new List<BuildManagerView>();
        private VisualElement m_Sidebar;
        private VisualElement m_Content;
        private VisualElement m_HeaderControls;
        private Label m_StatusLabel;
        private VisualElement m_StatusBadge;
        private int m_SelectedIndex;

        /// <summary>The profile the header Build button acts on.</summary>
        internal BuildTargetProfile SelectedProfile
        {
            get
            {
                var id = EditorPrefs.GetString(k_SelectedProfileKey, string.Empty);
                var profile = BuildManagerSettings.Instance.FindProfile(id);
                return profile != null
                    ? profile
                    : BuildManagerSettings.Instance.GetEnabledProfiles().FirstOrDefault();
            }
            set
            {
                EditorPrefs.SetString(k_SelectedProfileKey, value != null ? value.Id : string.Empty);
                RefreshHeader();
            }
        }

        /// <summary>Opens (or focuses) the window.</summary>
        public static BuildManagerWindow Open()
        {
            var window = GetWindow<BuildManagerWindow>();
            window.titleContent = new GUIContent("Build Manager");
            window.minSize = new Vector2(760, 460);
            window.Show();
            return window;
        }

        /// <summary>Opens the window on a specific tab.</summary>
        internal static void Open(string tabTitle)
        {
            var window = Open();
            var index = window.m_Views.FindIndex(view =>
                string.Equals(view.Title, tabTitle, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
                window.Select(index);
        }

        private void CreateGUI()
        {
            BuildManagerUI.ApplyStyles(rootVisualElement);
            rootVisualElement.AddToClassList("bmk-root");

            BuildManagerSettings.Instance.DiscoverAssets();

            m_Views.Clear();
            m_Views.Add(new DashboardView());
            m_Views.Add(new ProfilesView());
            m_Views.Add(new EnvironmentsView());
            m_Views.Add(new QueuesView());
            m_Views.Add(new HistoryView());
            m_Views.Add(new CiView());
            m_Views.Add(new SettingsView());

            foreach (var view in m_Views)
                view.Attach(this);

            rootVisualElement.Add(BuildHeader());

            var body = new VisualElement();
            body.AddToClassList("bmk-body");

            m_Sidebar = new VisualElement();
            m_Sidebar.AddToClassList("bmk-sidebar");

            m_Content = new VisualElement();
            m_Content.AddToClassList("bmk-content");

            body.Add(m_Sidebar);
            body.Add(m_Content);
            rootVisualElement.Add(body);
            rootVisualElement.Add(BuildStatusBar());

            m_SelectedIndex = Mathf.Clamp(EditorPrefs.GetInt(k_SelectedTabKey, 0), 0, m_Views.Count - 1);
            RebuildSidebar();
            Select(m_SelectedIndex);
        }

        private void OnEnable()
        {
            BuildRunner.LogAppended += HandleLog;
            BuildRunner.RunStarted += HandleRunStarted;
            BuildRunner.RunFinished += HandleRunFinished;
            BuildManagerSettings.ActiveEnvironmentChanged += HandleEnvironmentChanged;
            BuildHistory.Changed += RefreshSidebarBadges;
        }

        private void OnDisable()
        {
            BuildRunner.LogAppended -= HandleLog;
            BuildRunner.RunStarted -= HandleRunStarted;
            BuildRunner.RunFinished -= HandleRunFinished;
            BuildManagerSettings.ActiveEnvironmentChanged -= HandleEnvironmentChanged;
            BuildHistory.Changed -= RefreshSidebarBadges;
        }

        /// <summary>Rebuilds the currently visible tab.</summary>
        internal void RefreshCurrentView() => Select(m_SelectedIndex);

        /// <summary>Rebuilds the header pills and the sidebar badges.</summary>
        internal void RefreshHeader()
        {
            if (m_HeaderControls == null)
                return;

            m_HeaderControls.Clear();
            PopulateHeaderControls(m_HeaderControls);
            RefreshSidebarBadges();
        }

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("bmk-header");

            var title = new Label("Build Manager Kit");
            title.AddToClassList("bmk-header__title");

            var version = new Label("v1.0.0");
            version.AddToClassList("bmk-header__version");

            header.Add(title);
            header.Add(version);
            header.Add(BuildManagerUI.Spacer());

            m_HeaderControls = new VisualElement();
            m_HeaderControls.AddToClassList("bmk-header__controls");
            PopulateHeaderControls(m_HeaderControls);
            header.Add(m_HeaderControls);

            return header;
        }

        private void PopulateHeaderControls(VisualElement container)
        {
            var settings = BuildManagerSettings.Instance;
            var environment = settings.ActiveEnvironment;

            container.Add(BuildManagerUI.Pill(
                environment != null ? environment.DisplayName : "No environment",
                environment != null ? environment.Color : Color.gray,
                ShowEnvironmentMenu,
                "The environment applied to the Editor. Click to switch."));

            var activeTarget = EditorUserBuildSettings.activeBuildTarget;
            var activeSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;

            container.Add(BuildManagerUI.Pill(
                BuildTargetUtility.GetShortName(activeTarget),
                BuildTargetIcons.Get(activeTarget, activeSubtarget),
                ShowPlatformMenu,
                "Editor platform: the active build target.\n"
                + "Switching preserves each platform's own settings."));

            // Separates "what am I looking at" from "what will I build".
            var divider = new VisualElement();
            divider.AddToClassList("bmk-divider");
            container.Add(divider);

            var profile = SelectedProfile;

            // The Build control is also the target selector: pressing it builds the selected profile,
            // and its menu builds — and selects — any other one.
            container.Add(BuildManagerUI.BuildSplitButton(
                profile != null ? "Build " + profile.DisplayName : "Build",
                () => BuildSelected(false),
                ShowBuildMenu,
                profile != null && !BuildRunner.IsRunning,
                profile != null
                    ? $"Build '{profile.DisplayName}' for {profile.Target} with the active environment."
                    : "No build profile yet. Use the ▼ menu to create one.",
                !BuildRunner.IsRunning));

            var overflow = new Button { text = "⋯", tooltip = "More actions" };
            overflow.AddToClassList("bmk-icon-button");
            overflow.clicked += () => ShowOverflowMenu(overflow.worldBound);
            container.Add(overflow);
        }

        private VisualElement BuildStatusBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("bmk-status");

            m_StatusBadge = new VisualElement();
            m_StatusBadge.AddToClassList("bmk-row");
            m_StatusBadge.style.flexShrink = 0;

            m_StatusLabel = new Label("Idle");
            m_StatusLabel.AddToClassList("bmk-status__label");
            m_StatusLabel.style.flexGrow = 1;

            bar.Add(m_StatusBadge);
            bar.Add(m_StatusLabel);

            var openLogs = new Button(() =>
            {
                var folder = ProjectPaths.MakeAbsolute(BuildManagerSettings.Instance.LogFolder);
                System.IO.Directory.CreateDirectory(folder);
                EditorUtility.RevealInFinder(folder);
            })
            {
                text = "Logs",
                tooltip = "Open the folder containing the text log of every build."
            };

            bar.Add(openLogs);
            return bar;
        }

        private void RebuildSidebar()
        {
            m_Sidebar.Clear();

            for (var i = 0; i < m_Views.Count; i++)
            {
                var index = i;
                var view = m_Views[i];

                var item = new VisualElement();
                item.AddToClassList("bmk-nav");
                if (i == m_SelectedIndex)
                    item.AddToClassList("bmk-nav--selected");

                var label = new Label(view.Title);
                label.AddToClassList("bmk-nav__label");
                item.Add(label);

                var badge = view.Badge;
                if (!string.IsNullOrEmpty(badge))
                {
                    var badgeLabel = new Label(badge);
                    badgeLabel.AddToClassList("bmk-nav__badge");
                    item.Add(badgeLabel);
                }

                item.RegisterCallback<MouseDownEvent>(_ => Select(index));
                m_Sidebar.Add(item);
            }

            m_Sidebar.Add(BuildManagerUI.Separator());
            m_Sidebar.Add(BuildManagerUI.Muted(
                "Tip: ⌘⇧K opens this window, and the Scene view overlay switches environments without leaving the scene."));
        }

        private void RefreshSidebarBadges()
        {
            if (m_Sidebar != null)
                RebuildSidebar();
        }

        private void Select(int index)
        {
            if (m_Views.Count == 0)
                return;

            m_SelectedIndex = Mathf.Clamp(index, 0, m_Views.Count - 1);
            EditorPrefs.SetInt(k_SelectedTabKey, m_SelectedIndex);

            RebuildSidebar();

            m_Content.Clear();

            try
            {
                m_Content.Add(m_Views[m_SelectedIndex].Build());
            }
            catch (Exception exception)
            {
                m_Content.Add(BuildManagerUI.Muted("This tab failed to draw: " + exception));
                Debug.LogException(exception);
            }
        }

        private void ShowEnvironmentMenu(Rect anchor)
        {
            var settings = BuildManagerSettings.Instance;
            var menu = new GenericMenu();
            var active = settings.ActiveEnvironment;

            foreach (var environment in settings.GetSortedEnvironments())
            {
                var captured = environment;
                menu.AddItem(new GUIContent(environment.DisplayName), environment == active,
                    () =>
                    {
                        EnvironmentManager.Activate(captured, true);
                        RefreshHeader();
                        RefreshCurrentView();
                    });
            }

            if (settings.Environments.Count == 0)
                menu.AddDisabledItem(new GUIContent("No environments configured"));

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Clear Environment"), active == null,
                () =>
                {
                    EnvironmentManager.Activate(null, true);
                    RefreshHeader();
                });
            menu.AddItem(new GUIContent("Manage Environments…"), false, () => Open("Environments"));

            BuildManagerUI.ShowMenu(menu, anchor);
        }

        private void ShowPlatformMenu(Rect anchor)
        {
            var menu = new GenericMenu();
            var active = EditorUserBuildSettings.activeBuildTarget;

            foreach (var target in BuildTargetUtility.CommonTargets)
            {
                var captured = target;
                var installed = BuildTargetUtility.IsTargetInstalled(target);
                var label = BuildTargetIcons.GetContent(
                    target,
                    BuildTargetUtility.GetShortName(target)
                    + (installed ? string.Empty : " (module not installed)"));

                if (installed)
                    menu.AddItem(label, target == active, () =>
                    {
                        PlatformSwitcher.Switch(captured, StandaloneBuildSubtarget.Player, true);
                        RefreshHeader();
                    });
                else
                    menu.AddDisabledItem(label);
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Dedicated Server/Windows"), false,
                () => PlatformSwitcher.Switch(BuildTarget.StandaloneWindows64,
                    StandaloneBuildSubtarget.Server, true));
            menu.AddItem(new GUIContent("Dedicated Server/Linux"), false,
                () => PlatformSwitcher.Switch(BuildTarget.StandaloneLinux64,
                    StandaloneBuildSubtarget.Server, true));

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Forget Saved Platform Settings"), false,
                PlatformSwitcher.ClearStoredSettings);

            BuildManagerUI.ShowMenu(menu, anchor);
        }

        /// <summary>
        /// The Build button's menu: every profile with its platform icon, so a specific target is
        /// one click away without a second selector control in the header.
        ///
        /// Picking a target also makes it the selection, so the big half of the button keeps building
        /// whatever was built last — the header still answers "what does Build do" at a glance.
        /// </summary>
        internal void ShowBuildMenu(Rect anchor)
        {
            var settings = BuildManagerSettings.Instance;
            var menu = new GenericMenu();
            var selected = SelectedProfile;
            var busy = BuildRunner.IsRunning;

            foreach (var profile in settings.Profiles.Where(profile => profile != null))
            {
                var captured = profile;
                var label = BuildTargetIcons.GetContent(
                    profile.Target,
                    $"Build {profile.DisplayName} ({BuildTargetUtility.GetShortName(profile.Target)})"
                    + (profile.Enabled ? string.Empty : " — disabled"),
                    profile.StandaloneSubtarget);

                if (busy)
                {
                    menu.AddDisabledItem(label);
                    continue;
                }

                menu.AddItem(label, profile == selected, () =>
                {
                    SelectedProfile = captured;
                    RefreshCurrentView();
                    BuildProfile(captured, BuildManagerSettings.Instance.ActiveEnvironment, false);
                });
            }

            if (settings.Profiles.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No profiles configured"));
                menu.AddItem(new GUIContent("Create Starter Profiles"), false, () =>
                {
                    BuildManagerBootstrap.CreateDefaultProfiles();
                    RefreshHeader();
                    RefreshCurrentView();
                });
            }

            menu.AddSeparator(string.Empty);

            if (selected != null && !busy)
            {
                menu.AddItem(new GUIContent($"Dry Run {selected.DisplayName}"), false, () => BuildSelected(true));
                menu.AddItem(new GUIContent($"Validate {selected.DisplayName}"), false, ValidateSelected);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Dry Run"));
                menu.AddDisabledItem(new GUIContent("Validate"));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Manage Profiles…"), false, () => Open("Profiles"));

            BuildManagerUI.ShowMenu(menu, anchor);
        }

        private void ShowOverflowMenu(Rect anchor)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Dry Run"), false, () => BuildSelected(true));
            menu.AddItem(new GUIContent("Validate Selected Profile"), false, ValidateSelected);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Select Settings Asset"), false,
                () => Selection.activeObject = BuildManagerSettings.Instance);
            menu.AddItem(new GUIContent("Rescan Project For Assets"), false, () =>
            {
                var added = BuildManagerSettings.Instance.DiscoverAssets();
                Debug.Log($"[BuildManagerKit] Registered {added} new asset(s).");
                RefreshCurrentView();
            });
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Delete Generated BuildInfo Asset"), false, BuildInfoWriter.Delete);

            BuildManagerUI.ShowMenu(menu, anchor);
        }

        /// <summary>Builds <see cref="SelectedProfile"/> with the active environment.</summary>
        internal void BuildSelected(bool dryRun)
        {
            var profile = SelectedProfile;
            if (profile == null)
            {
                EditorUtility.DisplayDialog("Build Manager Kit", "Select a build profile first.", "OK");
                return;
            }

            BuildProfile(profile, BuildManagerSettings.Instance.ActiveEnvironment, dryRun);
        }

        /// <summary>Builds a profile from the UI, honouring the confirmation settings.</summary>
        internal void BuildProfile(BuildTargetProfile profile, BuildEnvironment environment, bool dryRun)
        {
            var settings = BuildManagerSettings.Instance;
            environment ??= profile.DefaultEnvironment ?? settings.ActiveEnvironment;

            var needsConfirmation = !dryRun &&
                                    (settings.ConfirmBeforeBuilding ||
                                     (environment != null && environment.RequireConfirmation));

            if (needsConfirmation && !EditorUtility.DisplayDialog(
                    "Build",
                    $"Build '{profile.DisplayName}' for {profile.Target}"
                    + (environment != null ? $" using the '{environment.DisplayName}' environment?" : "?"),
                    "Build",
                    "Cancel"))
                return;

            // delayCall keeps the click handler short so the UI repaints before the build blocks.
            EditorApplication.delayCall += () =>
            {
                BuildRunner.Run(new BuildRunRequest
                {
                    Profile = profile,
                    Environment = environment,
                    DryRun = dryRun,
                    Interactive = true
                });
            };
        }

        private void ValidateSelected()
        {
            var profile = SelectedProfile;
            if (profile == null)
                return;

            var report = BuildRunner.Validate(profile, BuildManagerSettings.Instance.ActiveEnvironment);

            var message = report.Issues.Count == 0
                ? "No problems found."
                : report.ToString();

            EditorUtility.DisplayDialog($"Validation — {profile.DisplayName}", message, "OK");
        }

        private void HandleLog(BuildLogEntry entry)
        {
            foreach (var view in m_Views)
                view.OnBuildLog(entry);

            if (m_StatusLabel != null)
                m_StatusLabel.text = entry.message;
        }

        private void HandleRunStarted(BuildContext context)
        {
            SetStatus($"Building {context.Profile.DisplayName}…", "warning", "RUNNING");

            foreach (var view in m_Views)
                view.OnBuildStateChanged();

            RefreshHeader();
        }

        private void HandleRunFinished(BuildRunResult result)
        {
            SetStatus(result.ToSummaryLine(), result.Succeeded ? "success" : "error",
                result.Succeeded ? "SUCCESS" : "FAILED");

            foreach (var view in m_Views)
                view.OnBuildStateChanged();

            RefreshHeader();
            RefreshSidebarBadges();
        }

        private void HandleEnvironmentChanged(BuildEnvironment environment) => RefreshHeader();

        private void SetStatus(string message, string modifier, string badge)
        {
            if (m_StatusBadge == null)
                return;

            m_StatusBadge.Clear();
            m_StatusBadge.Add(BuildManagerUI.Badge(badge, modifier));

            if (m_StatusLabel != null)
                m_StatusLabel.text = message;
        }
    }
}
