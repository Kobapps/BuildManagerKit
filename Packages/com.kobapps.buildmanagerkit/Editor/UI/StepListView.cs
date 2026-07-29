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
    /// Editor for a <c>[SerializeReference] List&lt;BuildStep&gt;</c>: an ordered stack of cards
    /// that can be enabled, reordered, duplicated and removed, plus an "Add Action" menu built
    /// from <see cref="BuildStepRegistry"/>.
    ///
    /// Actions are stored inline on the owning asset, so authoring a pipeline never creates loose
    /// sub-assets and the whole configuration stays in one readable YAML file.
    /// </summary>
    internal sealed class StepListView : VisualElement
    {
        private static readonly HashSet<string> k_HeaderFields = new HashSet<string>
        {
            "m_Guid",
            "m_Enabled"
        };

        private readonly SerializedObject m_SerializedObject;
        private readonly string m_PropertyPath;
        private readonly BuildStepScope m_Scope;
        private readonly BuildStepScopeLevel m_Level;
        private readonly HashSet<string> m_Expanded = new HashSet<string>();
        private readonly VisualElement m_Items;

        /// <summary>Creates the editor.</summary>
        /// <param name="serializedObject">Object owning the list.</param>
        /// <param name="propertyPath">Path of the list property.</param>
        /// <param name="scope">Which step types the Add menu offers.</param>
        /// <param name="title">Heading shown above the list.</param>
        /// <param name="description">Optional explanation shown under the heading.</param>
        /// <param name="level">
        /// Which tier this list belongs to. Non-global lists gain "promote to global" and
        /// "override a global" commands.
        /// </param>
        internal StepListView(
            SerializedObject serializedObject,
            string propertyPath,
            BuildStepScope scope,
            string title,
            string description = null,
            BuildStepScopeLevel level = BuildStepScopeLevel.Global)
        {
            m_SerializedObject = serializedObject;
            m_PropertyPath = propertyPath;
            m_Scope = scope;
            m_Level = level;

            var header = new VisualElement();
            header.AddToClassList("bmk-row");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("bmk-card__title");
            titleLabel.style.marginBottom = 0;

            var addButton = new Button { text = "+ Add Action" };
            addButton.clicked += () => ShowAddMenu(addButton.worldBound);

            header.Add(titleLabel);
            header.Add(BuildManagerUI.Spacer());
            header.Add(addButton);

            Add(header);

            if (!string.IsNullOrEmpty(description))
                Add(BuildManagerUI.Muted(description));

            m_Items = new VisualElement();
            m_Items.AddToClassList("bmk-reorder-host");
            m_Items.style.marginTop = 4;
            Add(m_Items);

            Rebuild();
        }

        /// <summary>Rebuilds every card from the current serialized state.</summary>
        internal void Rebuild()
        {
            m_Items.Clear();
            m_SerializedObject.Update();

            var list = m_SerializedObject.FindProperty(m_PropertyPath);
            if (list == null || !list.isArray)
            {
                m_Items.Add(BuildManagerUI.Muted($"Property '{m_PropertyPath}' was not found."));
                return;
            }

            if (list.arraySize == 0)
            {
                var empty = new Label("No actions yet. Use “Add Action” to build a pipeline.");
                empty.AddToClassList("bmk-empty");
                m_Items.Add(empty);
                return;
            }

            for (var i = 0; i < list.arraySize; i++)
                m_Items.Add(CreateCard(list, i));
        }

        private VisualElement CreateCard(SerializedProperty list, int index)
        {
            var element = list.GetArrayElementAtIndex(index);
            var step = element.managedReferenceValue as BuildStep;

            var card = new VisualElement();
            card.AddToClassList("bmk-step");

            if (step == null)
            {
                card.Add(BuildBrokenHeader(list, index));
                return card;
            }

            if (!step.Enabled)
                card.AddToClassList("bmk-step--disabled");

            var key = step.Guid;
            var expanded = m_Expanded.Contains(key);

            var header = new VisualElement();
            header.AddToClassList("bmk-step__header");

            // Order is execution order, so dragging a card is the primary way to change what runs
            // when. The ⋮ menu keeps Move Up / Move Down for keyboard and long lists.
            var handle = DragReorder.CreateHandle("Drag to reorder — actions run top to bottom");
            DragReorder.Attach(m_Items, card, handle, Move);

            var indexLabel = new Label((index + 1).ToString());
            indexLabel.AddToClassList("bmk-step__index");

            var foldout = new Label(expanded ? "▼" : "▶");
            foldout.style.width = 14;
            foldout.style.fontSize = 9;

            var enabledProperty = element.FindPropertyRelative("m_Enabled");
            var enabledToggle = new Toggle { value = enabledProperty != null && enabledProperty.boolValue };
            enabledToggle.style.marginRight = 4;
            enabledToggle.tooltip = "Enable or skip this action.";
            enabledToggle.RegisterValueChangedCallback(evt =>
            {
                if (enabledProperty == null)
                    return;

                enabledProperty.boolValue = evt.newValue;
                Apply();
                Rebuild();
            });

            var title = new Label(step.Title);
            title.AddToClassList("bmk-step__title");
            title.tooltip = BuildStepRegistry.GetTooltip(step.GetType());

            var summary = new Label(SafeSummary(step));
            summary.AddToClassList("bmk-step__summary");

            var menuButton = new Button { text = "⋮" };
            menuButton.style.width = 22;
            menuButton.clicked += () => ShowItemMenu(menuButton.worldBound, index, list.arraySize);

            header.Add(handle);
            header.Add(indexLabel);
            header.Add(foldout);
            header.Add(enabledToggle);
            header.Add(title);

            if (step.Key.Length > 0)
            {
                var keyBadge = new Label("⇄ " + step.Key)
                {
                    tooltip = "Override key. Among actions sharing this key only the most specific runs: "
                              + "profile beats environment beats global."
                };

                keyBadge.AddToClassList("bmk-step__key");
                header.Add(keyBadge);
            }

            header.Add(summary);
            header.Add(menuButton);

            // Only the inert parts of the header toggle the card: the enable toggle and the menu
            // button own their own clicks, and their inner elements are what actually receive the
            // event, so testing evt.target against them is not reliable.
            void ToggleExpanded(MouseDownEvent evt)
            {
                if (evt.button != 0)
                    return;

                if (!m_Expanded.Remove(key))
                    m_Expanded.Add(key);

                Rebuild();
                evt.StopPropagation();
            }

            indexLabel.RegisterCallback<MouseDownEvent>(ToggleExpanded);
            foldout.RegisterCallback<MouseDownEvent>(ToggleExpanded);
            title.RegisterCallback<MouseDownEvent>(ToggleExpanded);
            summary.RegisterCallback<MouseDownEvent>(ToggleExpanded);

            card.Add(header);

            if (expanded)
            {
                var body = new VisualElement();
                body.AddToClassList("bmk-step__body");
                BuildManagerUI.DrawChildren(body, element, m_SerializedObject, k_HeaderFields);
                card.Add(body);
            }

            return card;
        }

        private VisualElement BuildBrokenHeader(SerializedProperty list, int index)
        {
            var header = new VisualElement();
            header.AddToClassList("bmk-step__header");

            var label = new Label("Missing action — the script was removed or renamed");
            label.AddToClassList("bmk-step__title");
            label.style.color = new Color(0.97f, 0.32f, 0.29f);

            var remove = new Button(() =>
            {
                RemoveAt(list, index);
                Rebuild();
            }) { text = "Remove" };

            header.Add(label);
            header.Add(BuildManagerUI.Spacer());
            header.Add(remove);
            return header;
        }

        private static string SafeSummary(BuildStep step)
        {
            try
            {
                return step.Summary ?? string.Empty;
            }
            catch (Exception exception)
            {
                return "summary failed: " + exception.Message;
            }
        }

        private void ShowAddMenu(Rect anchor)
        {
            var menu = new GenericMenu();
            var any = false;

            foreach (var descriptor in BuildStepRegistry.GetDescriptors(m_Scope))
            {
                any = true;
                var captured = descriptor;
                menu.AddItem(new GUIContent(captured.MenuPath), false, () => AddStep(captured));
            }

            if (!any)
                menu.AddDisabledItem(new GUIContent("No actions available for this list"));

            AppendOverrideGlobalMenu(menu);
            BuildManagerUI.ShowMenu(menu, anchor);
        }

        /// <summary>
        /// Adds an "Override Global" submenu listing the matching global actions. Choosing one
        /// copies it into this list with a shared override key, so this copy replaces the global
        /// original for this environment or profile only.
        /// </summary>
        private void AppendOverrideGlobalMenu(GenericMenu menu)
        {
            if (m_Level == BuildStepScopeLevel.Global)
                return;

            var settings = BuildManagerSettings.InstanceOrNull;
            if (settings == null)
                return;

            var globals = GetGlobalList(settings).Where(step => step != null).ToList();
            if (globals.Count == 0)
                return;

            menu.AddSeparator(string.Empty);

            foreach (var global in globals)
            {
                var captured = global;
                var label = new GUIContent("Override Global/" + Sanitize(captured.Title));

                if (ListContainsKeyOf(captured))
                    menu.AddDisabledItem(label);
                else
                    menu.AddItem(label, false, () => OverrideGlobal(settings, captured));
            }
        }

        private bool ListContainsKeyOf(BuildStep global)
        {
            var key = global.Key;
            if (key.Length == 0)
                return false;

            var list = m_SerializedObject.FindProperty(m_PropertyPath);

            for (var i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).managedReferenceValue is BuildStep step &&
                    string.Equals(step.Key, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void OverrideGlobal(BuildManagerSettings settings, BuildStep global)
        {
            // The pair needs a shared key. Generate one on the global action the first time it is
            // overridden, so users never have to invent key strings by hand.
            var key = global.Key;

            if (key.Length == 0)
            {
                key = "global-" + global.Guid.Substring(0, 8);
                global.SetKey(key);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }

            var copy = Clone(global);
            if (copy == null)
                return;

            copy.SetKey(key);

            var list = m_SerializedObject.FindProperty(m_PropertyPath);
            var index = list.arraySize;
            list.arraySize++;
            list.GetArrayElementAtIndex(index).managedReferenceValue = copy;

            var guid = list.GetArrayElementAtIndex(index).FindPropertyRelative("m_Guid");
            if (guid != null)
                guid.stringValue = System.Guid.NewGuid().ToString("N");

            Apply();

            m_Expanded.Add(copy.Guid);
            Rebuild();

            Debug.Log($"[BuildManagerKit] '{copy.Title}' now overrides the global action (key '{key}').");
        }

        private IReadOnlyList<BuildStep> GetGlobalList(BuildManagerSettings settings)
        {
            switch (m_Scope)
            {
                case BuildStepScope.PostBuild: return settings.GlobalPostBuildSteps;
                case BuildStepScope.EnvironmentActivate: return settings.GlobalOnActivateSteps;
                default: return settings.GlobalPreBuildSteps;
            }
        }

        private string GetGlobalListPath()
        {
            switch (m_Scope)
            {
                case BuildStepScope.PostBuild: return "m_GlobalPostBuildSteps";
                case BuildStepScope.EnvironmentActivate: return "m_GlobalOnActivateSteps";
                default: return "m_GlobalPreBuildSteps";
            }
        }

        private static BuildStep Clone(BuildStep source) =>
            source == null ? null : JsonUtility.FromJson(JsonUtility.ToJson(source), source.GetType()) as BuildStep;

        private static string Sanitize(string label) => label?.Replace('/', '∕') ?? string.Empty;

        private void ShowItemMenu(Rect anchor, int index, int count)
        {
            var menu = new GenericMenu();

            if (index > 0)
                menu.AddItem(new GUIContent("Move Up"), false, () => Move(index, index - 1));
            else
                menu.AddDisabledItem(new GUIContent("Move Up"));

            if (index < count - 1)
                menu.AddItem(new GUIContent("Move Down"), false, () => Move(index, index + 1));
            else
                menu.AddDisabledItem(new GUIContent("Move Down"));

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Duplicate"), false, () => Duplicate(index));

            if (m_Level != BuildStepScopeLevel.Global)
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Make Global (move)"), false, () => PromoteToGlobal(index, true));
                menu.AddItem(new GUIContent("Make Global (copy)"), false, () => PromoteToGlobal(index, false));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Remove"), false, () =>
            {
                var list = m_SerializedObject.FindProperty(m_PropertyPath);
                RemoveAt(list, index);
                Rebuild();
            });

            BuildManagerUI.ShowMenu(menu, anchor);
        }

        private void AddStep(BuildStepDescriptor descriptor)
        {
            var list = m_SerializedObject.FindProperty(m_PropertyPath);
            var index = list.arraySize;

            list.arraySize++;
            var element = list.GetArrayElementAtIndex(index);
            var instance = descriptor.CreateInstance();
            element.managedReferenceValue = instance;

            Apply();

            m_Expanded.Add(instance.Guid);
            Rebuild();
        }

        private void Duplicate(int index)
        {
            var list = m_SerializedObject.FindProperty(m_PropertyPath);
            var source = list.GetArrayElementAtIndex(index).managedReferenceValue as BuildStep;

            if (source == null)
                return;

            var copy = JsonUtility.FromJson(JsonUtility.ToJson(source), source.GetType()) as BuildStep;
            if (copy == null)
                return;

            list.InsertArrayElementAtIndex(index + 1);
            var element = list.GetArrayElementAtIndex(index + 1);
            element.managedReferenceValue = copy;

            // The copy inherited the original identifier; give it a fresh one so foldout state
            // and any future per-action bookkeeping stay independent.
            var guid = element.FindPropertyRelative("m_Guid");
            if (guid != null)
                guid.stringValue = System.Guid.NewGuid().ToString("N");

            Apply();
            Rebuild();
        }

        /// <summary>
        /// Promotes an action to the matching project-wide list, so it applies to every
        /// environment and profile instead of just this one.
        /// </summary>
        /// <param name="index">Index in this list.</param>
        /// <param name="move">Remove the local copy afterwards, rather than leaving a duplicate.</param>
        private void PromoteToGlobal(int index, bool move)
        {
            var settings = BuildManagerSettings.Instance;
            if (settings == m_SerializedObject.targetObject)
                return;

            var list = m_SerializedObject.FindProperty(m_PropertyPath);
            var source = list.GetArrayElementAtIndex(index).managedReferenceValue as BuildStep;
            var copy = Clone(source);

            if (copy == null)
                return;

            var globalObject = new SerializedObject(settings);
            var globalList = globalObject.FindProperty(GetGlobalListPath());
            var target = globalList.arraySize;

            globalList.arraySize++;
            globalList.GetArrayElementAtIndex(target).managedReferenceValue = copy;

            var guid = globalList.GetArrayElementAtIndex(target).FindPropertyRelative("m_Guid");
            if (guid != null)
                guid.stringValue = System.Guid.NewGuid().ToString("N");

            globalObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            if (move)
                RemoveAt(list, index);

            Rebuild();

            Debug.Log($"[BuildManagerKit] '{copy.Title}' {(move ? "moved" : "copied")} to the global "
                      + $"{m_Scope} list; it now applies project wide.");
        }

        private void Move(int from, int to)
        {
            var list = m_SerializedObject.FindProperty(m_PropertyPath);
            list.MoveArrayElement(from, to);
            Apply();
            Rebuild();
        }

        private void RemoveAt(SerializedProperty list, int index)
        {
            var before = list.arraySize;
            list.DeleteArrayElementAtIndex(index);

            // Object reference arrays need a second delete to drop the nulled slot; managed
            // reference arrays do not. Handle both so the behaviour is identical either way.
            if (list.arraySize == before)
                list.DeleteArrayElementAtIndex(index);

            Apply();
        }

        private void Apply()
        {
            m_SerializedObject.ApplyModifiedProperties();

            if (m_SerializedObject.targetObject != null)
                EditorUtility.SetDirty(m_SerializedObject.targetObject);
        }
    }
}
