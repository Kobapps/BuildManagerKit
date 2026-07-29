using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
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
                m_Root.Add(BuildManagerUI.Muted("No environments configured."));

                m_Root.Add(new Button(() =>
                {
                    BuildManagerBootstrap.CreateDefaultEnvironments();
                    Rebuild();
                }) { text = "Create dev / stage / prod" });

                return;
            }

            var active = settings.ActiveEnvironment;

            foreach (var environment in settings.GetSortedEnvironments())
            {
                var captured = environment;
                var isActive = environment == active;

                var row = new VisualElement();
                row.AddToClassList("bmk-list-item");
                row.style.height = 22;

                var dot = new VisualElement();
                dot.AddToClassList("bmk-pill__dot");
                dot.style.backgroundColor = environment.Color;

                var label = new Label(environment.DisplayName);
                label.AddToClassList("bmk-grow");
                label.style.fontSize = 11;
                if (isActive)
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;

                row.Add(dot);
                row.Add(label);

                if (isActive)
                    row.AddToClassList("bmk-list-item--selected");
                else
                    row.RegisterCallback<MouseDownEvent>(_ => EnvironmentManager.Activate(captured, true));

                m_Root.Add(row);
            }

            var footer = new Button(() => BuildManagerWindow.Open("Environments")) { text = "Manage…" };
            footer.style.marginTop = 4;
            m_Root.Add(footer);
        }
    }
}
