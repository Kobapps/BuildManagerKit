using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// The landing tab: what is active right now, what is about to be built, and a live console
    /// while it builds.
    /// </summary>
    internal sealed class DashboardView : BuildManagerView
    {
        private BuildConsole m_Console;

        /// <inheritdoc />
        internal override string Title => "Dashboard";

        /// <inheritdoc />
        internal override VisualElement Build()
        {
            var root = new ScrollView();
            root.AddToClassList("bmk-scroll");

            var health = BuildHealthCard();
            if (health != null)
                root.Add(health);

            root.Add(BuildEnvironmentCard());
            root.Add(BuildPlatformCard());
            root.Add(BuildBuildCard());
            root.Add(BuildConsoleCard());
            root.Add(BuildRecentCard());

            return root;
        }

        /// <inheritdoc />
        internal override void OnBuildLog(BuildLogEntry entry) => m_Console?.Append(entry);

        /// <summary>
        /// Surfaces project-wide problems — duplicate ids, colliding output paths, clashing
        /// defines — at the top of the dashboard. Returns null when the project is healthy so a
        /// well-configured project shows no clutter.
        /// </summary>
        private VisualElement BuildHealthCard()
        {
            BuildValidationReport report;

            try
            {
                report = BuildManagerIntegrity.Check(Settings);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }

            if (report.Issues.Count == 0)
                return null;

            var isError = report.HasErrors;

            var card = BuildManagerUI.Card(
                null,
                null,
                isError ? new Color(0.97f, 0.32f, 0.29f) : new Color(0.82f, 0.60f, 0.13f));

            var header = new VisualElement();
            header.AddToClassList("bmk-row");
            header.Add(BuildManagerUI.Badge(isError ? "PROBLEMS" : "WARNINGS", isError ? "error" : "warning"));

            var title = new Label(isError
                ? $"{report.ErrorCount} project problem(s) will break builds"
                : $"{report.WarningCount} project warning(s)");
            title.AddToClassList("bmk-card__title");
            title.style.marginBottom = 0;

            header.Add(title);
            header.Add(BuildManagerUI.Spacer());
            header.Add(new Button(() => Window.RefreshCurrentView()) { text = "Re-check" });
            card.Add(header);

            // Cap the listing: a badly merged settings asset can produce a long tail of issues and
            // the dashboard is not the place to scroll through all of them.
            foreach (var issue in report.Issues.Take(6))
            {
                var line = BuildManagerUI.Muted((issue.IsError ? "• " : "◦ ") + issue);
                line.style.marginLeft = 4;
                card.Add(line);
            }

            if (report.Issues.Count > 6)
                card.Add(BuildManagerUI.Muted($"…and {report.Issues.Count - 6} more. "
                                              + "Tools ▸ Build Manager Kit ▸ Run Project Health Check."));

            return card;
        }

        private VisualElement BuildEnvironmentCard()
        {
            var environment = Settings.ActiveEnvironment;

            var card = BuildManagerUI.Card(
                environment != null ? $"Environment · {environment.DisplayName}" : "Environment · none",
                environment != null && !string.IsNullOrWhiteSpace(environment.Description)
                    ? environment.Description
                    : "Environments carry defines, identifiers and runtime variables. Switching one applies "
                      + "the same changes a build would, so play mode matches the shipped configuration.",
                environment != null ? environment.Color : (Color?)null);

            if (environment != null)
            {
                var defines = environment.GetAddedDefines().ToArray();
                var variables = ConfigResolver.ResolveVariables(Settings, environment);
                var identifier = ConfigResolver.ResolveApplicationIdentifier(Settings, environment);

                card.Add(BuildManagerUI.KeyValue("Id", environment.Id));
                card.Add(BuildManagerUI.KeyValue("Defines",
                    defines.Length > 0 ? string.Join("  ", defines) : "none"));

                // Resolved rather than declared: what shipped code sees is the base environment's
                // variables with this environment's on top.
                card.Add(BuildManagerUI.KeyValue("Runtime variables",
                    variables.Count > 0
                        ? string.Join(", ", variables.Select(variable => variable.key))
                        : "none"));

                if (!string.IsNullOrEmpty(identifier))
                    card.Add(BuildManagerUI.KeyValue("Bundle id", identifier));

                var inheritance = ConfigResolver.DescribeInheritance(Settings, environment);
                if (!string.IsNullOrEmpty(inheritance))
                    card.Add(BuildManagerUI.Muted(inheritance));
            }

            var row = new VisualElement();
            row.AddToClassList("bmk-row");
            row.AddToClassList("bmk-row--wrap");
            row.style.marginTop = 6;

            foreach (var candidate in Settings.GetSortedEnvironments())
            {
                var captured = candidate;
                var button = new Button(() =>
                {
                    EnvironmentManager.Activate(captured, true);
                    Window.RefreshHeader();
                    Window.RefreshCurrentView();
                }) { text = captured.DisplayName };

                button.style.marginRight = 4;

                if (captured == environment)
                {
                    button.SetEnabled(false);
                    button.style.borderLeftWidth = 3;
                    button.style.borderLeftColor = captured.Color;
                }

                row.Add(button);
            }

            if (Settings.Environments.Count == 0)
            {
                row.Add(BuildManagerUI.PrimaryButton("Create dev / stage / prod",
                    () =>
                    {
                        BuildManagerBootstrap.CreateDefaultEnvironments();
                        Window.RefreshCurrentView();
                        Window.RefreshHeader();
                    }));
            }

            card.Add(row);
            return card;
        }

        private VisualElement BuildPlatformCard()
        {
            var active = EditorUserBuildSettings.activeBuildTarget;
            var card = BuildManagerUI.Card(
                null,
                "Switching platforms stores the settings of the one you are leaving and restores whatever "
                + "was saved for the one you move to.");

            var heading = new VisualElement();
            heading.AddToClassList("bmk-row");

            var headingIcon = new Image
            {
                image = BuildTargetIcons.Get(active, EditorUserBuildSettings.standaloneBuildSubtarget),
                scaleMode = ScaleMode.ScaleToFit
            };
            headingIcon.AddToClassList("bmk-pill__icon");

            var headingLabel = new Label($"Platform · {BuildTargetUtility.GetShortName(active)}");
            headingLabel.AddToClassList("bmk-card__title");
            headingLabel.style.marginBottom = 0;

            heading.Add(headingIcon);
            heading.Add(headingLabel);
            card.Insert(0, heading);

            var row = new VisualElement();
            row.AddToClassList("bmk-row");
            row.AddToClassList("bmk-row--wrap");

            foreach (var target in BuildTargetUtility.CommonTargets)
            {
                var captured = target;
                var installed = BuildTargetUtility.IsTargetInstalled(target);

                var button = new Button(() =>
                {
                    PlatformSwitcher.Switch(captured, StandaloneBuildSubtarget.Player, true);
                    Window.RefreshHeader();
                    Window.RefreshCurrentView();
                }) { text = BuildTargetUtility.GetShortName(target) };

                button.iconImage = Background.FromTexture2D(BuildTargetIcons.Get(target));
                button.style.marginRight = 4;
                button.SetEnabled(installed && target != active);
                button.tooltip = installed ? $"Switch to {target}" : $"The {target} module is not installed";

                row.Add(button);
            }

            card.Add(row);
            return card;
        }

        private VisualElement BuildBuildCard()
        {
            var profile = Window.SelectedProfile;
            var card = BuildManagerUI.Card("Build");

            if (profile == null)
            {
                card.Add(BuildManagerUI.Muted("No build profiles yet."));
                card.Add(BuildManagerUI.PrimaryButton("Create a starter set of profiles", () =>
                {
                    BuildManagerBootstrap.CreateDefaultProfiles();
                    Window.RefreshCurrentView();
                    Window.RefreshHeader();
                }));
                return card;
            }

            var environment = Settings.ActiveEnvironment;
            var preview = BuildOutputPreview(profile, environment);
            var git = GitInfo.Read();
            var versioning = ConfigResolver.ResolveVersioning(Settings, environment, profile);

            card.Add(BuildManagerUI.KeyValue("Profile", $"{profile.DisplayName} · {profile.Target}"));
            card.Add(BuildManagerUI.KeyValue("Environment",
                environment != null ? environment.DisplayName : "none",
                environment != null ? environment.Color : (Color?)null));
            card.Add(BuildManagerUI.KeyValue("Scenes", profile.ResolveScenePaths().Length.ToString()));
            card.Add(BuildManagerUI.KeyValue("Version",
                $"{VersionService.Resolve(versioning.Config, git, null)} "
                + $"(build {VersionService.ResolveBuildNumber(versioning.Config, git)}) "
                + $"· from {versioning.OwnerLabel}"));
            card.Add(BuildManagerUI.KeyValue("Output", preview));

            var actions = new VisualElement();
            actions.AddToClassList("bmk-row");
            actions.style.marginTop = 8;

            var buildButton = BuildManagerUI.BuildSplitButton(
                "Build " + profile.DisplayName,
                () => Window.BuildSelected(false),
                Window.ShowBuildMenu,
                !BuildRunner.IsRunning,
                $"Build '{profile.DisplayName}' for {profile.Target}.");
            buildButton.style.marginLeft = 0;
            buildButton.style.marginRight = 4;

            var dryRun = new Button(() => Window.BuildSelected(true)) { text = "Dry Run" };
            dryRun.SetEnabled(!BuildRunner.IsRunning);
            dryRun.tooltip = "Resolve, validate and log everything without writing a player.";

            var validate = new Button(() =>
            {
                var report = BuildRunner.Validate(profile, environment);
                EditorUtility.DisplayDialog(
                    $"Validation — {profile.DisplayName}",
                    report.Issues.Count == 0 ? "No problems found." : report.ToString(),
                    "OK");
            }) { text = "Validate" };

            var edit = new Button(() => BuildManagerWindow.Open("Profiles")) { text = "Edit Profile" };

            actions.Add(buildButton);
            actions.Add(dryRun);
            actions.Add(validate);
            actions.Add(edit);

            card.Add(actions);
            return card;
        }

        private VisualElement BuildConsoleCard()
        {
            var card = BuildManagerUI.Card("Console");
            m_Console = new BuildConsole();
            m_Console.style.minHeight = 180;

            var current = BuildRunner.Current;
            if (current != null && current.Log is BuildLog log)
                m_Console.SetEntries(log.Entries);
            else if (BuildHistory.Entries.Count > 0)
                m_Console.SetPlainText(BuildHistory.ReadLog(BuildHistory.Entries[0]));

            card.Add(m_Console);
            return card;
        }

        private VisualElement BuildRecentCard()
        {
            var card = BuildManagerUI.Card("Recent builds");
            var entries = BuildHistory.Entries.Take(5).ToArray();

            if (entries.Length == 0)
            {
                card.Add(BuildManagerUI.Muted("Nothing built yet."));
                return card;
            }

            foreach (var entry in entries)
            {
                var row = new VisualElement();
                row.AddToClassList("bmk-list-item");

                row.Add(BuildManagerUI.StatusBadge(entry.result.status));

                var label = new Label(
                    $"{entry.result.profileName} · {entry.result.environmentId} · "
                    + $"{entry.result.version}+{entry.result.buildNumber} · "
                    + BuildTargetUtility.FormatDuration(TimeSpan.FromSeconds(entry.result.durationSeconds)));
                label.AddToClassList("bmk-grow");
                label.style.fontSize = 11;

                var when = new Label(FormatRelative(entry.FinishedAt));
                when.AddToClassList("bmk-muted");

                row.Add(label);
                row.Add(when);
                card.Add(row);
            }

            var openHistory = new Button(() => BuildManagerWindow.Open("History")) { text = "Open History" };
            openHistory.style.marginTop = 6;
            openHistory.style.alignSelf = Align.FlexStart;
            card.Add(openHistory);

            return card;
        }

        private string BuildOutputPreview(BuildTargetProfile profile, BuildEnvironment environment)
        {
            try
            {
                var git = GitInfo.Read();
                var versioning = ConfigResolver.ResolveVersioning(Settings, environment, profile).Config;
                var version = VersionService.Resolve(versioning, git, null);
                var number = VersionService.ResolveBuildNumber(versioning, git);

                // Ordinal: {env} and {ENV} are distinct tokens.
                var tokens = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["projectRoot"] = ProjectPaths.ProjectRoot,
                    ["projectName"] = ProjectPaths.ProjectName,
                    ["productName"] = PlayerSettings.productName,
                    ["companyName"] = PlayerSettings.companyName,
                    ["profile"] = profile.Id,
                    ["profileName"] = profile.DisplayName,
                    ["env"] = environment != null ? environment.Id : "none",
                    ["ENV"] = environment != null ? environment.Id.ToUpperInvariant() : "NONE",
                    ["envName"] = environment != null ? environment.DisplayName : "none",
                    ["target"] = profile.Target.ToString(),
                    ["targetShort"] = BuildTargetUtility.GetShortName(profile.Target),
                    ["platform"] = BuildPipeline.GetBuildTargetGroup(profile.Target).ToString(),
                    ["version"] = version,
                    ["versionDots"] = version.Replace(".", string.Empty),
                    ["buildNumber"] = number.ToString(),
                    ["branch"] = git.Branch,
                    ["commit"] = git.ShortCommit,
                    ["buildType"] = profile.DevelopmentBuild ? "Development" : "Release"
                };

                var directory = BuildTokens.Resolve(profile.OutputDirectoryTemplate, tokens, DateTime.Now);
                var name = BuildTargetUtility.GetPlayerFileName(
                    profile.Target,
                    BuildTokens.Resolve(profile.ExecutableNameTemplate, tokens, DateTime.Now),
                    profile.Android.buildAppBundle);

                return ProjectPaths.Normalize(System.IO.Path.Combine(
                    ProjectPaths.MakeAbsolute(directory), name));
            }
            catch (Exception exception)
            {
                return "could not resolve: " + exception.Message;
            }
        }

        private static string FormatRelative(DateTime when)
        {
            if (when == DateTime.MinValue)
                return string.Empty;

            var delta = DateTime.Now - when;

            if (delta.TotalMinutes < 1)
                return "just now";
            if (delta.TotalHours < 1)
                return $"{(int)delta.TotalMinutes}m ago";
            if (delta.TotalDays < 1)
                return $"{(int)delta.TotalHours}h ago";
            if (delta.TotalDays < 7)
                return $"{(int)delta.TotalDays}d ago";

            return when.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
