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
            "m_ProductName",
            "m_CompanyName",
            "m_ApplicationIdentifier",
            "m_ForceDevelopmentBuild",
            "m_ApplicationIcon",
            "m_OverrideVersioning",
            "m_Versioning",
            "m_OnActivateSteps",
            "m_PreBuildSteps",
            "m_PostBuildSteps"
        };

        /// <summary>
        /// The player settings that exist both in the common configuration and on every environment,
        /// with the property name each of them uses.
        ///
        /// Drawn as one card so "what is different about this environment" is a single list rather
        /// than fields scattered through the generic property loop — and so the common value can be
        /// shown as the placeholder of an empty field.
        /// </summary>
        private static readonly (string Property, string CommonProperty, string Label, string Tooltip)[]
            k_CommonFields =
            {
                ("m_ProductName", "productName", "Product name",
                    "PlayerSettings.productName, i.e. the name of the application."),
                ("m_CompanyName", "companyName", "Company name",
                    "PlayerSettings.companyName. Usually identical everywhere, so it belongs in the common "
                    + "configuration."),
                ("m_ApplicationIdentifier", "applicationIdentifier", "Bundle identifier",
                    "The bundle/package identifier of the target being built, e.g. com.studio.game.dev.")
            };

        private BuildEnvironment m_Selected;
        private bool m_CommonSelected;

        /// <inheritdoc />
        internal override string Title => "Environments";

        /// <inheritdoc />
        internal override string Badge => Settings.Environments.Count.ToString();

        /// <inheritdoc />
        internal override VisualElement Build()
        {
            if (!m_CommonSelected)
                m_Selected ??= Settings.ActiveEnvironment ?? Settings.GetSortedEnvironments().FirstOrDefault();

            var root = new VisualElement();
            root.AddToClassList("bmk-split");

            root.Add(BuildMasterList());
            root.Add(m_CommonSelected ? BuildCommonDetail() : BuildDetail());

            return root;
        }

        private VisualElement BuildMasterList()
        {
            var master = new VisualElement();
            master.AddToClassList("bmk-master");

            // The common configuration reads as one more thing you can select and edit, but it is not
            // an environment and must not look like one in the list — so it sits in its own section
            // above, outside the reorderable list.
            master.Add(BuildManagerUI.SectionTitle("Shared by every environment"));
            master.Add(CreateCommonRow());
            master.Add(BuildManagerUI.Separator());
            master.Add(BuildManagerUI.SectionTitle("Environments"));

            var toolbar = new VisualElement();
            toolbar.AddToClassList("bmk-toolbar");
            toolbar.Add(new Button(() =>
            {
                m_Selected = BuildManagerBootstrap.CreateEnvironment("new", "New Environment", Color.gray);
                m_CommonSelected = false;
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
        /// The row that selects the common configuration. Shaped like an environment row so it is
        /// obviously selectable, but with a shared-values badge and no drag grip — there is nothing to
        /// reorder it against.
        /// </summary>
        private VisualElement CreateCommonRow()
        {
            var row = new VisualElement();
            row.AddToClassList("bmk-list-item");
            row.AddToClassList("bmk-common-row");

            if (m_CommonSelected)
                row.AddToClassList("bmk-list-item--selected");

            var dot = new VisualElement();
            dot.AddToClassList("bmk-pill__dot");
            dot.style.backgroundColor = new Color(0.62f, 0.62f, 0.66f);

            var label = new Label("Common configuration");
            label.AddToClassList("bmk-grow");
            label.style.fontSize = 11;
            label.style.overflow = Overflow.Hidden;

            row.tooltip = "Product and company name, bundle identifier, icon, shared runtime variables and "
                          + "versioning. Every environment starts from these.";

            row.Add(dot);
            row.Add(label);

            if (!Settings.Common.IsConfigured)
            {
                var empty = BuildManagerUI.Badge("EMPTY", "neutral");
                empty.tooltip = "Nothing is shared yet.";
                row.Add(empty);
            }

            row.RegisterCallback<MouseDownEvent>(_ =>
            {
                m_CommonSelected = true;
                Window.RefreshCurrentView();
            });

            return row;
        }

        /// <summary>
        /// The detail page of the common configuration: the same shape as an environment's page, so
        /// the two read as siblings — a header saying what it is, then the player settings, the icon,
        /// the shared variables and versioning.
        /// </summary>
        private VisualElement BuildCommonDetail()
        {
            var detail = new ScrollView();
            detail.AddToClassList("bmk-detail");

            var serializedObject = new SerializedObject(Settings);
            var common = serializedObject.FindProperty("m_Common");

            var header = BuildManagerUI.Card(null, null, new Color(0.62f, 0.62f, 0.66f));
            var headerRow = new VisualElement();
            headerRow.AddToClassList("bmk-row");

            var title = new Label("Common configuration");
            title.AddToClassList("bmk-card__title");
            title.style.marginBottom = 0;

            headerRow.Add(title);
            headerRow.Add(BuildManagerUI.Badge("SHARED", "neutral"));
            headerRow.Add(BuildManagerUI.Spacer());
            headerRow.Add(new Button(() => EditorGUIUtility.PingObject(Settings)) { text = "Ping Settings Asset" });
            header.Add(headerRow);

            var summary = BuildManagerUI.Muted(string.Empty);
            header.Add(summary);
            detail.Add(header);

            var values = BuildManagerUI.Card(
                "Player settings",
                "Applied to every environment that does not fill the same field in itself. An empty field "
                + "means Build Manager Kit does not manage it, and the project's own player settings are "
                + "left as they are.");

            foreach (var field in k_CommonFields)
            {
                var text = new TextField(field.Label) { tooltip = field.Tooltip };
                text.BindProperty(common.FindPropertyRelative(field.CommonProperty));
                text.textEdition.placeholder = "not managed";
                values.Add(text);
            }

            var development = new PropertyField(common.FindPropertyRelative("forceDevelopmentBuild"),
                "Force development build");
            development.tooltip = "Inherit leaves the decision to each profile's own development build flag.";
            development.Bind(serializedObject);
            values.Add(development);

            detail.Add(values);

            detail.Add(BuildCommonIconCard(serializedObject, common));

            var variables = BuildManagerUI.Card(
                "Shared runtime variables",
                "Baked into BuildInfo for every environment and readable with "
                + "BuildInfo.Current.GetVariable(key). An environment declaring the same key overrides the "
                + "value.");

            var variablesField = new PropertyField(common.FindPropertyRelative("variables"), "Variables");
            variablesField.Bind(serializedObject);
            variables.Add(variablesField);
            detail.Add(variables);

            var versioning = BuildManagerUI.Card(
                "Versioning",
                "Where the version string and the build number come from, for every environment and every "
                + "profile that does not version itself.");

            var versioningField = new PropertyField(common.FindPropertyRelative("versioning"), string.Empty);
            versioningField.Bind(serializedObject);
            versioning.Add(versioningField);
            detail.Add(versioning);

            void Refresh()
            {
                serializedObject.Update();

                summary.text = Settings.Common.IsConfigured
                    ? "In effect: " + Settings.Common.Describe()
                    : "Nothing is shared yet. Fill a field in and every environment picks it up unless it "
                      + "fills in the same field — that is one edit for a company rename rather than one per "
                      + "flavour.";
            }

            detail.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            detail.schedule.Execute(Refresh);

            detail.Bind(serializedObject);
            return detail;
        }

        /// <summary>
        /// The shared application icon, with the same live preview as the per-environment card. No
        /// switch: an assigned texture is the switch.
        /// </summary>
        private VisualElement BuildCommonIconCard(SerializedObject serializedObject, SerializedProperty common)
        {
            var iconProperty = common.FindPropertyRelative("applicationIcon");

            var card = BuildManagerUI.Card(
                "Application icon",
                "Used by every environment that does not assign its own. Applied when an environment is "
                + "activated and when it is built, then restored with the rest of the player settings.");

            var body = new VisualElement();
            body.AddToClassList("bmk-icon-row");
            card.Add(body);

            var frame = new VisualElement();
            frame.AddToClassList("bmk-icon-frame");
            body.Add(frame);

            var preview = new Image { scaleMode = ScaleMode.ScaleToFit };
            preview.AddToClassList("bmk-icon-image");
            frame.Add(preview);

            var emptyLabel = new Label("no icon");
            emptyLabel.AddToClassList("bmk-icon-empty-label");
            frame.Add(emptyLabel);

            var fields = new VisualElement();
            fields.AddToClassList("bmk-icon-fields");
            body.Add(fields);

            var iconField = new PropertyField(iconProperty, "Icon texture");
            iconField.Bind(serializedObject);
            fields.Add(iconField);

            var detailLabel = BuildManagerUI.Muted(string.Empty);
            fields.Add(detailLabel);

            void Refresh()
            {
                serializedObject.Update();

                var texture = iconProperty.objectReferenceValue as Texture2D;

                preview.image = texture;
                preview.style.display = texture != null ? DisplayStyle.Flex : DisplayStyle.None;
                emptyLabel.style.display = texture != null ? DisplayStyle.None : DisplayStyle.Flex;
                frame.EnableInClassList("bmk-icon-frame--empty", texture == null);

                detailLabel.text = texture != null
                    ? $"{texture.width} × {texture.height} · {texture.format}"
                    : "Empty — every environment keeps the project icon unless it assigns one.";
            }

            card.TrackPropertyValue(iconProperty, _ => Refresh());
            card.schedule.Execute(Refresh);

            return card;
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

                Select(captured);
            });

            // Right-click straight on the row: the same actions as the detail header, where deleting
            // one of a long list is actually convenient.
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Select", _ => Select(captured));

                evt.menu.AppendAction("Make Active", _ =>
                {
                    EnvironmentManager.Activate(captured, true);
                    Window.RefreshHeader();
                    Window.RefreshCurrentView();
                }, captured == Settings.ActiveEnvironment
                    ? DropdownMenuAction.Status.Disabled
                    : DropdownMenuAction.Status.Normal);

                evt.menu.AppendAction("Ping Asset", _ => EditorGUIUtility.PingObject(captured));
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Delete…", _ => DeleteEnvironment(captured));
            }));

            DragReorder.Attach(container, row, handle, OnReordered);
            return row;
        }

        /// <summary>Selects an environment, leaving the common configuration item.</summary>
        private void Select(BuildEnvironment environment)
        {
            m_Selected = environment;
            m_CommonSelected = false;
            Window.RefreshCurrentView();
        }

        /// <summary>Persists a drag, undoably, and refreshes the surfaces already on screen.</summary>
        private void OnReordered(int from, int to)
        {
            Undo.RegisterCompleteObjectUndo(Settings, "Reorder Environments");

            if (!Settings.MoveEnvironment(from, to))
                return;

            Window.RefreshHeader();
#if UNITY_6000_4_OR_NEWER
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

            var delete = new Button(() => DeleteEnvironment(m_Selected))
            {
                text = "Delete",
                tooltip = "Delete this environment asset and remove it from the settings, the queues and the "
                          + "profiles that reference it."
            };
            delete.AddToClassList("bmk-button-danger");

            headerRow.Add(title);
            headerRow.Add(BuildManagerUI.Spacer());
            headerRow.Add(ping);
            headerRow.Add(delete);
            headerRow.Add(activate);
            header.Add(headerRow);

            var define = m_Selected.EnvironmentDefine;
            header.Add(BuildManagerUI.KeyValue("Generated define", string.IsNullOrEmpty(define) ? "disabled" : define));
            header.Add(BuildManagerUI.KeyValue("Runtime access",
                $"BuildInfo.Current.IsEnvironment(\"{m_Selected.Id}\")"));

            var inheritance = ConfigResolver.DescribeInheritance(Settings, m_Selected);
            if (!string.IsNullOrEmpty(inheritance))
                header.Add(BuildManagerUI.Muted(inheritance));

            detail.Add(header);

            var configuration = BuildManagerUI.Card("Configuration");
            BuildManagerUI.DrawChildren(configuration, serializedObject.GetIterator(), serializedObject,
                k_HiddenFields);
            detail.Add(configuration);

            detail.Add(BuildCommonConfigCard(serializedObject));
            detail.Add(BuildVersioningCard(serializedObject));
            detail.Add(BuildApplicationIconCard(serializedObject));
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
        /// Deletes an environment after confirming, spelling out what else changes: the references it
        /// is removed from, and whether the Editor has to move to another environment because this
        /// one is active.
        /// </summary>
        private void DeleteEnvironment(BuildEnvironment environment)
        {
            if (environment == null)
                return;

            var isActive = environment == Settings.ActiveEnvironment;
            var replacement = Settings.GetSortedEnvironments()
                .FirstOrDefault(candidate => candidate != environment);

            if (isActive && replacement == null)
            {
                EditorUtility.DisplayDialog(
                    "Delete environment",
                    $"'{environment.DisplayName}' is the only environment and is currently applied to the "
                    + "Editor.\n\nCreate another one first, so the Editor has somewhere to move to.",
                    "OK");

                return;
            }

            var references = Settings.Profiles.Count(profile => profile != null &&
                                                               (profile.DefaultEnvironment == environment ||
                                                                profile.AllowedEnvironments.Contains(environment)))
                             + Settings.Queues.Count(queue => queue != null &&
                                                              (queue.defaultEnvironment == environment ||
                                                               (queue.entries?.Any(entry =>
                                                                   entry?.environmentOverride == environment) ?? false)));

            var message = $"Delete the environment '{environment.DisplayName}'?\n\n"
                          + $"The asset {AssetDatabase.GetAssetPath(environment)} is deleted and the environment "
                          + "is removed from the settings"
                          + (references > 0 ? $" and from {references} profile/queue reference(s)." : ".")
                          + (isActive
                              ? $"\n\nIt is active, so the Editor switches to '{replacement.DisplayName}'."
                              : string.Empty)
                          + "\n\nThis cannot be undone.";

            if (!EditorUtility.DisplayDialog("Delete environment", message, "Delete", "Cancel"))
                return;

            var wasSelected = m_Selected == environment;

            if (!BuildManagerBootstrap.DeleteEnvironment(Settings, environment))
                return;

            // Only the deleted row hands the selection on; deleting some other row from its context
            // menu must leave the environment being edited where it was.
            if (wasSelected)
                m_Selected = Settings.GetSortedEnvironments().FirstOrDefault();

            Window.RefreshHeader();
#if UNITY_6000_4_OR_NEWER
            EnvironmentToolbar.Refresh();
#endif
            Window.RefreshCurrentView();
        }

        /// <summary>
        /// The player settings of one environment: a plain field per setting, showing the common value
        /// as its placeholder while it is empty.
        ///
        /// There is no override switch. Typing a value overrides the common one, clearing the field
        /// goes back to it, and the greyed placeholder means the field never looks unset when a value
        /// is in fact in effect.
        /// </summary>
        private VisualElement BuildCommonConfigCard(SerializedObject serializedObject)
        {
            var card = BuildManagerUI.Card(
                "Player settings",
                "Fill a field in only where this environment differs from the common configuration; clear it "
                + "to go back to the shared value, shown greyed. Applied when this environment is activated "
                + "and when it is built, then restored with the rest of the player settings.");

            foreach (var field in k_CommonFields)
            {
                var property = serializedObject.FindProperty(field.Property);
                var commonValue = CommonValue(Settings.Common, field.Property);

                var text = new TextField(field.Label) { tooltip = field.Tooltip };
                text.BindProperty(property);
                text.textEdition.placeholder = commonValue ?? "not set";
                card.Add(text);

                var origin = BuildManagerUI.Muted(string.Empty);
                origin.AddToClassList("bmk-inherited__label");
                card.Add(origin);

                void Refresh()
                {
                    serializedObject.Update();

                    var own = !string.IsNullOrWhiteSpace(property.stringValue);

                    origin.text = own
                        ? "Overrides the common configuration for this environment."
                        : commonValue != null
                            ? $"From the common configuration: '{commonValue}'"
                            : "Not set here or in the common configuration — the project's own player settings "
                              + "are used.";
                }

                card.TrackPropertyValue(property, _ => Refresh());
                card.schedule.Execute(Refresh);
            }

            var developmentBuild = new PropertyField(serializedObject.FindProperty("m_ForceDevelopmentBuild"),
                "Force development build");
            developmentBuild.tooltip = "Inherit falls back to the common configuration, then to the profile's own "
                                      + "development build flag.";
            developmentBuild.Bind(serializedObject);
            card.Add(developmentBuild);

            return card;
        }

        /// <summary>
        /// The value the common configuration contributes for one of the <see cref="k_CommonFields"/>
        /// settings, or null when it contributes nothing.
        /// </summary>
        private static string CommonValue(CommonBuildConfig common, string property)
        {
            if (common == null)
                return null;

            switch (property)
            {
                case "m_ProductName": return common.ProductNameOverride;
                case "m_CompanyName": return common.CompanyNameOverride;
                case "m_ApplicationIdentifier": return common.ApplicationIdentifierOverride;
                default: return null;
            }
        }

        /// <summary>
        /// Versioning for this environment: the common one unless this environment ships a different
        /// version, which is the usual reason a staging build reads <c>1.4.0-rc</c>.
        /// </summary>
        private VisualElement BuildVersioningCard(SerializedObject serializedObject)
        {
            var overrideProperty = serializedObject.FindProperty("m_OverrideVersioning");
            var versioningProperty = serializedObject.FindProperty("m_Versioning");

            var card = BuildManagerUI.Card(
                "Versioning",
                "Switch on only when builds of this environment carry a different version from the common "
                + "configuration — staging shipping a release candidate, for instance. A profile that versions "
                + "itself still wins over this.");

            var toggle = new PropertyField(overrideProperty, "Version this environment differently");
            toggle.Bind(serializedObject);
            card.Add(toggle);

            var inherited = BuildManagerUI.Muted(string.Empty);
            inherited.AddToClassList("bmk-inherited__label");
            card.Add(inherited);

            var own = new PropertyField(versioningProperty, string.Empty);
            own.Bind(serializedObject);
            card.Add(own);

            void Refresh()
            {
                serializedObject.Update();

                var overrides = overrideProperty.boolValue;
                own.style.display = overrides ? DisplayStyle.Flex : DisplayStyle.None;
                inherited.style.display = overrides ? DisplayStyle.None : DisplayStyle.Flex;

                if (overrides)
                    return;

                var resolved = ConfigResolver.ResolveVersioning(Settings, m_Selected, null);

                inherited.text = resolved.IsOwned
                    ? $"Inherited from {resolved.OwnerLabel}: {resolved.Config.Describe()}."
                    : "Nothing manages versioning in this project, so the version and build number are left as "
                      + "the project has them.";
            }

            card.TrackPropertyValue(overrideProperty, _ => Refresh());
            card.schedule.Execute(Refresh);

            return card;
        }

        /// <summary>
        /// The config assets this environment actually publishes: the project-wide defaults with
        /// its own entries layered on top. Shows which key comes from where, because "why is
        /// Get&lt;T&gt; returning the wrong asset" is otherwise a guessing game.
        /// </summary>
        /// <summary>
        /// The application icon of one environment, with a live preview.
        ///
        /// Drawn by hand rather than left to the generic property loop because an icon is the one
        /// setting you want to *see* rather than read a path for. An assigned texture overrides the
        /// shared icon; leaving it empty keeps whatever the common configuration provides.
        /// </summary>
        private VisualElement BuildApplicationIconCard(SerializedObject serializedObject)
        {
            var iconProperty = serializedObject.FindProperty("m_ApplicationIcon");
            var commonIcon = Settings.Common.ApplicationIconOverride;

            var card = BuildManagerUI.Card(
                "Application icon",
                "A badged or tinted icon per environment stops a tester filing a bug against the wrong "
                + "build. Applied when this environment is activated and when it is built, then restored "
                + "with the rest of the player settings — so it never leaks into a production player.");

            var body = new VisualElement();
            body.AddToClassList("bmk-icon-row");
            card.Add(body);

            var frame = new VisualElement();
            frame.AddToClassList("bmk-icon-frame");
            body.Add(frame);

            var preview = new Image { scaleMode = ScaleMode.ScaleToFit };
            preview.AddToClassList("bmk-icon-image");
            frame.Add(preview);

            var emptyLabel = new Label("no icon");
            emptyLabel.AddToClassList("bmk-icon-empty-label");
            frame.Add(emptyLabel);

            var fields = new VisualElement();
            fields.AddToClassList("bmk-icon-fields");
            body.Add(fields);

            var iconField = new PropertyField(iconProperty, "Icon texture");
            iconField.Bind(serializedObject);
            fields.Add(iconField);

            var detailLabel = BuildManagerUI.Muted(string.Empty);
            fields.Add(detailLabel);

            void Refresh()
            {
                // The SerializedObject is shared with the field above, so re-read it rather than
                // trusting a cached value: the texture may have been assigned a frame ago.
                serializedObject.Update();

                var texture = iconProperty.objectReferenceValue as Texture2D;

                // The shared icon is what an empty field actually produces, so preview that rather
                // than an empty frame that would suggest no icon at all.
                var shown = texture != null ? texture : commonIcon;

                preview.image = shown;
                preview.style.display = shown != null ? DisplayStyle.Flex : DisplayStyle.None;
                emptyLabel.style.display = shown != null ? DisplayStyle.None : DisplayStyle.Flex;
                frame.EnableInClassList("bmk-icon-frame--empty", shown == null);

                detailLabel.text = texture != null
                    ? $"{texture.width} × {texture.height} · {texture.format}"
                    : commonIcon != null
                        ? $"Empty — the common icon '{commonIcon.name}' is used."
                        : "Empty here and in the common configuration, so the project icon is kept.";
            }

            // Tracked so the card also follows undo, a CLI edit picked up by a reimport, and someone
            // editing the asset in the Inspector alongside this window.
            card.TrackPropertyValue(iconProperty, _ => Refresh());
            card.schedule.Execute(Refresh);

            return card;
        }

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
