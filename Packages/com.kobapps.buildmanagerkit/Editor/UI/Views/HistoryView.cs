using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// The build log dashboard: every past run with its outcome, timing and artifacts, and the
    /// full persisted log of whichever run is selected — searchable and filterable by severity.
    /// </summary>
    internal sealed class HistoryView : BuildManagerView
    {
        private BuildHistoryEntry m_Selected;
        private string m_Query = string.Empty;
        private BuildRunStatus? m_StatusFilter;

        /// <inheritdoc />
        internal override string Title => "History";

        /// <inheritdoc />
        internal override string Badge => BuildHistory.Entries.Count.ToString();

        /// <inheritdoc />
        internal override VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexGrow = 1;
            root.style.minHeight = 0;

            root.Add(BuildToolbar());

            var split = new VisualElement();
            split.AddToClassList("bmk-split");

            split.Add(BuildList());
            split.Add(BuildDetail());

            root.Add(split);
            return root;
        }

        /// <inheritdoc />
        internal override void OnBuildStateChanged() => m_Selected = null;

        private VisualElement BuildToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList("bmk-toolbar");

            var search = new ToolbarSearchFieldCompat("Filter by profile, environment, version, branch or message");
            search.style.width = 280;
            search.SetValueWithoutNotify(m_Query);
            search.OnValueChanged += value =>
            {
                m_Query = value;
                Window.RefreshCurrentView();
            };

            var statusField = new EnumField("Status", StatusFilterValue.All);
            statusField.style.width = 190;
            statusField.value = m_StatusFilter.HasValue
                ? (StatusFilterValue)(int)m_StatusFilter.Value
                : StatusFilterValue.All;
            statusField.RegisterValueChangedCallback(evt =>
            {
                var value = (StatusFilterValue)evt.newValue;
                m_StatusFilter = value == StatusFilterValue.All ? (BuildRunStatus?)null : (BuildRunStatus)(int)value;
                Window.RefreshCurrentView();
            });

            var clear = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog(
                        "Clear history",
                        "Remove every history entry and delete the log files they point at?",
                        "Clear",
                        "Cancel"))
                    return;

                BuildHistory.Clear();
                m_Selected = null;
                Window.RefreshCurrentView();
            }) { text = "Clear History" };
            clear.AddToClassList("bmk-button-danger");

            toolbar.Add(search);
            toolbar.Add(statusField);
            toolbar.Add(BuildManagerUI.Spacer());
            toolbar.Add(clear);

            return toolbar;
        }

        private VisualElement BuildList()
        {
            var container = new ScrollView();
            container.style.width = 330;
            container.style.minWidth = 260;
            container.style.marginRight = 10;
            container.style.flexGrow = 0;
            container.style.flexShrink = 0;
            container.style.minHeight = 0;

            var entries = BuildHistory.Search(m_Query, m_StatusFilter).ToArray();

            if (entries.Length == 0)
            {
                container.Add(BuildManagerUI.Muted(
                    BuildHistory.Entries.Count == 0
                        ? "No builds recorded yet."
                        : "No runs match the current filter."));

                return container;
            }

            m_Selected ??= entries[0];

            var list = new VisualElement();
            list.AddToClassList("bmk-list");

            foreach (var entry in entries)
            {
                var captured = entry;

                var item = new VisualElement();
                item.AddToClassList("bmk-list-item");
                item.style.height = 38;
                if (entry == m_Selected)
                    item.AddToClassList("bmk-list-item--selected");

                var dot = new VisualElement();
                dot.AddToClassList("bmk-pill__dot");
                dot.style.backgroundColor = StatusColor(entry.result.status);

                var text = new VisualElement();
                text.AddToClassList("bmk-grow");

                var line1 = new Label($"{entry.result.profileName} · {entry.result.environmentId}");
                line1.style.fontSize = 11;

                var line2 = new Label(
                    $"{entry.result.version}+{entry.result.buildNumber} · "
                    + $"{entry.FinishedAt:yyyy-MM-dd HH:mm} · "
                    + BuildTargetUtility.FormatDuration(TimeSpan.FromSeconds(entry.result.durationSeconds)));
                line2.AddToClassList("bmk-muted");
                line2.style.fontSize = 10;

                text.Add(line1);
                text.Add(line2);

                item.Add(dot);
                item.Add(text);

                item.RegisterCallback<MouseDownEvent>(_ =>
                {
                    m_Selected = captured;
                    Window.RefreshCurrentView();
                });

                list.Add(item);
            }

            container.Add(list);
            return container;
        }

        private VisualElement BuildDetail()
        {
            // A ScrollView: the metadata card plus a full build log easily exceeds the window.
            var detail = new ScrollView();
            detail.AddToClassList("bmk-detail");

            if (m_Selected == null)
            {
                detail.Add(BuildManagerUI.Muted("Select a run to inspect its log."));
                return detail;
            }

            var result = m_Selected.result;
            var card = BuildManagerUI.Card();

            var header = new VisualElement();
            header.AddToClassList("bmk-row");
            header.Add(BuildManagerUI.StatusBadge(result.status));

            var title = new Label($"{result.profileName} · {result.target}");
            title.AddToClassList("bmk-card__title");
            title.style.marginBottom = 0;
            header.Add(title);
            header.Add(BuildManagerUI.Spacer());

            if (!string.IsNullOrEmpty(result.outputPath))
            {
                header.Add(new Button(() => EditorUtility.RevealInFinder(result.outputPath))
                {
                    text = "Reveal Output"
                });
            }

            header.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = result.ToJson())
            {
                text = "Copy JSON"
            });

            card.Add(header);

            card.Add(BuildManagerUI.KeyValue("Environment", result.environmentId));
            card.Add(BuildManagerUI.KeyValue("Version", $"{result.version}+{result.buildNumber}"));
            card.Add(BuildManagerUI.KeyValue("Duration",
                BuildTargetUtility.FormatDuration(TimeSpan.FromSeconds(result.durationSeconds))));
            card.Add(BuildManagerUI.KeyValue("Size", BuildTargetUtility.FormatSize(result.outputSizeBytes)));
            card.Add(BuildManagerUI.KeyValue("Errors / warnings", $"{result.errors} / {result.warnings}"));
            card.Add(BuildManagerUI.KeyValue("Git",
                string.IsNullOrEmpty(result.gitCommit) ? "—" : $"{result.gitBranch}@{result.gitCommit}"));
            card.Add(BuildManagerUI.KeyValue("Output", result.outputPath));

            if (!string.IsNullOrEmpty(result.message))
                card.Add(BuildManagerUI.KeyValue("Message", result.message,
                    result.Succeeded ? (Color?)null : new Color(0.97f, 0.32f, 0.29f)));

            if (result.artifacts is { Length: > 0 })
            {
                card.Add(BuildManagerUI.SectionTitle("Artifacts"));
                foreach (var artifact in result.artifacts)
                {
                    var row = new VisualElement();
                    row.AddToClassList("bmk-row");

                    var label = new Label(artifact);
                    label.AddToClassList("bmk-grow");
                    label.style.fontSize = 10;

                    var reveal = new Button(() => EditorUtility.RevealInFinder(artifact)) { text = "Reveal" };
                    reveal.style.height = 16;

                    row.Add(label);
                    row.Add(reveal);
                    card.Add(row);
                }
            }

            detail.Add(card);

            var logCard = BuildManagerUI.Card("Log");
            var console = new BuildConsole();
            console.style.minHeight = 220;

            var text = m_Selected.HasLog ? BuildHistory.ReadLog(m_Selected) : result.log;
            if (string.IsNullOrEmpty(text))
                logCard.Add(BuildManagerUI.Muted("No log was stored for this run."));
            else
                console.SetPlainText(text);

            logCard.Add(console);
            detail.Add(logCard);

            return detail;
        }

        private static Color StatusColor(BuildRunStatus status)
        {
            switch (status)
            {
                case BuildRunStatus.Succeeded: return new Color(0.25f, 0.73f, 0.31f);
                case BuildRunStatus.Failed: return new Color(0.97f, 0.32f, 0.29f);
                case BuildRunStatus.Cancelled: return new Color(0.82f, 0.60f, 0.13f);
                default: return Color.gray;
            }
        }

        /// <summary>Status filter values, mirroring <see cref="BuildRunStatus"/> plus "all".</summary>
        private enum StatusFilterValue
        {
            All = 0,
            Succeeded = 1,
            Failed = 2,
            Cancelled = 3
        }
    }
}
