using System;
using System.Linq;
using EditorCoreKit.Editor;
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
        private KUILogConsole m_Console;

        /// <inheritdoc />
        internal override string Title => "Dashboard";

        /// <inheritdoc />
        internal override VisualElement Build()
        {
            var page = KUILayout.Page();

            var health = BuildHealthCard();
            if (health != null)
                page.Add(health);

            page.Add(BuildEnvironmentCard());
            page.Add(BuildPlatformCard());
            page.Add(BuildBuildCard());
            page.Add(BuildConsoleCard());
            page.Add(BuildRecentCard());

            return page;
        }

        /// <inheritdoc />
        internal override void OnBuildLog(BuildLogEntry entry) => m_Console?.Append(BuildManagerUI.ToLogEntry(entry));

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

            var tone = report.HasErrors ? KUITone.Error : KUITone.Warning;

            var card = new KUICard(
                report.HasErrors
                    ? $"{report.ErrorCount} project problem(s) will break builds"
                    : $"{report.WarningCount} project warning(s)",
                null,
                KUITheme.ColorOf(tone));

            card.Header.Insert(0, new KUIBadge(report.HasErrors ? "PROBLEMS" : "WARNINGS", tone));
            card.WithHeaderAction(KUIButton.Secondary("Re-check", () => Window.RefreshCurrentView()));

            // Cap the listing: a badly merged settings asset can produce a long tail of issues and
            // the dashboard is not the place to scroll through all of them.
            foreach (var issue in report.Issues.Take(6))
            {
                var line = KUIText.Muted((issue.IsError ? KUIIcons.Bullet + " " : "◦ ") + issue);
                line.style.marginLeft = 4;
                card.Add(line);
            }

            if (report.Issues.Count > 6)
                card.Add(KUIText.Muted($"…and {report.Issues.Count - 6} more. "
                                       + "Tools ▸ Build Manager Kit ▸ Run Project Health Check."));

            return card;
        }

        private VisualElement BuildEnvironmentCard()
        {
            var environment = Settings.ActiveEnvironment;

            var card = new KUICard(
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

                card.Add(KUIText.KeyValue("Id", environment.Id));
                card.Add(KUIText.KeyValue("Defines", defines.Length > 0 ? string.Join("  ", defines) : "none"));

                // Resolved rather than declared: what shipped code sees is the base environment's
                // variables with this environment's on top.
                card.Add(KUIText.KeyValue("Runtime variables",
                    variables.Count > 0
                        ? string.Join(", ", variables.Select(variable => variable.key))
                        : "none"));

                if (!string.IsNullOrEmpty(identifier))
                    card.Add(KUIText.KeyValue("Bundle id", identifier));

                var inheritance = ConfigResolver.DescribeInheritance(Settings, environment);
                if (!string.IsNullOrEmpty(inheritance))
                    card.Add(KUIText.Muted(inheritance));
            }

            var row = KUILayout.WrapRow();
            row.style.marginTop = 6;

            foreach (var candidate in Settings.GetSortedEnvironments())
            {
                var captured = candidate;
                var button = KUIButton.Secondary(captured.DisplayName, () =>
                {
                    EnvironmentManager.Activate(captured, true);
                    Window.RefreshHeader();
                    Window.RefreshCurrentView();
                });

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
                row.Add(KUIButton.Primary("Create dev / stage / prod", () =>
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
            var card = new KUICard(
                $"Platform · {BuildTargetUtility.GetShortName(active)}",
                "Switching platforms stores the settings of the one you are leaving and restores whatever "
                + "was saved for the one you move to.");

            var icon = new Image
            {
                image = BuildTargetIcons.Get(active, EditorUserBuildSettings.standaloneBuildSubtarget),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList(KUIClass.ListItemIcon);

            // Before the title rather than beside it: the icon is part of the heading, and the
            // header's action slot pushes whatever it is given to the far edge.
            card.Header.Insert(0, icon);

            var row = KUILayout.WrapRow();

            foreach (var target in BuildTargetUtility.CommonTargets)
            {
                var captured = target;
                var installed = BuildTargetUtility.IsTargetInstalled(target);

                var button = KUIButton.WithIcon(
                    BuildTargetUtility.GetShortName(target),
                    BuildTargetIcons.Get(target),
                    () =>
                    {
                        PlatformSwitcher.Switch(captured, StandaloneBuildSubtarget.Player, true);
                        Window.RefreshHeader();
                        Window.RefreshCurrentView();
                    },
                    installed ? $"Switch to {target}" : $"The {target} module is not installed");

                button.SetEnabled(installed && target != active);
                row.Add(button);
            }

            card.Add(row);
            return card;
        }

        private VisualElement BuildBuildCard()
        {
            var profile = Window.SelectedProfile;
            var card = new KUICard("Build");

            if (profile == null)
            {
                card.Add(new KUIEmptyState(
                    "No build profiles yet",
                    "A profile says how to build one platform — target, scenes, output path and options.",
                    "Create a starter set of profiles",
                    () =>
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

            card.Add(KUIText.KeyValue("Profile", $"{profile.DisplayName} · {profile.Target}"));
            card.Add(KUIText.KeyValue("Environment",
                environment != null ? environment.DisplayName : "none",
                environment != null ? environment.Color : (Color?)null));
            card.Add(KUIText.KeyValue("Scenes", profile.ResolveScenePaths().Length.ToString()));
            card.Add(KUIText.KeyValue("Version",
                $"{VersionService.Resolve(versioning.Config, git, null)} "
                + $"(build {VersionService.ResolveBuildNumber(versioning.Config, git)}) "
                + $"· from {versioning.OwnerLabel}"));
            card.Add(KUIText.KeyValue("Output", preview));

            var buildButton = BuildManagerUI.BuildSplitButton(
                "Build " + profile.DisplayName,
                () => Window.BuildSelected(false),
                Window.ShowBuildMenu,
                !BuildRunner.IsRunning,
                $"Build '{profile.DisplayName}' for {profile.Target}.");
            buildButton.style.marginLeft = 0;
            buildButton.style.marginRight = 4;

            var runButton = KUIButton.Secondary("Build and Run", () => Window.BuildSelected(false, true));
            runButton.SetEnabled(!BuildRunner.IsRunning);
            runButton.tooltip = RunTooltip(profile);

            var dryRun = KUIButton.Secondary("Dry Run", () => Window.BuildSelected(true));
            dryRun.SetEnabled(!BuildRunner.IsRunning);
            dryRun.tooltip = "Resolve, validate and log everything without writing a player.";

            var validate = KUIButton.Secondary("Validate", () =>
            {
                var report = BuildRunner.Validate(profile, environment);
                EditorUtility.DisplayDialog(
                    $"Validation — {profile.DisplayName}",
                    report.Issues.Count == 0 ? "No problems found." : report.ToString(),
                    "OK");
            });

            var edit = KUIButton.Secondary("Edit Profile", () => BuildManagerWindow.Open("Profiles"));

            var actions = KUILayout.WrapRow(buildButton, runButton, dryRun, validate, edit);
            actions.style.marginTop = 8;

            card.Add(actions);

            // Next to the resolved path rather than in the toolbar: this is the answer to "where is
            // the build I just made", and the path above is the question.
            card.Add(KUIText.Link("Open output folder",
                () => BuildManagerUI.RevealOutputFolder(profile, environment)));

            return card;
        }

        /// <summary>
        /// What "and Run" means for this profile's platform, since it differs enough to be worth
        /// saying: a standalone player starts here, a phone build has to be plugged in.
        /// </summary>
        private static string RunTooltip(BuildTargetProfile profile)
        {
            switch (profile.Target)
            {
                case BuildTarget.Android:
                case BuildTarget.iOS:
                case BuildTarget.tvOS:
                    return $"Build, then deploy and launch on the connected {profile.Target} device.";
                case BuildTarget.WebGL:
                    return "Build, then serve it locally and open it in a browser.";
                default:
                    return "Build, then launch the player on this machine.";
            }
        }

        private VisualElement BuildConsoleCard()
        {
            var card = new KUICard("Console");
            m_Console = new KUILogConsole();
            m_Console.style.minHeight = 180;

            var current = BuildRunner.Current;
            if (current != null && current.Log is BuildLog log)
                m_Console.SetEntries(log.Entries.Select(BuildManagerUI.ToLogEntry));
            else if (BuildHistory.Entries.Count > 0)
                m_Console.SetPlainText(BuildHistory.ReadLog(BuildHistory.Entries[0]));

            card.Add(m_Console);
            return card;
        }

        private VisualElement BuildRecentCard()
        {
            var card = new KUICard("Recent builds");
            var entries = BuildHistory.Entries.Take(5).ToArray();

            if (entries.Length == 0)
            {
                card.Add(KUIEmptyState.Line("Nothing built yet."));
                return card;
            }

            var list = new VisualElement();
            list.AddToClassList(KUIClass.List);

            foreach (var entry in entries)
            {
                list.Add(new KUIListRow(
                        $"{entry.result.profileName} · {entry.result.environmentId} · "
                        + $"{entry.result.version}+{entry.result.buildNumber} · "
                        + BuildTargetUtility.FormatDuration(TimeSpan.FromSeconds(entry.result.durationSeconds)))
                    .WithDot(BuildManagerUI.ToneOf(entry.result.status), entry.result.status.ToString())
                    .WithSublabel(FormatRelative(entry.FinishedAt)));
            }

            card.Add(list);

            var openHistory = KUIButton.Secondary("Open History", () => BuildManagerWindow.Open("History"));
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
