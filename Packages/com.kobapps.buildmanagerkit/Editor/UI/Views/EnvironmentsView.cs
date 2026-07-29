using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Master/detail editor for environments, plus the one-click switch that applies an
    /// environment to the Editor.
    /// </summary>
    internal sealed class EnvironmentsView : BuildManagerView
    {
        private static readonly HashSet<string> k_HiddenFields = new HashSet<string>
        {
            "m_Script",
            "m_OnActivateSteps",
            "m_PreBuildSteps",
            "m_PostBuildSteps"
        };

        private BuildEnvironment m_Selected;

        /// <inheritdoc />
        internal override string Title => "Environments";

        /// <inheritdoc />
        internal override string Badge => Settings.Environments.Count.ToString();

        /// <inheritdoc />
        internal override VisualElement Build()
        {
            m_Selected ??= Settings.ActiveEnvironment ?? Settings.GetSortedEnvironments().FirstOrDefault();

            var root = new VisualElement();
            root.AddToClassList("bmk-split");

            root.Add(BuildMasterList());
            root.Add(BuildDetail());

            return root;
        }

        private VisualElement BuildMasterList()
        {
            var master = new VisualElement();
            master.AddToClassList("bmk-master");

            var toolbar = new VisualElement();
            toolbar.AddToClassList("bmk-toolbar");
            toolbar.Add(new Button(() =>
            {
                m_Selected = BuildManagerBootstrap.CreateEnvironment("new", "New Environment", Color.gray);
                Window.RefreshCurrentView();
            }) { text = "+ New" });

            toolbar.Add(new Button(() =>
            {
                BuildManagerBootstrap.CreateDefaultEnvironments();
                Window.RefreshCurrentView();
            }) { text = "Starter Set" });

            master.Add(toolbar);

            if (Settings.Environments.Count == 0)
            {
                master.Add(BuildManagerUI.Muted("No environments yet."));
                return master;
            }

            master.Add(BuildEnvironmentList());
            master.Add(BuildManagerUI.Muted("Drag to reorder. The order here drives the toolbar "
                                            + "dropdown, the Scene view overlay and every menu."));

            return master;
        }

        /// <summary>
        /// The environment catalogue: the same styled rows as everywhere else in the window, with
        /// a grip that reorders them.
        ///
        /// The settings list order is what every switcher reads, so dragging here controls the
        /// order of the toolbar dropdown, the Scene view overlay, the window header menu, the CLI
        /// listing and the "switch to next environment" shortcut.
        /// </summary>
        private VisualElement BuildEnvironmentList()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;

            var list = new VisualElement();
            list.AddToClassList("bmk-list");
            list.AddToClassList("bmk-reorder-host");

            var environments = Settings.EnvironmentsMutable;

            for (var i = 0; i < environments.Count; i++)
                list.Add(CreateEnvironmentRow(list, environments[i]));

            scroll.Add(list);
            return scroll;
        }

        private VisualElement CreateEnvironmentRow(VisualElement container, BuildEnvironment environment)
        {
            var row = new VisualElement();
            row.AddToClassList("bmk-list-item");

            var handle = DragReorder.CreateHandle("Drag to reorder — this order drives every switcher");
            row.Add(handle);

            if (environment == null)
            {
                // A deleted asset still occupies a slot; show it so it can be found and removed
                // rather than silently shifting every index below it.
                var missing = new Label("(missing environment)");
                missing.AddToClassList("bmk-grow");
                missing.style.fontSize = 11;
                missing.style.opacity = 0.6f;

                row.tooltip = "This slot points at a deleted asset. Use Rescan to tidy up.";
                row.Add(missing);

                DragReorder.Attach(container, row, handle, OnReordered);
                return row;
            }

            if (environment == m_Selected)
                row.AddToClassList("bmk-list-item--selected");

            var dot = new VisualElement();
            dot.AddToClassList("bmk-pill__dot");
            dot.style.backgroundColor = environment.Color;

            var label = new Label(environment.DisplayName);
            label.AddToClassList("bmk-grow");
            label.style.fontSize = 11;
            label.style.overflow = Overflow.Hidden;

            row.tooltip = $"{environment.DisplayName} ({environment.Id})";
            row.Add(dot);
            row.Add(label);

            if (environment == Settings.ActiveEnvironment)
                row.Add(BuildManagerUI.Badge("ACTIVE", "success"));

            var captured = environment;
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                // The grip owns its own pointer events; clicking anywhere else selects.
                if (evt.target == handle)
                    return;

                m_Selected = captured;
                Window.RefreshCurrentView();
            });

            DragReorder.Attach(container, row, handle, OnReordered);
            return row;
        }

        /// <summary>Persists a drag, undoably, and refreshes the surfaces already on screen.</summary>
        private void OnReordered(int from, int to)
        {
            Undo.RegisterCompleteObjectUndo(Settings, "Reorder Environments");

            if (!Settings.MoveEnvironment(from, to))
                return;

            Window.RefreshHeader();
#if UNITY_6000_5_OR_NEWER
            EnvironmentToolbar.Refresh();
#endif
            Window.RefreshCurrentView();
        }

        private VisualElement BuildDetail()
        {
            var detail = new ScrollView();
            detail.AddToClassList("bmk-detail");

            if (m_Selected == null)
            {
                detail.Add(BuildManagerUI.EmptyState(
                    "Environments let one set of profiles produce dev, stage and prod builds — and let the Editor "
                    + "reproduce any of them while you work.",
                    "Create dev / stage / prod",
                    () =>
                    {
                        BuildManagerBootstrap.CreateDefaultEnvironments();
                        Window.RefreshCurrentView();
                    }));

                return detail;
            }

            var serializedObject = new SerializedObject(m_Selected);
            var isActive = m_Selected == Settings.ActiveEnvironment;

            var header = BuildManagerUI.Card(null, null, m_Selected.Color);
            var headerRow = new VisualElement();
            headerRow.AddToClassList("bmk-row");

            var title = new Label(m_Selected.DisplayName);
            title.AddToClassList("bmk-card__title");
            title.style.marginBottom = 0;

            var activate = BuildManagerUI.PrimaryButton(isActive ? "Active" : "Make Active", () =>
            {
                EnvironmentManager.Activate(m_Selected, true);
                Window.RefreshHeader();
                Window.RefreshCurrentView();
            });
            activate.SetEnabled(!isActive);

            var ping = new Button(() => EditorGUIUtility.PingObject(m_Selected)) { text = "Ping Asset" };

            headerRow.Add(title);
            headerRow.Add(BuildManagerUI.Spacer());
            headerRow.Add(ping);
            headerRow.Add(activate);
            header.Add(headerRow);

            var define = m_Selected.EnvironmentDefine;
            header.Add(BuildManagerUI.KeyValue("Generated define", string.IsNullOrEmpty(define) ? "disabled" : define));
            header.Add(BuildManagerUI.KeyValue("Runtime access",
                $"BuildInfo.Current.IsEnvironment(\"{m_Selected.Id}\")"));

            detail.Add(header);

            var configuration = BuildManagerUI.Card("Configuration");
            BuildManagerUI.DrawChildren(configuration, serializedObject.GetIterator(), serializedObject,
                k_HiddenFields);
            detail.Add(configuration);

            detail.Add(BuildPublishedAssetsCard());
            detail.Add(BuildGlobalActionsBanner());

            var activateCard = BuildManagerUI.Card();
            activateCard.Add(new StepListView(serializedObject, "m_OnActivateSteps",
                BuildStepScope.EnvironmentActivate,
                "On activate actions",
                "Run when this environment becomes the active Editor environment — swap config assets, "
                + "regenerate data, sync a backend URL.",
                BuildStepScopeLevel.Environment));
            detail.Add(activateCard);

            var preCard = BuildManagerUI.Card();
            preCard.Add(new StepListView(serializedObject, "m_PreBuildSteps", BuildStepScope.PreBuild,
                "Pre build actions",
                "Run for every profile built with this environment.",
                BuildStepScopeLevel.Environment));
            detail.Add(preCard);

            var postCard = BuildManagerUI.Card();
            postCard.Add(new StepListView(serializedObject, "m_PostBuildSteps", BuildStepScope.PostBuild,
                "Post build actions",
                "Run for every profile built with this environment.",
                BuildStepScopeLevel.Environment));
            detail.Add(postCard);

            detail.Bind(serializedObject);
            return detail;
        }

        /// <summary>
        /// The config assets this environment actually publishes: the project-wide defaults with
        /// its own entries layered on top. Shows which key comes from where, because "why is
        /// Get&lt;T&gt; returning the wrong asset" is otherwise a guessing game.
        /// </summary>
        private VisualElement BuildPublishedAssetsCard()
        {
            var resolved = EnvironmentAssetsWriter.Resolve(m_Selected, Settings);

            var card = BuildManagerUI.Card(
                "Published config assets",
                "Readable at runtime with EnvironmentAssets.Current.Get<T>(key). Only these assets are "
                + "referenced by the generated Resources asset, so the other environments' assets stay out "
                + "of the player.");

            if (resolved.Count == 0)
            {
                card.Add(BuildManagerUI.Muted(
                    "None yet. Add key/asset pairs under Config Assets above, or set project-wide defaults "
                    + "in the Settings tab."));

                return card;
            }

            foreach (var entry in resolved)
            {
                var ownEntry = m_Selected.GetConfigAsset(entry.key) != null;
                var typeName = entry.asset != null ? entry.asset.GetType().Name : "missing";

                var row = BuildManagerUI.KeyValue(
                    entry.key,
                    $"{entry.asset.name}  ({typeName})" + (ownEntry ? string.Empty : "  · inherited default"));

                row.tooltip = ownEntry
                    ? "Declared by this environment."
                    : "Inherited from the project-wide defaults. Add the same key here to override it.";

                card.Add(row);
            }

            var usage = new TextField
            {
                multiline = true,
                isReadOnly = true,
                value = $"var asset = EnvironmentAssets.Current.Get<YourType>(\"{resolved[0].key}\");"
            };
            usage.AddToClassList("bmk-code");
            usage.style.marginTop = 6;
            card.Add(usage);

            return card;
        }

        private VisualElement BuildGlobalActionsBanner() =>
            BuildManagerUI.GlobalActionsBanner(Settings, "environment", includeActivate: true);
    }
}
