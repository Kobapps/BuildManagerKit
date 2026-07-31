using System;
using System.Collections.Generic;
using System.Linq;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Editor for a <c>[SerializeReference] List&lt;BuildStep&gt;</c>: an ordered stack of cards
    /// that can be enabled, reordered, duplicated and removed, plus an "Add Action" menu built
    /// from <see cref="BuildStepRegistry"/>.
    ///
    /// Each card is an <see cref="KUIExpandableCard"/> and the grip is EditorCoreKit's, so the
    /// list looks and drags like every other ordered stack of configuration in the editor. The
    /// rows are still built by hand rather than through <c>KUIReorderableList</c>: the backing
    /// store is a <c>SerializedProperty</c> array, not an <c>IList&lt;T&gt;</c>, so every move has
    /// to go through <c>MoveArrayElement</c> to stay undoable.
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

            var header = new KUIToolbar();
            header.Add(KUIText.SectionTitle(title));
            header.PushRight();
            header.Add(KUIDropdownButton.Create(KUIIcons.Plus + " Add Action", BuildAddMenu));
            Add(header);

            if (!string.IsNullOrEmpty(description))
                Add(KUIText.Muted(description));

            m_Items = new VisualElement();

            // The reorder host is the positioning context the drop placeholder is measured against;
            // without it the greyed slot lands relative to the wrong element.
            m_Items.AddToClassList(KUIClass.ReorderHost);
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
                m_Items.Add(KUIText.Muted($"Property '{m_PropertyPath}' was not found."));
                return;
            }

            if (list.arraySize == 0)
            {
                m_Items.Add(KUIEmptyState.Line("No actions yet. Use “Add Action” to build a pipeline."));
                return;
            }

            for (var i = 0; i < list.arraySize; i++)
                m_Items.Add(CreateCard(list, i));
        }

        private VisualElement CreateCard(SerializedProperty list, int index)
        {
            var element = list.GetArrayElementAtIndex(index);
            var step = element.managedReferenceValue as BuildStep;

            if (step == null)
                return BuildBrokenCard(list, index);

            var key = step.Guid;
            var count = list.arraySize;

            var expanded = m_Expanded.Contains(key);
            var path = element.propertyPath;

            var card = new KUIExpandableCard(step.Title, SafeSummary(step), expanded)
                .WithIndex(index);

            card.Header.tooltip = BuildStepRegistry.GetTooltip(step.GetType());

            // The body is built the first time the card is opened rather than up front: a settings
            // asset can carry a few dozen actions, and a PropertyField for each of them is not free.
            var populated = expanded;

            // Remembered by guid rather than by index, so opening a card and then dragging it does
            // not leave a different action expanded.
            card.ExpandedChanged += isExpanded =>
            {
                if (!isExpanded)
                {
                    m_Expanded.Remove(key);
                    return;
                }

                m_Expanded.Add(key);

                if (populated)
                    return;

                populated = true;
                DrawBody(card, path);
            };

            var enabledProperty = element.FindPropertyRelative("m_Enabled");
            card.WithEnableToggle(enabledProperty != null && enabledProperty.boolValue, value =>
            {
                if (enabledProperty == null)
                    return;

                enabledProperty.boolValue = value;
                Apply();
            });

            if (step.Key.Length > 0)
            {
                card.WithTag(
                    "⇄ " + step.Key,
                    KUITone.Accent,
                    "Override key. Among actions sharing this key only the most specific runs: "
                    + "profile beats environment beats global.");
            }

            card.WithOverflowMenu(menu => BuildItemMenu(menu, index, count));

            // Order is execution order, so dragging a card is the primary way to change what runs
            // when. The ⋮ menu keeps Move Up / Move Down for keyboard and long lists.
            var handle = KUIDragReorder.CreateHandle("Drag to reorder — actions run top to bottom");
            card.Header.Insert(0, handle);
            KUIDragReorder.Attach(m_Items, card, handle, Move);

            if (expanded)
                DrawBody(card, path);

            return card;
        }

        /// <summary>
        /// Draws one action's fields into its card body.
        ///
        /// The property is looked up again from its path rather than captured: a card outlives
        /// several <c>ApplyModifiedProperties</c> calls, and a stale <c>SerializedProperty</c>
        /// draws the wrong action rather than failing.
        /// </summary>
        private void DrawBody(VisualElement card, string propertyPath)
        {
            var element = m_SerializedObject.FindProperty(propertyPath);

            if (element != null)
                KUIProperty.DrawChildren(card, element, m_SerializedObject, k_HeaderFields);
        }

        /// <summary>
        /// The card shown for a slot whose script was removed or renamed. It cannot be edited, only
        /// found and dropped — so it is a banner rather than an expandable card.
        /// </summary>
        private VisualElement BuildBrokenCard(SerializedProperty list, int index) =>
            new KUIBanner(KUITone.Error, "Missing action — the script was removed or renamed")
                .WithAction("Remove", () =>
                {
                    RemoveAt(list, index);
                    Rebuild();
                });

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

        private void BuildAddMenu(KUIMenu menu)
        {
            var any = false;

            foreach (var descriptor in BuildStepRegistry.GetDescriptors(m_Scope))
            {
                any = true;
                var captured = descriptor;
                menu.Item(captured.MenuPath, () => AddStep(captured));
            }

            if (!any)
                menu.Disabled("No actions available for this list");

            AppendOverrideGlobalMenu(menu);
        }

        /// <summary>
        /// Adds an "Override Global" submenu listing the matching global actions. Choosing one
        /// copies it into this list with a shared override key, so this copy replaces the global
        /// original for this environment or profile only.
        /// </summary>
        private void AppendOverrideGlobalMenu(KUIMenu menu)
        {
            if (m_Level == BuildStepScopeLevel.Global)
                return;

            var settings = BuildManagerSettings.InstanceOrNull;
            if (settings == null)
                return;

            var globals = GetGlobalList(settings).Where(step => step != null).ToList();
            if (globals.Count == 0)
                return;

            menu.Separator();

            foreach (var global in globals)
            {
                var captured = global;
                var path = "Override Global/" + Sanitize(captured.Title);

                if (ListContainsKeyOf(captured))
                    menu.Disabled(path);
                else
                    menu.Item(path, () => OverrideGlobal(settings, captured));
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

        private void BuildItemMenu(KUIMenu menu, int index, int count)
        {
            if (index > 0)
                menu.Item("Move Up", () => Move(index, index - 1));
            else
                menu.Disabled("Move Up");

            if (index < count - 1)
                menu.Item("Move Down", () => Move(index, index + 1));
            else
                menu.Disabled("Move Down");

            menu.Separator()
                .Item("Duplicate", () => Duplicate(index));

            if (m_Level != BuildStepScopeLevel.Global)
            {
                menu.Separator()
                    .Item("Make Global (move)", () => PromoteToGlobal(index, true))
                    .Item("Make Global (copy)", () => PromoteToGlobal(index, false));
            }

            menu.Separator()
                .Item("Remove", () =>
                {
                    var list = m_SerializedObject.FindProperty(m_PropertyPath);
                    RemoveAt(list, index);
                    Rebuild();
                });
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
