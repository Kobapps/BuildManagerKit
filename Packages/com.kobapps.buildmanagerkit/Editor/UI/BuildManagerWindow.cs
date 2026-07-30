using System;
using System.Collections.Generic;
using System.Linq;
using EditorCoreKit.Editor;
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
    ///
    /// The furniture — header, sidebar, content and status bar — is EditorCoreKit's window shell,
    /// so the window follows whichever theme and density the user chose for their editor tooling
    /// and only the build-specific controls are built here.
    /// </summary>
    public sealed class BuildManagerWindow : EditorWindow
    {
        private const string k_SelectedProfileKey = "BuildManagerKit.SelectedProfile";
        private const string k_SelectedTabKey = "BuildManagerKit.SelectedTab";

        private readonly List<BuildManagerView> m_Views = new List<BuildManagerView>();
        private EckWindowShell m_Shell;
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

            m_Shell = new EckWindowShell("Build Manager Kit", "v" + AgentSkill.PackageVersion)
                .MountInto(rootVisualElement);

            m_Shell.Status.Add(EckButton.Secondary("Builds", RevealOutputFolder)
                .Tip("Open the folder builds of the selected profile are written to."));

            m_Shell.Status.Add(EckButton.Secondary("Logs", RevealLogFolder)
                .Tip("Open the folder containing the text log of every build."));

            m_Shell.Status.Set("Idle");
            RefreshHeader();

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
            if (m_Shell == null)
                return;

            m_Shell.Header.Rebuild(PopulateHeaderControls);
            RefreshSidebarBadges();
        }

        private void PopulateHeaderControls(VisualElement container)
        {
            var settings = BuildManagerSettings.Instance;
            var environment = settings.ActiveEnvironment;

            container.Add(new EckPill(
                environment != null ? environment.DisplayName : "No environment",
                environment != null ? environment.Color : Color.gray,
                ShowEnvironmentMenu,
                "The environment applied to the Editor. Click to switch."));

            var activeTarget = EditorUserBuildSettings.activeBuildTarget;
            var activeSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;

            container.Add(new EckPill(
                BuildTargetUtility.GetShortName(activeTarget),
                BuildTargetIcons.Get(activeTarget, activeSubtarget),
                ShowPlatformMenu,
                "Editor platform: the active build target.\n"
                + "Switching preserves each platform's own settings."));

            // Separates "what am I looking at" from "what will I build".
            container.Add(EckLayout.VerticalSeparator());

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

            container.Add(EckDropdownButton.Overflow(BuildOverflowMenu));
        }

        private void RebuildSidebar()
        {
            if (m_Shell == null)
                return;

            m_Shell.Sidebar.Reset();

            for (var i = 0; i < m_Views.Count; i++)
            {
                var index = i;
                var view = m_Views[i];
                m_Shell.Sidebar.Add(view.Title, () => Select(index), view.Badge);
            }

            m_Shell.Sidebar.SelectedIndex = m_SelectedIndex;
            m_Shell.Sidebar.AddSeparator();
            m_Shell.Sidebar.AddFootnote(
                "Tip: ⌘⇧K opens this window, and the Scene view overlay switches environments without leaving the scene.");
        }

        private void RefreshSidebarBadges()
        {
            if (m_Shell != null)
                RebuildSidebar();
        }

        private void Select(int index)
        {
            if (m_Views.Count == 0 || m_Shell == null)
                return;

            m_SelectedIndex = Mathf.Clamp(index, 0, m_Views.Count - 1);
            EditorPrefs.SetInt(k_SelectedTabKey, m_SelectedIndex);

            RebuildSidebar();

            // The factory overload logs the exception and shows it in the pane, so a tab that
            // throws while building says so instead of leaving the window blank.
            m_Shell.SetContent(m_Views[m_SelectedIndex].Build);
        }

        private void ShowEnvironmentMenu(Rect anchor)
        {
            var settings = BuildManagerSettings.Instance;
            var active = settings.ActiveEnvironment;

            var menu = EckMenu.New()
                .Items(
                    settings.GetSortedEnvironments(),
                    environment => environment.DisplayName,
                    environment =>
                    {
                        EnvironmentManager.Activate(environment, true);
                        RefreshHeader();
                        RefreshCurrentView();
                    },
                    environment => environment == active);

            if (settings.Environments.Count == 0)
                menu.Disabled("No environments configured");

            menu.Separator()
                .Item("Clear Environment", () =>
                {
                    EnvironmentManager.Activate(null, true);
                    RefreshHeader();
                }, active == null)
                .Item("Manage Environments…", () => Open("Environments"))
                .ShowUnder(anchor);
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

            // Built as a GenericMenu rather than through EckMenu: the entries carry platform icons,
            // which only GUIContent can express.
            EckMenu.ShowUnder(menu, anchor);
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
                menu.AddItem(new GUIContent($"Build and Run {selected.DisplayName}"), false,
                    () => BuildSelected(false, true));
                menu.AddItem(new GUIContent($"Dry Run {selected.DisplayName}"), false, () => BuildSelected(true));
                menu.AddItem(new GUIContent($"Validate {selected.DisplayName}"), false, ValidateSelected);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Build and Run"));
                menu.AddDisabledItem(new GUIContent("Dry Run"));
                menu.AddDisabledItem(new GUIContent("Validate"));
            }

            menu.AddSeparator(string.Empty);

            if (selected != null)
                menu.AddItem(new GUIContent("Open Output Folder"), false, RevealOutputFolder);
            else
                menu.AddDisabledItem(new GUIContent("Open Output Folder"));

            menu.AddItem(new GUIContent("Manage Profiles…"), false, () => Open("Profiles"));

            EckMenu.ShowUnder(menu, anchor);
        }

        private void BuildOverflowMenu(EckMenu menu) =>
            menu.Item("Build and Run", () => BuildSelected(false, true), !BuildRunner.IsRunning, false)
                .Item("Dry Run", () => BuildSelected(true))
                .Item("Validate Selected Profile", ValidateSelected)
                .Separator()
                .Item("Open Output Folder", RevealOutputFolder)
                .Item("Open Build Log Folder", RevealLogFolder)
                .Separator()
                .Item("Select Settings Asset", () => Selection.activeObject = BuildManagerSettings.Instance)
                .Item("Rescan Project For Assets", () =>
                {
                    var added = BuildManagerSettings.Instance.DiscoverAssets();
                    Debug.Log($"[BuildManagerKit] Registered {added} new asset(s).");
                    RefreshCurrentView();
                })
                .Separator()
                .Item("Delete Generated BuildInfo Asset", BuildInfoWriter.Delete);

        /// <summary>Builds <see cref="SelectedProfile"/> with the active environment.</summary>
        /// <param name="dryRun">Validate and log everything without writing a player.</param>
        /// <param name="runAfterBuild">Launch the player once it is built.</param>
        internal void BuildSelected(bool dryRun, bool runAfterBuild = false)
        {
            var profile = SelectedProfile;
            if (profile == null)
            {
                EditorUtility.DisplayDialog("Build Manager Kit", "Select a build profile first.", "OK");
                return;
            }

            BuildProfile(profile, BuildManagerSettings.Instance.ActiveEnvironment, dryRun, runAfterBuild);
        }

        /// <summary>Builds a profile from the UI, honouring the confirmation settings.</summary>
        /// <param name="profile">Profile to build.</param>
        /// <param name="environment">Environment to build with, or null for the usual fallback.</param>
        /// <param name="dryRun">Validate and log everything without writing a player.</param>
        /// <param name="runAfterBuild">
        /// Launch the player once it is built — on this machine for a standalone target, on the
        /// connected device for Android and iOS, in a browser for WebGL.
        /// </param>
        internal void BuildProfile(
            BuildTargetProfile profile,
            BuildEnvironment environment,
            bool dryRun,
            bool runAfterBuild = false)
        {
            var settings = BuildManagerSettings.Instance;
            environment ??= profile.DefaultEnvironment ?? settings.ActiveEnvironment;

            var needsConfirmation = !dryRun &&
                                    (settings.ConfirmBeforeBuilding ||
                                     (environment != null && environment.RequireConfirmation));

            // The dialog says so when a player is about to be launched: on a protected environment
            // "build" and "build, then run it on the device in my hand" are different answers.
            if (needsConfirmation && !EditorUtility.DisplayDialog(
                    runAfterBuild ? "Build and Run" : "Build",
                    $"Build '{profile.DisplayName}' for {profile.Target}"
                    + (environment != null ? $" using the '{environment.DisplayName}' environment" : string.Empty)
                    + (runAfterBuild ? ", then launch it?" : "?"),
                    runAfterBuild ? "Build and Run" : "Build",
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
                    RunAfterBuild = runAfterBuild,
                    Interactive = true
                });
            };
        }

        /// <summary>Opens the folder builds of the selected profile land in.</summary>
        internal void RevealOutputFolder() =>
            BuildManagerUI.RevealOutputFolder(SelectedProfile, BuildManagerSettings.Instance.ActiveEnvironment);

        /// <summary>
        /// Opens the build log folder, creating it first. Unlike the output folder this one is
        /// ours to make: it is a fixed setting rather than a resolved template, and an empty log
        /// folder is a truthful answer to "where do the logs go".
        /// </summary>
        private static void RevealLogFolder()
        {
            var folder = ProjectPaths.MakeAbsolute(BuildManagerSettings.Instance.LogFolder);
            ProjectPaths.EnsureDirectory(folder);
            EditorUtility.RevealInFinder(folder);
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

            m_Shell?.Status.Set(entry.message, BuildManagerUI.ToneOf(entry.level));
        }

        private void HandleRunStarted(BuildContext context)
        {
            m_Shell?.Status.Set($"Building {context.Profile.DisplayName}…", "RUNNING", EckTone.Warning);

            foreach (var view in m_Views)
                view.OnBuildStateChanged();

            RefreshHeader();
        }

        private void HandleRunFinished(BuildRunResult result)
        {
            m_Shell?.Status.Set(
                result.ToSummaryLine(),
                result.Succeeded ? "SUCCESS" : "FAILED",
                result.Succeeded ? EckTone.Success : EckTone.Error);

            foreach (var view in m_Views)
                view.OnBuildStateChanged();

            RefreshHeader();
            RefreshSidebarBadges();
        }

        private void HandleEnvironmentChanged(BuildEnvironment environment) => RefreshHeader();
    }
}
