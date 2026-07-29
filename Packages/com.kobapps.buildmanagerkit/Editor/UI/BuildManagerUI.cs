using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Small factory helpers shared by every view, so the window keeps a single visual language
    /// instead of each tab inventing its own spacing and colours.
    /// </summary>
    internal static class BuildManagerUI
    {
        internal const string StyleSheetPath = ProjectPaths.PackageRoot + "/Editor/UI/BuildManager.uss";

        /// <summary>Loads the package stylesheet and attaches it to <paramref name="element"/>.</summary>
        internal static void ApplyStyles(VisualElement element)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
                element.styleSheets.Add(styleSheet);
        }

        /// <summary>A titled surface used to group related controls.</summary>
        internal static VisualElement Card(string title = null, string subtitle = null, Color? accent = null)
        {
            var card = new VisualElement();
            card.AddToClassList("bmk-card");

            if (accent.HasValue)
            {
                card.AddToClassList("bmk-card--accent");
                card.style.borderLeftColor = accent.Value;
            }

            if (!string.IsNullOrEmpty(title))
            {
                var label = new Label(title);
                label.AddToClassList("bmk-card__title");
                card.Add(label);
            }

            if (!string.IsNullOrEmpty(subtitle))
            {
                var label = new Label(subtitle);
                label.AddToClassList("bmk-card__subtitle");
                card.Add(label);
            }

            return card;
        }

        /// <summary>A horizontal container.</summary>
        internal static VisualElement Row(params VisualElement[] children)
        {
            var row = new VisualElement();
            row.AddToClassList("bmk-row");

            foreach (var child in children)
            {
                if (child != null)
                    row.Add(child);
            }

            return row;
        }

        /// <summary>A flexible spacer that pushes later siblings to the right.</summary>
        internal static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("bmk-grow");
            return spacer;
        }

        /// <summary>A dimmed helper label that wraps.</summary>
        internal static Label Muted(string text)
        {
            var label = new Label(text);
            label.AddToClassList("bmk-muted");
            return label;
        }

        /// <summary>A small section heading.</summary>
        internal static Label SectionTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("bmk-section-title");
            return label;
        }

        /// <summary>A key/value line used by the summary cards.</summary>
        internal static VisualElement KeyValue(string key, string value, Color? valueColor = null)
        {
            var row = new VisualElement();
            row.AddToClassList("bmk-kv");

            var keyLabel = new Label(key);
            keyLabel.AddToClassList("bmk-kv__key");

            var valueLabel = new Label(string.IsNullOrEmpty(value) ? "—" : value);
            valueLabel.AddToClassList("bmk-kv__value");
            if (valueColor.HasValue)
                valueLabel.style.color = valueColor.Value;

            row.Add(keyLabel);
            row.Add(valueLabel);
            return row;
        }

        /// <summary>A coloured status badge.</summary>
        internal static Label Badge(string text, string modifier)
        {
            var label = new Label(text);
            label.AddToClassList("bmk-badge");
            label.AddToClassList("bmk-badge--" + modifier);
            return label;
        }

        /// <summary>Badge for a build status, coloured by outcome.</summary>
        internal static Label StatusBadge(BuildRunStatus status)
        {
            switch (status)
            {
                case BuildRunStatus.Succeeded: return Badge("SUCCESS", "success");
                case BuildRunStatus.Failed: return Badge("FAILED", "error");
                case BuildRunStatus.Cancelled: return Badge("CANCELLED", "warning");
                default: return Badge("UNKNOWN", "neutral");
            }
        }

        /// <summary>A pill shaped dropdown button with a coloured dot, used for environments.</summary>
        internal static VisualElement Pill(string label, Color dotColor, Action<Rect> onClick, string tooltip = null)
        {
            var dot = new VisualElement();
            dot.AddToClassList("bmk-pill__dot");
            dot.style.backgroundColor = dotColor;

            return BuildPill(dot, label, onClick, tooltip);
        }

        /// <summary>
        /// A pill shaped dropdown button with an image, used where the thing being chosen has a
        /// recognisable icon — most importantly the platform, so it is never mistaken for one of
        /// the neighbouring text dropdowns.
        /// </summary>
        internal static VisualElement Pill(string label, Texture icon, Action<Rect> onClick, string tooltip = null)
        {
            VisualElement badge;

            if (icon != null)
            {
                badge = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit };
                badge.AddToClassList("bmk-pill__icon");
            }
            else
            {
                badge = new VisualElement();
                badge.AddToClassList("bmk-pill__dot");
                badge.style.backgroundColor = new Color(0.6f, 0.6f, 0.6f);
            }

            return BuildPill(badge, label, onClick, tooltip);
        }

        private static VisualElement BuildPill(VisualElement badge, string label, Action<Rect> onClick,
            string tooltip)
        {
            var pill = new VisualElement { tooltip = tooltip };
            pill.AddToClassList("bmk-pill");

            var text = new Label(label);
            text.AddToClassList("bmk-pill__label");

            var caret = new Label("▼");
            caret.AddToClassList("bmk-pill__caret");

            pill.Add(badge);
            pill.Add(text);
            pill.Add(caret);

            pill.RegisterCallback<MouseDownEvent>(evt =>
            {
                onClick?.Invoke(pill.worldBound);
                evt.StopPropagation();
            });

            return pill;
        }

        /// <summary>A primary call to action button.</summary>
        internal static Button PrimaryButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("bmk-button-primary");
            return button;
        }

        /// <summary>The placeholder shown when a list has no entries.</summary>
        internal static VisualElement EmptyState(string message, string actionText = null, Action action = null)
        {
            var container = new VisualElement();
            container.AddToClassList("bmk-card");

            var label = new Label(message);
            label.AddToClassList("bmk-empty");
            container.Add(label);

            if (!string.IsNullOrEmpty(actionText) && action != null)
            {
                var button = PrimaryButton(actionText, action);
                button.style.alignSelf = Align.Center;
                container.Add(button);
            }

            return container;
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

            var card = Card();

            // Wrapping row: on a narrow window the button drops below the text instead of being
            // pushed past the right edge of the pane.
            var row = new VisualElement();
            row.AddToClassList("bmk-row");
            row.AddToClassList("bmk-row--wrap");

            var text = Muted(total == 0
                ? $"Actions that should run for every {subject} belong in the global lists — configure them "
                  + $"once there instead of repeating them on each {subject}."
                : $"{total} global action(s) also run for this {subject}: {breakdown}.");

            text.AddToClassList("bmk-flex-text");

            var edit = new Button(() => BuildManagerWindow.Open("Settings"))
            {
                text = total == 0 ? "Add Global Actions" : "Edit Global Actions",
                tooltip = "Global actions live on the settings asset and apply project wide."
            };
            edit.style.flexShrink = 0;
            edit.style.marginLeft = 0;

            row.Add(text);
            row.Add(edit);
            card.Add(row);

            return card;
        }

        /// <summary>Opens a <see cref="GenericMenu"/> anchored under <paramref name="anchor"/>.</summary>
        internal static void ShowMenu(GenericMenu menu, Rect anchor) =>
            menu.DropDown(new Rect(anchor.x, anchor.yMax, anchor.width, 0));

        /// <summary>A horizontal separator.</summary>
        internal static VisualElement Separator()
        {
            var line = new VisualElement();
            line.style.height = 1;
            line.style.marginTop = 6;
            line.style.marginBottom = 6;
            line.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.25f);
            return line;
        }

        /// <summary>Renders every visible child of a serialized property into a container.</summary>
        /// <param name="container">Element the fields are added to.</param>
        /// <param name="property">Property whose children are drawn.</param>
        /// <param name="serializedObject">Object the fields bind to.</param>
        /// <param name="skip">Field names that are drawn elsewhere and must not repeat.</param>
        internal static void DrawChildren(
            VisualElement container,
            SerializedProperty property,
            SerializedObject serializedObject,
            ICollection<string> skip = null)
        {
            var iterator = property.Copy();

            // Depth, not GetEndProperty: a root iterator straight from SerializedObject.GetIterator()
            // has not been stepped into yet, and asking it for its end property logs
            // "Invalid iteration - you need to call Next(true) on the first element".
            var parentDepth = iterator.depth;

            if (!iterator.NextVisible(true))
                return;

            do
            {
                // Coming back up to the parent's level means the children are exhausted.
                if (iterator.depth <= parentDepth)
                    break;

                if (skip != null && skip.Contains(iterator.name))
                    continue;

                var field = new PropertyField(iterator.Copy());
                field.Bind(serializedObject);
                container.Add(field);
            } while (iterator.NextVisible(false));
        }
    }
}
