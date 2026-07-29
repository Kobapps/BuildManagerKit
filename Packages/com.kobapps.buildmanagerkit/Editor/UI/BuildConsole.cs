using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Structured, colour coded, searchable build console.
    ///
    /// Used live while a build runs and, in the History tab, to replay the persisted log of any
    /// past run. Severity toggles and the search box filter the same in-memory list, so a long
    /// IL2CPP log can be reduced to its warnings in one click.
    /// </summary>
    internal sealed class BuildConsole : VisualElement
    {
        private const int k_MaxEntries = 5000;

        private readonly List<BuildLogEntry> m_Entries = new List<BuildLogEntry>();
        private readonly ScrollView m_Scroll;
        private readonly ToolbarSearchFieldCompat m_Search;
        private readonly Toggle m_ShowInfo;
        private readonly Toggle m_ShowWarnings;
        private readonly Toggle m_ShowErrors;
        private readonly Toggle m_AutoScroll;
        private readonly Label m_Counters;

        /// <summary>Creates the console.</summary>
        /// <param name="showToolbar">Hide the toolbar when the host provides its own filters.</param>
        internal BuildConsole(bool showToolbar = true)
        {
            style.flexGrow = 1;

            if (showToolbar)
            {
                var toolbar = new VisualElement();
                toolbar.AddToClassList("bmk-row");
                toolbar.style.marginBottom = 4;

                m_Search = new ToolbarSearchFieldCompat("Search…");
                m_Search.style.width = 190;
                m_Search.OnValueChanged += _ => Render();

                m_ShowInfo = FilterToggle("Info", true);
                m_ShowWarnings = FilterToggle("Warnings", true);
                m_ShowErrors = FilterToggle("Errors", true);
                m_AutoScroll = FilterToggle("Follow", true);

                m_Counters = new Label();
                m_Counters.AddToClassList("bmk-muted");

                var copy = new Button(CopyToClipboard) { text = "Copy" };
                var clear = new Button(ClearLog) { text = "Clear" };

                toolbar.Add(m_Search);
                toolbar.Add(m_ShowInfo);
                toolbar.Add(m_ShowWarnings);
                toolbar.Add(m_ShowErrors);
                toolbar.Add(m_AutoScroll);
                toolbar.Add(BuildManagerUI.Spacer());
                toolbar.Add(m_Counters);
                toolbar.Add(copy);
                toolbar.Add(clear);

                Add(toolbar);
            }

            m_Scroll = new ScrollView(ScrollViewMode.Vertical);
            m_Scroll.AddToClassList("bmk-console");
            Add(m_Scroll);

            Render();
        }

        /// <summary>Replaces the contents with a fresh set of entries.</summary>
        internal void SetEntries(IEnumerable<BuildLogEntry> entries)
        {
            m_Entries.Clear();
            if (entries != null)
                m_Entries.AddRange(entries);

            Trim();
            Render();
        }

        /// <summary>Parses a persisted log file and shows it.</summary>
        internal void SetPlainText(string text)
        {
            m_Entries.Clear();

            if (!string.IsNullOrEmpty(text))
            {
                foreach (var line in text.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    m_Entries.Add(new BuildLogEntry
                    {
                        level = LevelFromLine(line),
                        message = line.TrimEnd('\r')
                    });
                }
            }

            Trim();
            Render();
        }

        /// <summary>Appends one entry, keeping the view scrolled to the bottom when following.</summary>
        internal void Append(BuildLogEntry entry)
        {
            m_Entries.Add(entry);
            Trim();

            if (!Passes(entry))
            {
                UpdateCounters();
                return;
            }

            m_Scroll.Add(CreateLine(entry));
            UpdateCounters();

            if (m_AutoScroll == null || m_AutoScroll.value)
                ScrollToBottom();
        }

        /// <summary>Removes every log entry. Named to avoid hiding <c>VisualElement.Clear</c>.</summary>
        internal void ClearLog()
        {
            m_Entries.Clear();
            Render();
        }

        private Toggle FilterToggle(string label, bool value)
        {
            var toggle = new Toggle(label) { value = value };
            toggle.style.marginRight = 8;
            toggle.RegisterValueChangedCallback(_ => Render());
            return toggle;
        }

        private void Trim()
        {
            if (m_Entries.Count > k_MaxEntries)
                m_Entries.RemoveRange(0, m_Entries.Count - k_MaxEntries);
        }

        private void Render()
        {
            if (m_Scroll == null)
                return;

            m_Scroll.Clear();

            foreach (var entry in m_Entries.Where(Passes))
                m_Scroll.Add(CreateLine(entry));

            UpdateCounters();

            if (m_AutoScroll == null || m_AutoScroll.value)
                ScrollToBottom();
        }

        private void UpdateCounters()
        {
            if (m_Counters == null)
                return;

            var warnings = m_Entries.Count(entry => entry.level == BuildLogLevel.Warning);
            var errors = m_Entries.Count(entry => entry.level == BuildLogLevel.Error);

            m_Counters.text = $"{m_Entries.Count} lines · {warnings} warnings · {errors} errors  ";
        }

        private bool Passes(BuildLogEntry entry)
        {
            switch (entry.level)
            {
                case BuildLogLevel.Warning when m_ShowWarnings != null && !m_ShowWarnings.value:
                case BuildLogLevel.Error when m_ShowErrors != null && !m_ShowErrors.value:
                    return false;
                case BuildLogLevel.Debug:
                case BuildLogLevel.Info:
                case BuildLogLevel.Success:
                    if (m_ShowInfo != null && !m_ShowInfo.value)
                        return false;
                    break;
            }

            var needle = m_Search != null ? m_Search.Value : null;
            if (string.IsNullOrWhiteSpace(needle))
                return true;

            return (entry.message ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                   || (entry.scope ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Label CreateLine(BuildLogEntry entry)
        {
            var label = new Label(entry.elapsedSeconds > 0 ? entry.Format() : entry.message)
            {
                selection = { isSelectable = true }
            };

            label.AddToClassList("bmk-console__line");
            label.AddToClassList("bmk-console__line--" + entry.level.ToString().ToLowerInvariant());
            return label;
        }

        private void ScrollToBottom() =>
            schedule.Execute(() => m_Scroll.verticalScroller.value = m_Scroll.verticalScroller.highValue)
                .StartingIn(1);

        private void CopyToClipboard() =>
            EditorGUIUtility.systemCopyBuffer = string.Join("\n", m_Entries.Where(Passes).Select(e => e.Format()));

        private static BuildLogLevel LevelFromLine(string line)
        {
            if (line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                return BuildLogLevel.Error;

            if (line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
                return BuildLogLevel.Warning;

            if (line.StartsWith("SUCCESS", StringComparison.OrdinalIgnoreCase))
                return BuildLogLevel.Success;

            return BuildLogLevel.Info;
        }
    }

    /// <summary>
    /// A tiny search field built from a plain <see cref="TextField"/>.
    ///
    /// <c>ToolbarSearchField</c> lives in the editor toolbar namespace and drags a fixed style with
    /// it; this keeps the console self contained and consistent with the rest of the window.
    /// </summary>
    internal sealed class ToolbarSearchFieldCompat : VisualElement
    {
        private readonly TextField m_Field;

        /// <summary>Raised whenever the query changes.</summary>
        internal event Action<string> OnValueChanged;

        /// <summary>The current query.</summary>
        internal string Value => m_Field.value;

        /// <summary>Creates the field with placeholder text.</summary>
        internal ToolbarSearchFieldCompat(string placeholder)
        {
            AddToClassList("bmk-row");

            m_Field = new TextField { value = string.Empty };
            m_Field.style.flexGrow = 1;
            m_Field.tooltip = placeholder;
            m_Field.RegisterValueChangedCallback(evt => OnValueChanged?.Invoke(evt.newValue));

            var clear = new Button(() =>
            {
                m_Field.value = string.Empty;
                OnValueChanged?.Invoke(string.Empty);
            }) { text = "×" };
            clear.style.width = 18;

            Add(m_Field);
            Add(clear);
        }

        /// <summary>Sets the query without raising the change event.</summary>
        internal void SetValueWithoutNotify(string value) => m_Field.SetValueWithoutNotify(value);
    }
}
