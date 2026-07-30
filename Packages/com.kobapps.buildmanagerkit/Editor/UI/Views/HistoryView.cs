using System;
using System.Linq;
using EditorCoreKit.Editor;
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

            var split = new EckSplitView(330f, false, "BuildManagerKit.History");
            split.First.Add(BuildList());
            split.Second.Add(BuildDetail());

            root.Add(split);
            return root;
        }

        /// <inheritdoc />
        internal override void OnBuildStateChanged() => m_Selected = null;

        private VisualElement BuildToolbar()
        {
            var toolbar = new EckToolbar();

            var search = new EckSearchField(
                "Filter by profile, environment, version, branch or message",
                value =>
                {
                    m_Query = value;
                    Window.RefreshCurrentView();
                },
                280f);

            search.SetValueWithoutNotify(m_Query);

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

            toolbar.Add(search);
            toolbar.Add(statusField);
            toolbar.PushRight();
            toolbar.Add(EckButton.Danger("Clear History", () =>
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
            }));

            return toolbar;
        }

        private VisualElement BuildList()
        {
            var container = new ScrollView();
            container.style.flexGrow = 1;
            container.style.minHeight = 0;
            container.style.marginRight = 10;

            var entries = BuildHistory.Search(m_Query, m_StatusFilter).ToArray();

            if (entries.Length == 0)
            {
                container.Add(EckEmptyState.Line(
                    BuildHistory.Entries.Count == 0
                        ? "No builds recorded yet."
                        : "No runs match the current filter."));

                return container;
            }

            m_Selected ??= entries[0];

            var list = new VisualElement();
            list.AddToClassList(EckClass.List);

            foreach (var entry in entries)
            {
                var captured = entry;

                var row = new EckListRow(
                        $"{entry.result.profileName} · {entry.result.environmentId}",
                        () =>
                        {
                            m_Selected = captured;
                            Window.RefreshCurrentView();
                        })
                    .WithDot(BuildManagerUI.ToneOf(entry.result.status), entry.result.status.ToString())
                    .WithSublabel(
                        $"{entry.result.version}+{entry.result.buildNumber} · "
                        + $"{entry.FinishedAt:yyyy-MM-dd HH:mm} · "
                        + BuildTargetUtility.FormatDuration(TimeSpan.FromSeconds(entry.result.durationSeconds)));

                row.Selected = entry == m_Selected;
                list.Add(row);
            }

            container.Add(list);
            return container;
        }

        private VisualElement BuildDetail()
        {
            // A ScrollView: the metadata card plus a full build log easily exceeds the window.
            var detail = new ScrollView();
            detail.AddToClassList(EckClass.Detail);

            if (m_Selected == null)
            {
                detail.Add(EckEmptyState.Line("Select a run to inspect its log."));
                return detail;
            }

            var result = m_Selected.result;
            var card = new EckCard($"{result.profileName} · {result.target}");

            card.Header.Insert(0, BuildManagerUI.StatusBadge(result.status));

            if (!string.IsNullOrEmpty(result.outputPath))
            {
                card.WithHeaderAction(EckButton.Secondary("Reveal Output",
                    () => EditorUtility.RevealInFinder(result.outputPath)));
            }

            card.WithHeaderAction(EckButton.Secondary("Copy JSON",
                () => EditorGUIUtility.systemCopyBuffer = result.ToJson()));

            card.Add(EckText.KeyValue("Environment", result.environmentId));
            card.Add(EckText.KeyValue("Version", $"{result.version}+{result.buildNumber}"));
            card.Add(EckText.KeyValue("Duration",
                BuildTargetUtility.FormatDuration(TimeSpan.FromSeconds(result.durationSeconds))));
            card.Add(EckText.KeyValue("Size", BuildTargetUtility.FormatSize(result.outputSizeBytes)));
            card.Add(EckText.KeyValue("Errors / warnings", $"{result.errors} / {result.warnings}"));
            card.Add(EckText.KeyValue("Git",
                string.IsNullOrEmpty(result.gitCommit) ? "—" : $"{result.gitBranch}@{result.gitCommit}"));
            card.Add(EckText.KeyValue("Output", result.outputPath));

            if (!string.IsNullOrEmpty(result.message))
            {
                card.Add(EckText.KeyValue("Message", result.message,
                    result.Succeeded ? (Color?)null : EckTheme.Error));
            }

            if (result.artifacts is { Length: > 0 })
            {
                card.Add(EckText.SectionTitle("Artifacts"));

                var artifacts = new VisualElement();
                artifacts.AddToClassList(EckClass.List);

                foreach (var artifact in result.artifacts)
                {
                    var captured = artifact;
                    artifacts.Add(new EckListRow(artifact)
                        .WithAction(EckButton.Secondary("Reveal", () => EditorUtility.RevealInFinder(captured))));
                }

                card.Add(artifacts);
            }

            detail.Add(card);

            var logCard = new EckCard("Log");
            var text = m_Selected.HasLog ? BuildHistory.ReadLog(m_Selected) : result.log;

            if (string.IsNullOrEmpty(text))
            {
                logCard.Add(EckEmptyState.Line("No log was stored for this run."));
            }
            else
            {
                var console = new EckLogConsole();
                console.style.minHeight = 220;
                console.SetPlainText(text);
                logCard.Add(console);
            }

            detail.Add(logCard);
            return detail;
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
