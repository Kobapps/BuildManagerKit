using EditorCoreKit.Editor;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Scene view overlay that shows the active environment and switches it in one click.
    ///
    /// Iterating on environment-specific behaviour usually means switching, pressing Play, and
    /// switching back. Having that in the Scene view keeps the loop inside the scene instead of
    /// bouncing through a settings window.
    /// </summary>
    [Overlay(typeof(SceneView), k_Id, "Build Environment", defaultDisplay = false)]
    internal sealed class EnvironmentOverlay : Overlay
    {
        private const string k_Id = "buildmanagerkit-environment";

        private VisualElement m_Root;

        /// <inheritdoc />
        public override VisualElement CreatePanelContent()
        {
            m_Root = new VisualElement();
            m_Root.style.minWidth = 172;
            BuildManagerUI.ApplyStyles(m_Root);

            Rebuild();

            BuildManagerSettings.ActiveEnvironmentChanged += OnEnvironmentChanged;
            m_Root.RegisterCallback<DetachFromPanelEvent>(_ =>
                BuildManagerSettings.ActiveEnvironmentChanged -= OnEnvironmentChanged);

            return m_Root;
        }

        private void OnEnvironmentChanged(BuildEnvironment environment) => Rebuild();

        private void Rebuild()
        {
            if (m_Root == null)
                return;

            m_Root.Clear();

            var settings = BuildManagerSettings.InstanceOrNull;
            if (settings == null || settings.Environments.Count == 0)
            {
                m_Root.Add(EckText.Muted("No environments configured."));

                m_Root.Add(EckButton.Primary("Create dev / stage / prod", () =>
                {
                    BuildManagerBootstrap.CreateDefaultEnvironments();
                    Rebuild();
                }));

                return;
            }

            var active = settings.ActiveEnvironment;

            var list = new VisualElement();
            list.AddToClassList(EckClass.List);

            foreach (var environment in settings.GetSortedEnvironments())
            {
                var captured = environment;
                var isActive = environment == active;

                var row = new EckListRow(
                        environment.DisplayName,
                        isActive ? null : () => EnvironmentManager.Activate(captured, true))
                    .WithDot(environment.Color);

                row.Selected = isActive;
                list.Add(row);
            }

            m_Root.Add(list);

            var footer = EckButton.Secondary("Manage…", () => BuildManagerWindow.Open("Environments"));
            footer.style.marginTop = 4;
            m_Root.Add(footer);
        }
    }
}
