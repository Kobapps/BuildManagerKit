using System.Collections.Generic;
using System.Linq;
using EditorCoreKit.Editor;
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
            "m_Configs",
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

            var split = new KUISplitView(220f, false, "BuildManagerKit.Environments");
            split.First.Add(BuildMasterList());
            split.Second.Add(m_CommonSelected ? BuildCommonDetail() : BuildDetail());

            return split;
        }

        private VisualElement BuildMasterList()
        {
            var master = new VisualElement();
            master.style.flexGrow = 1;
            master.style.minHeight = 0;
            master.style.marginRight = 6;

            // The common configuration reads as one more thing you can select and edit, but it is not
            // an environment and must not look like one in the list — so it sits in its own section
            // above, outside the reorderable list.
            master.Add(KUIText.SectionTitle("Shared by every environment"));
            master.Add(CreateCommonRow());
            master.Add(KUILayout.Separator());
            master.Add(KUIText.SectionTitle("Environments"));

            var toolbar = new KUIToolbar();
            toolbar.Add(KUIButton.Secondary(KUIIcons.Plus + " New", () =>
            {
                m_Selected = BuildManagerBootstrap.CreateEnvironment("new", "New Environment", Color.gray);
                m_CommonSelected = false;
                Window.RefreshCurrentView();
            }));

            toolbar.Add(KUIButton.Secondary("Starter Set", () =>
            {
                BuildManagerBootstrap.CreateDefaultEnvironments();
                Window.RefreshCurrentView();
            }));

            master.Add(toolbar);

            if (Settings.Environments.Count == 0)
            {
                master.Add(KUIEmptyState.Line("No environments yet."));
                return master;
            }

            master.Add(BuildEnvironmentList());
            master.Add(KUIText.Muted("Drag to reorder. The order here drives the toolbar "
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
            var row = new KUIListRow("Common configuration", () =>
            {
                m_CommonSelected = true;
                Window.RefreshCurrentView();
            });

            row.AddToClassList("bmk-common-row");
            row.Selected = m_CommonSelected;
            row.WithDot(new Color(0.62f, 0.62f, 0.66f));

            row.tooltip = "Product and company name, bundle identifier, icon, shared runtime variables and "
                          + "versioning. Every environment starts from these.";

            if (!Settings.Common.IsConfigured)
            {
                var empty = new KUIBadge("EMPTY") { tooltip = "Nothing is shared yet." };
                row.WithAction(empty);
            }

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
            detail.AddToClassList(KUIClass.Detail);

            var serializedObject = new SerializedObject(Settings);
            var common = serializedObject.FindProperty("m_Common");

            var header = new KUICard("Common configuration", null, new Color(0.62f, 0.62f, 0.66f));
            header.Header.Add(KUIBadge.Tag("SHARED"));
            header.WithHeaderAction(KUIButton.Secondary("Ping Settings Asset",
                () => EditorGUIUtility.PingObject(Settings)));

            var summary = KUIText.Muted(string.Empty);
            header.Add(summary);
            detail.Add(header);

            var values = new KUICard(
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

            var variables = new KUICard(
                "Shared runtime variables",
                "Baked into BuildInfo for every environment and readable with "
                + "BuildInfo.Current.GetVariable(key). An environment declaring the same key overrides the "
                + "value.");

            var variablesField = new PropertyField(common.FindPropertyRelative("variables"), "Variables");
            variablesField.Bind(serializedObject);
            variables.Add(variablesField);
            detail.Add(variables);

            var versioning = new KUICard(
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

            var card = new KUICard(
                "Application icon",
                "Used by every environment that does not assign its own. Applied when an environment is "
                + "activated and when it is built, then restored with the rest of the player settings.");

            var preview = new KUIThumbnail(76f, "no icon");

            var fields = new VisualElement();
            fields.style.flexGrow = 1;
            fields.style.marginLeft = 10;
            fields.style.minWidth = 0;

            var iconField = new PropertyField(iconProperty, "Icon texture");
            iconField.Bind(serializedObject);
            fields.Add(iconField);

            var detailLabel = KUIText.Muted(string.Empty);
            fields.Add(detailLabel);

            var body = KUILayout.Row(preview, fields);
            body.AddToClassList(KUIClass.RowTop);
            body.style.marginTop = 8;
            card.Add(body);

            void Refresh()
            {
                serializedObject.Update();

                var texture = iconProperty.objectReferenceValue as Texture2D;
                preview.SetAsset(texture);

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
            list.AddToClassList(KUIClass.List);
            list.AddToClassList(KUIClass.ReorderHost);

            var environments = Settings.EnvironmentsMutable;

            for (var i = 0; i < environments.Count; i++)
                list.Add(CreateEnvironmentRow(list, environments[i]));

            scroll.Add(list);
            return scroll;
        }

        private VisualElement CreateEnvironmentRow(VisualElement container, BuildEnvironment environment)
        {
            if (environment == null)
            {
                // A deleted asset still occupies a slot; show it so it can be found and removed
                // rather than silently shifting every index below it.
                var missing = new KUIListRow("(missing environment)")
                    .WithDragHandle(container, OnReordered);

                missing.style.opacity = 0.6f;
                missing.tooltip = "This slot points at a deleted asset. Use Rescan to tidy up.";
                return missing;
            }

            var captured = environment;

            var row = new KUIListRow(environment.DisplayName, () => Select(captured))
                .WithDragHandle(container, OnReordered)
                .WithDot(environment.Color);

            row.Selected = environment == m_Selected;
            row.tooltip = $"{environment.DisplayName} ({environment.Id})";

            if (environment == Settings.ActiveEnvironment)
                row.WithBadge("ACTIVE", KUITone.Success);

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
            detail.AddToClassList(KUIClass.Detail);

            if (m_Selected == null)
            {
                detail.Add(new KUIEmptyState(
                    "No environments yet",
                    "Environments let one set of profiles produce dev, stage and prod builds — and let the "
                    + "Editor reproduce any of them while you work.",
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

            var header = new KUICard(m_Selected.DisplayName, null, m_Selected.Color);

            var activate = KUIButton.Primary(isActive ? "Active" : "Make Active", () =>
            {
                EnvironmentManager.Activate(m_Selected, true);
                Window.RefreshHeader();
                Window.RefreshCurrentView();
            });
            activate.SetEnabled(!isActive);

            header.WithHeaderAction(KUIButton.Secondary("Ping Asset",
                () => EditorGUIUtility.PingObject(m_Selected)));

            header.WithHeaderAction(KUIButton.Danger("Delete", () => DeleteEnvironment(m_Selected))
                .Tip("Delete this environment asset and remove it from the settings, the queues and the "
                     + "profiles that reference it."));

            header.WithHeaderAction(activate);

            var define = m_Selected.EnvironmentDefine;
            header.Add(KUIText.KeyValue("Generated define", string.IsNullOrEmpty(define) ? "disabled" : define));
            header.Add(KUIText.KeyValue("Runtime access",
                $"BuildInfo.Current.IsEnvironment(\"{m_Selected.Id}\")"));

            var inheritance = ConfigResolver.DescribeInheritance(Settings, m_Selected);
            if (!string.IsNullOrEmpty(inheritance))
                header.Add(KUIText.Muted(inheritance));

            detail.Add(header);

            var configuration = new KUICard("Configuration");
            KUIProperty.DrawChildren(configuration, serializedObject.GetIterator(), serializedObject,
                k_HiddenFields);
            detail.Add(configuration);

            detail.Add(BuildCommonConfigCard(serializedObject));
            detail.Add(BuildVersioningCard(serializedObject));
            detail.Add(BuildApplicationIconCard(serializedObject));
            detail.Add(BuildConfigsCard());
            detail.Add(BuildPublishedAssetsCard());
            detail.Add(BuildGlobalActionsBanner());

            var activateCard = new KUICard();
            activateCard.Add(new StepListView(serializedObject, "m_OnActivateSteps",
                BuildStepScope.EnvironmentActivate,
                "On activate actions",
                "Run when this environment becomes the active Editor environment — swap config assets, "
                + "regenerate data, sync a backend URL.",
                BuildStepScopeLevel.Environment));
            detail.Add(activateCard);

            var preCard = new KUICard();
            preCard.Add(new StepListView(serializedObject, "m_PreBuildSteps", BuildStepScope.PreBuild,
                "Pre build actions",
                "Run for every profile built with this environment.",
                BuildStepScopeLevel.Environment));
            detail.Add(preCard);

            var postCard = new KUICard();
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
            var card = new KUICard(
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

                var origin = KUIText.Muted(string.Empty);
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

            var card = new KUICard(
                "Versioning",
                "Switch on only when builds of this environment carry a different version from the common "
                + "configuration — staging shipping a release candidate, for instance. A profile that versions "
                + "itself still wins over this.");

            var toggle = new PropertyField(overrideProperty, "Version this environment differently");
            toggle.Bind(serializedObject);
            card.Add(toggle);

            var inherited = KUIText.Muted(string.Empty);
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

            var card = new KUICard(
                "Application icon",
                "A badged or tinted icon per environment stops a tester filing a bug against the wrong "
                + "build. Applied when this environment is activated and when it is built, then restored "
                + "with the rest of the player settings — so it never leaks into a production player.");

            var preview = new KUIThumbnail(76f, "no icon");

            var fields = new VisualElement();
            fields.style.flexGrow = 1;
            fields.style.marginLeft = 10;
            fields.style.minWidth = 0;

            var iconField = new PropertyField(iconProperty, "Icon texture");
            iconField.Bind(serializedObject);
            fields.Add(iconField);

            var detailLabel = KUIText.Muted(string.Empty);
            fields.Add(detailLabel);

            var body = KUILayout.Row(preview, fields);
            body.AddToClassList(KUIClass.RowTop);
            body.style.marginTop = 8;
            card.Add(body);

            void Refresh()
            {
                // The SerializedObject is shared with the field above, so re-read it rather than
                // trusting a cached value: the texture may have been assigned a frame ago.
                serializedObject.Update();

                var texture = iconProperty.objectReferenceValue as Texture2D;

                // The shared icon is what an empty field actually produces, so preview that rather
                // than an empty frame that would suggest no icon at all.
                preview.SetAsset(texture != null ? texture : commonIcon);

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

        /// <summary>
        /// The typed configs this environment publishes, each one editable in place.
        ///
        /// Inline rather than "select the asset and edit it in the Inspector": the reason to open
        /// this window is to compare flavours, and that falls apart the moment answering "what is
        /// different about staging" means clicking through to three other assets. Each card folds
        /// away, so a long list still reads as a list.
        ///
        /// The header of every card says how many environments publish the same asset, because
        /// editing a shared config changes all of them and that is not visible from the fields.
        /// </summary>
        private VisualElement BuildConfigsCard()
        {
            var card = new KUICard(
                "Configs",
                "Typed configuration assets this environment publishes. Runtime code reads them with "
                + "EnvironmentConfigs.Get<YourConfig>() — the type is the address, so there is no key to "
                + "keep in sync. List the same asset in several environments to share it.");

            card.WithHeaderAction(KUIButton.Secondary(KUIIcons.Plus + " Add", ShowAddConfigMenu)
                .Tip("Publish an existing config asset from this environment, or create a new one."));

            var configs = m_Selected.Configs;

            for (var i = 0; i < configs.Count; i++)
            {
                var config = configs[i];
                card.Add(config != null ? BuildConfigCard(config) : BuildMissingConfigRow(i));
            }

            if (configs.Count == 0)
            {
                card.Add(KUIEmptyState.Line(
                    "No configs yet. Add one to give this environment its own tuning values, endpoints or "
                    + "feature flags — readable at runtime without a magic string."));
            }

            var drop = new KUIDropZone(
                "Drop config assets here to publish them from this environment",
                assets =>
                {
                    var added = assets.OfType<EnvironmentConfig>()
                        .Count(config => EnvironmentConfigCatalog.Attach(m_Selected, config));

                    if (added > 0)
                        Window.RefreshCurrentView();
                },
                typeof(EnvironmentConfig));

            drop.style.marginTop = 8;
            card.Add(drop);

            if (EnvironmentConfigCatalog.ConfigTypes.Count == 0)
            {
                card.Add(KUIText.Muted(
                    "This project defines no config types yet. Derive a ScriptableObject from "
                    + "BuildManagerKit.EnvironmentConfig and it appears in the Add menu."));

                card.Add(KUIText.Code(
                    "public sealed class Endpoints : EnvironmentConfig { public string baseUrl; }"));
            }

            return card;
        }

        /// <summary>
        /// One config, folded away behind a header that says what it is and who else publishes it.
        /// The body is the asset's own inspector, so custom drawers and attributes keep working.
        /// </summary>
        private VisualElement BuildConfigCard(EnvironmentConfig config)
        {
            var sharedWith = EnvironmentConfigCatalog.UsedBy(config, Settings)
                .Where(environment => environment != m_Selected)
                .ToList();

            var card = new KUIExpandableCard(config.name, EnvironmentConfigCatalog.Describe(config))
                .WithTag(config.ConfigKey, KUITone.Accent,
                    config.HasExplicitKey
                        ? "The key this config is published under, set on the asset."
                        : "The key this config is published under, taken from its type name.");

            if (sharedWith.Count > 0)
            {
                var badge = new KUIBadge($"SHARED ×{sharedWith.Count + 1}")
                {
                    tooltip = "Also published by " + string.Join(", ", sharedWith.Select(e => e.DisplayName))
                              + ". Editing the fields below changes it for all of them."
                };

                card.WithHeaderAction(badge);
            }

            card.WithOverflowMenu(menu =>
            {
                menu.Item("Ping Asset", () => EditorGUIUtility.PingObject(config));
                menu.Item("Open in Inspector", () => Selection.activeObject = config);
                menu.Separator();

                foreach (var environment in Settings.GetSortedEnvironments())
                {
                    if (environment == null || environment == m_Selected)
                        continue;

                    var captured = environment;
                    var published = environment.Configs.Contains(config);

                    menu.Item($"Also publish from/{environment.DisplayName}", () =>
                    {
                        if (published)
                            EnvironmentConfigCatalog.Detach(captured, config);
                        else
                            EnvironmentConfigCatalog.Attach(captured, config);

                        Window.RefreshCurrentView();
                    }, on: published);
                }

                menu.Separator();

                menu.Item("Remove From This Environment", () =>
                {
                    if (EnvironmentConfigCatalog.Detach(m_Selected, config))
                        Window.RefreshCurrentView();
                });

                menu.Item("Delete Asset…", () => DeleteConfig(config, sharedWith));
            });

            // A real InspectorElement rather than a loop of PropertyFields: it brings the asset's own
            // custom editor and property drawers with it, and — the part that matters here — it owns
            // its binding. A plain field list would be re-bound to the *environment* when BuildDetail
            // binds the page, and every field would quietly stop working.
            var inspector = new InspectorElement(config);
            inspector.style.marginTop = 2;
            card.Add(inspector);

            // The script row is noise on a card whose header already says the type. Deferred because
            // the inspector fills itself in asynchronously.
            inspector.schedule.Execute(() =>
            {
                var script = inspector.Q<PropertyField>("PropertyField:m_Script");
                if (script != null)
                    script.style.display = DisplayStyle.None;
            });

            card.Add(KUIText.Code($"EnvironmentConfigs.Get<{config.GetType().Name}>()"));

            return card;
        }

        /// <summary>An empty slot in the list, shown so it can be cleared rather than puzzled over.</summary>
        private VisualElement BuildMissingConfigRow(int index)
        {
            var row = new KUIListRow("(empty config slot)");
            row.style.opacity = 0.6f;
            row.tooltip = "This slot is empty — probably a deleted asset. It publishes nothing.";

            row.WithAction(KUIButton.Secondary("Remove", () =>
            {
                Undo.RecordObject(m_Selected, "Remove Config");
                m_Selected.ConfigsMutable.RemoveAt(index);
                EditorUtility.SetDirty(m_Selected);
                AssetDatabase.SaveAssetIfDirty(m_Selected);
                Window.RefreshCurrentView();
            }));

            return row;
        }

        /// <summary>
        /// The Add menu: assets another environment already publishes first, because sharing one is
        /// almost always what is wanted and duplicating it by hand is the mistake this saves.
        /// </summary>
        private void ShowAddConfigMenu()
        {
            var menu = KUIMenu.New();

            var elsewhere = EnvironmentConfigCatalog.PublishedElsewhere(m_Selected, Settings);

            foreach (var config in elsewhere)
            {
                var captured = config;
                var owners = EnvironmentConfigCatalog.UsedBy(config, Settings);

                menu.Item($"Share from another environment/{config.name}  ({string.Join(", ", owners.Select(e => e.Id))})",
                    () =>
                    {
                        EnvironmentConfigCatalog.Attach(m_Selected, captured);
                        Window.RefreshCurrentView();
                    });
            }

            var unused = EnvironmentConfigCatalog.FindAll()
                .Where(config => !m_Selected.Configs.Contains(config) && !elsewhere.Contains(config))
                .ToList();

            foreach (var config in unused)
            {
                var captured = config;

                menu.Item($"Existing asset/{config.name}  ({config.GetType().Name})", () =>
                {
                    EnvironmentConfigCatalog.Attach(m_Selected, captured);
                    Window.RefreshCurrentView();
                });
            }

            if (elsewhere.Count > 0 || unused.Count > 0)
                menu.Separator();

            var types = EnvironmentConfigCatalog.ConfigTypes;

            if (types.Count == 0)
            {
                menu.Disabled("No config types defined — derive one from EnvironmentConfig");
            }
            else
            {
                foreach (var type in types)
                {
                    var captured = type;

                    menu.Item($"New/{type.Name}", () =>
                    {
                        EnvironmentConfigCatalog.Create(captured, m_Selected, m_Selected.Id);
                        Window.RefreshCurrentView();
                    });
                }
            }

            menu.ShowAtCursor();
        }

        /// <summary>
        /// Deletes a config asset, spelling out which other environments lose it. Deleting one that
        /// three environments publish is a much bigger change than the button suggests.
        /// </summary>
        private void DeleteConfig(EnvironmentConfig config, IReadOnlyList<BuildEnvironment> sharedWith)
        {
            var path = AssetDatabase.GetAssetPath(config);

            var message = $"Delete the config asset '{config.name}'?\n\n{path} is deleted and removed from "
                          + "this environment"
                          + (sharedWith.Count > 0
                              ? $" and from {string.Join(", ", sharedWith.Select(e => "'" + e.DisplayName + "'"))}, "
                                + "which publish it too."
                              : ".")
                          + "\n\nThis cannot be undone.";

            if (!EditorUtility.DisplayDialog("Delete config", message, "Delete", "Cancel"))
                return;

            foreach (var environment in EnvironmentConfigCatalog.UsedBy(config, Settings).ToList())
                EnvironmentConfigCatalog.Detach(environment, config);

            AssetDatabase.DeleteAsset(path);
            Window.RefreshCurrentView();
        }

        /// <summary>
        /// The config assets this environment actually publishes: the project-wide defaults with
        /// its own entries layered on top. Shows which key comes from where, because "why is
        /// Get&lt;T&gt; returning the wrong asset" is otherwise a guessing game.
        /// </summary>
        private VisualElement BuildPublishedAssetsCard()
        {
            var resolved = EnvironmentAssetsWriter.Resolve(m_Selected, Settings);

            var card = new KUICard(
                "Everything published to the player",
                "The whole set this environment bakes into Resources: the configs above, the project-wide "
                + "defaults, and any key/asset pairs. Only these assets are referenced, so the other "
                + "environments' assets stay out of the player.");

            if (resolved.Count == 0)
            {
                card.Add(KUIEmptyState.Line(
                    "Nothing yet. Add a config above, add key/asset pairs under Config Assets, or set "
                    + "project-wide defaults in the Settings tab."));

                return card;
            }

            foreach (var entry in resolved)
            {
                var isConfig = entry.asset is EnvironmentConfig;
                var ownEntry = isConfig || m_Selected.GetConfigAsset(entry.key) != null;
                var typeName = entry.asset != null ? entry.asset.GetType().Name : "missing";

                var origin = isConfig
                    ? "  · config"
                    : ownEntry
                        ? string.Empty
                        : "  · inherited default";

                var row = KUIText.KeyValue(entry.key, $"{entry.asset.name}  ({typeName}){origin}");

                row.tooltip = isConfig
                    ? $"A typed config. Read it with EnvironmentConfigs.Get<{typeName}>()."
                    : ownEntry
                        ? "Declared by this environment."
                        : "Inherited from the project-wide defaults. Add the same key here to override it.";

                card.Add(row);
            }

            var first = resolved[0];

            var usage = KUIText.Code(first.asset is EnvironmentConfig
                ? $"var config = EnvironmentConfigs.Get<{first.asset.GetType().Name}>();"
                : $"var asset = EnvironmentAssets.Current.Get<YourType>(\"{first.key}\");");

            usage.style.marginTop = 6;
            card.Add(usage);

            return card;
        }

        private VisualElement BuildGlobalActionsBanner() =>
            BuildManagerUI.GlobalActionsBanner(Settings, "environment", includeActivate: true);
    }
}
