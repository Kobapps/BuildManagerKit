using System;
using System.Linq;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// The few UI helpers that are specific to this package.
    ///
    /// Everything generic — cards, pills, badges, lists, consoles, the window shell — comes from
    /// EditorCoreKit, so the window follows whichever theme and density the user picked for their
    /// editor tooling. What is left here is the vocabulary only a build tool has: turning a build
    /// outcome or a log level into a tone, and the split Build control.
    /// </summary>
    internal static class BuildManagerUI
    {
        internal const string StyleSheetPath = ProjectPaths.PackageRoot + "/Editor/UI/BuildManager.uss";

        /// <summary>
        /// Applies EditorCoreKit's stylesheets to <paramref name="element"/> and layers this
        /// package's handful of extra rules on top.
        ///
        /// The kit's sheets have to go on first: the rules here are written against its tokens, so
        /// the order is what lets a BMK-only control pick up a theme it has never heard of.
        /// </summary>
        internal static void ApplyStyles(VisualElement element)
        {
            if (element == null)
                return;

            KUITheme.Apply(element);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null && !element.styleSheets.Contains(styleSheet))
                element.styleSheets.Add(styleSheet);
        }

        /// <summary>The tone a build outcome reads as.</summary>
        internal static KUITone ToneOf(BuildRunStatus status)
        {
            switch (status)
            {
                case BuildRunStatus.Succeeded: return KUITone.Success;
                case BuildRunStatus.Failed: return KUITone.Error;
                case BuildRunStatus.Cancelled: return KUITone.Warning;
                default: return KUITone.Neutral;
            }
        }

        /// <summary>Badge for a build status, coloured by outcome.</summary>
        internal static KUIBadge StatusBadge(BuildRunStatus status)
        {
            switch (status)
            {
                case BuildRunStatus.Succeeded: return new KUIBadge("SUCCESS", KUITone.Success);
                case BuildRunStatus.Failed: return new KUIBadge("FAILED", KUITone.Error);
                case BuildRunStatus.Cancelled: return new KUIBadge("CANCELLED", KUITone.Warning);
                default: return new KUIBadge("UNKNOWN");
            }
        }

        /// <summary>
        /// The tone a log line is drawn in. Debug and Info share the neutral tone — the kit's
        /// console has one dim colour, and the distinction was never worth a second grey.
        /// </summary>
        internal static KUITone ToneOf(BuildLogLevel level)
        {
            switch (level)
            {
                case BuildLogLevel.Success: return KUITone.Success;
                case BuildLogLevel.Warning: return KUITone.Warning;
                case BuildLogLevel.Error: return KUITone.Error;
                default: return KUITone.Neutral;
            }
        }

        /// <summary>Converts a build log entry into a console line.</summary>
        internal static KUILogEntry ToLogEntry(BuildLogEntry entry) =>
            new KUILogEntry(
                entry.elapsedSeconds > 0 ? entry.Format() : entry.message,
                ToneOf(entry.level),
                entry.scope);

        /// <summary>
        /// The primary Build control: a wide half that builds what is already selected and a caret
        /// half that opens a menu of the other targets.
        ///
        /// One control rather than a separate selector dropdown — the thing you pick and the thing
        /// you press are the same decision, and splitting them across the header made two similar
        /// looking pills that both read as "a platform".
        /// </summary>
        /// <param name="text">Label of the main half, e.g. <c>Build Android</c>.</param>
        /// <param name="onClick">Invoked when the main half is pressed.</param>
        /// <param name="onDropdown">Invoked with the control's screen rect to anchor a menu.</param>
        /// <param name="enabled">
        /// Disables the main half — there is nothing to build, or a build is already running. The
        /// caret keeps working whenever <paramref name="menuEnabled"/> allows it, because the menu is
        /// also how a project with no profiles creates its first one.
        /// </param>
        /// <param name="tooltip">Tooltip for the main half.</param>
        /// <param name="menuEnabled">Disables the caret too, e.g. while a build runs.</param>
        internal static VisualElement BuildSplitButton(
            string text,
            Action onClick,
            Action<Rect> onDropdown,
            bool enabled = true,
            string tooltip = null,
            bool menuEnabled = true)
        {
            var container = new VisualElement();
            container.AddToClassList("bmk-build-button");

            var main = KUIButton.Primary(text, onClick);
            main.AddToClassList("bmk-build-button__main");
            main.tooltip = tooltip;
            main.SetEnabled(enabled);

            var caret = new Button { text = KUIIcons.Caret, tooltip = "Build a specific target" };
            caret.AddToClassList(KUIClass.Button);
            caret.AddToClassList("bmk-build-button__caret");
            caret.SetEnabled(menuEnabled);

            // Anchored to the whole control so the menu lines up with the left edge of the label
            // rather than with the 22px caret.
            caret.clicked += () => onDropdown?.Invoke(container.worldBound);

            container.Add(main);
            container.Add(caret);
            return container;
        }

        /// <summary>
        /// Opens the folder a build of this pairing lands in, so past builds can be found without
        /// reading the path off the Dashboard and pasting it into Finder.
        ///
        /// Nothing is created. A folder that does not exist yet — nothing has been built for this
        /// profile — opens its nearest existing ancestor and says so, which answers "where will it
        /// go" as well as "where did it go".
        /// </summary>
        /// <param name="profile">Profile whose output folder to open.</param>
        /// <param name="environment">
        /// Environment to resolve the path with. Null falls back the same way a build does.
        /// </param>
        internal static void RevealOutputFolder(BuildTargetProfile profile, BuildEnvironment environment = null)
        {
            if (profile == null)
            {
                EditorUtility.DisplayDialog("Build Manager Kit",
                    "Select a build profile first — the output folder is resolved from its output template.",
                    "OK");

                return;
            }

            string directory;

            try
            {
                directory = BuildRunner.ResolveOutputDirectory(profile, environment);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Build Manager Kit",
                    $"The output folder of '{profile.DisplayName}' could not be resolved:\n\n{exception.Message}",
                    "OK");

                return;
            }

            var existing = ProjectPaths.NearestExistingDirectory(directory);

            if (existing == null)
            {
                EditorUtility.DisplayDialog("Build Manager Kit",
                    $"Nothing has been built for '{profile.DisplayName}' yet, and none of the folders in its "
                    + $"output path exist:\n\n{directory}",
                    "OK");

                return;
            }

            if (existing != ProjectPaths.Normalize(directory))
            {
                Debug.Log($"[BuildManagerKit] '{directory}' does not exist yet — opening '{existing}', "
                          + "the deepest folder of that path that does.");
            }

            EditorUtility.RevealInFinder(existing);
        }

        /// <summary>
        /// A banner pointing at the project-wide action lists, shown above the per-asset lists.
        /// An action that should run everywhere belongs in the global list once, not copied onto
        /// every environment or profile.
        /// </summary>
        /// <param name="settings">Settings asset holding the global lists.</param>
        /// <param name="subject">What the current page edits, e.g. "environment" or "profile".</param>
        /// <param name="includeActivate">Count the global on-activate list too.</param>
        internal static VisualElement GlobalActionsBanner(
            BuildManagerSettings settings,
            string subject,
            bool includeActivate)
        {
            var onActivate = includeActivate
                ? settings.GlobalOnActivateSteps.Count(step => step != null)
                : 0;

            var pre = settings.GlobalPreBuildSteps.Count(step => step != null);
            var post = settings.GlobalPostBuildSteps.Count(step => step != null);
            var total = onActivate + pre + post;

            var breakdown = includeActivate
                ? $"{onActivate} on activate · {pre} pre build · {post} post build"
                : $"{pre} pre build · {post} post build";

            var message = total == 0
                ? $"Actions that should run for every {subject} belong in the global lists — configure them "
                  + $"once there instead of repeating them on each {subject}."
                : $"{total} global action(s) also run for this {subject}: {breakdown}.";

            return new KUIBanner(KUITone.Accent, message)
                .WithAction(
                    total == 0 ? "Add Global Actions" : "Edit Global Actions",
                    () => BuildManagerWindow.Open("Settings"))
                .Tip("Global actions live on the settings asset and apply project wide.");
        }
    }
}
