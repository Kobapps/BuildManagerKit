#if UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// A dropdown in Unity's main toolbar showing the active environment, with one-click
    /// switching.
    ///
    /// This is the fastest path in the whole package: the environment you are working against is
    /// visible from anywhere in the Editor, and changing it never costs a window. Right-click the
    /// element for the Build Manager window and the other shortcuts; drag it elsewhere on the
    /// toolbar with Unity's own customisation, or hide it entirely.
    ///
    /// Requires Unity 6.5, which introduced the main toolbar extension API. On older versions the
    /// Scene view overlay and the <c>Tools ▸ Build Manager Kit</c> menu cover the same ground.
    /// </summary>
    internal static class EnvironmentToolbar
    {
        /// <summary>
        /// Identifies the element both in the attribute and in <c>MainToolbar.Refresh</c>, so the
        /// two can never drift apart.
        /// </summary>
        internal const string ElementPath = "Build Manager Kit/Environment";

        private static readonly Dictionary<Color, Texture2D> k_Dots = new Dictionary<Color, Texture2D>();

        private static bool s_Subscribed;

        /// <summary>
        /// Factory Unity calls to build (and rebuild) the toolbar element. It reads the active
        /// environment every time, so a rebuild is all that is needed to show a new one.
        /// </summary>
        [MainToolbarElement(
            ElementPath,
            defaultDockPosition = MainToolbarDockPosition.Right,
            defaultDockIndex = 0,
            ussName = "bmk-toolbar-environment")]
        internal static MainToolbarElement CreateEnvironmentDropdown()
        {
            if (!s_Subscribed)
            {
                // The static event outlives an element rebuild inside the same domain, so guard
                // against stacking handlers when Unity recreates the toolbar.
                BuildManagerSettings.ActiveEnvironmentChanged += OnActiveEnvironmentChanged;
                s_Subscribed = true;
            }

            return new MainToolbarDropdown(BuildContent(), ShowMenu)
            {
                populateContextMenu = PopulateContextMenu
            };
        }

        private static void OnActiveEnvironmentChanged(BuildEnvironment environment) => Refresh();

        /// <summary>
        /// Rebuilds the toolbar element so it shows the environment that is active right now.
        ///
        /// Assigning <c>MainToolbarElement.content</c> is not enough: that property is a plain
        /// auto-property, so the new value is stored but nothing repaints and the label keeps
        /// showing the previous environment until some unrelated event rebuilds the toolbar —
        /// usually the script recompile a define change happens to trigger, which makes the switch
        /// look like it silently failed. <c>MainToolbar.Refresh</c> re-runs the factory above.
        /// </summary>
        internal static void Refresh()
        {
            try
            {
                MainToolbar.Refresh(ElementPath);
            }
            catch (Exception exception)
            {
                // Never let a cosmetic refresh break an environment switch.
                Debug.LogWarning($"[BuildManagerKit] Could not refresh the toolbar element: {exception.Message}");
            }
        }

        private static MainToolbarContent BuildContent()
        {
            // InstanceOrNull, never Instance: drawing the toolbar must not create assets in a
            // project that has not opted into Build Manager Kit yet.
            var settings = BuildManagerSettings.InstanceOrNull;
            var environment = settings != null ? settings.ActiveEnvironment : null;

            if (environment == null)
            {
                return new MainToolbarContent(
                    "Environment",
                    GetDot(new Color(0.45f, 0.45f, 0.45f)),
                    "No Build Manager Kit environment is active. Click to choose one.");
            }

            var tooltip = $"Active environment: {environment.DisplayName} ({environment.Id})";

            var defines = environment.GetAddedDefines().ToArray();
            if (defines.Length > 0)
                tooltip += "\nDefines: " + string.Join("  ", defines);

            if (environment.Variables.Count > 0)
                tooltip += "\nVariables: " + string.Join(", ", environment.Variables.Select(v => v.key));

            return new MainToolbarContent(environment.DisplayName, GetDot(environment.Color), tooltip);
        }

        private static void ShowMenu(Rect anchor)
        {
            var settings = BuildManagerSettings.InstanceOrNull;
            var menu = new GenericMenu();

            if (settings == null || settings.Environments.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No environments configured"));
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Create dev / stage / prod"), false, () =>
                {
                    BuildManagerBootstrap.CreateDefaultEnvironments();
                    Refresh();
                });
                menu.AddItem(new GUIContent("Open Build Manager"), false, () => BuildManagerWindow.Open());
                menu.DropDown(anchor);
                return;
            }

            var active = settings.ActiveEnvironment;

            foreach (var environment in settings.GetSortedEnvironments())
            {
                var captured = environment;
                var label = environment.RequireConfirmation
                    ? environment.DisplayName + " (protected)"
                    : environment.DisplayName;

                menu.AddItem(new GUIContent(label), environment == active, () =>
                {
                    EnvironmentManager.Activate(captured, true);
                    Refresh();
                });
            }

            menu.AddSeparator(string.Empty);

            if (active != null)
                menu.AddItem(new GUIContent("Clear Environment"), false, () =>
                {
                    EnvironmentManager.Activate(null, true);
                    Refresh();
                });

            menu.AddItem(new GUIContent("Manage Environments…"), false, () => BuildManagerWindow.Open("Environments"));
            menu.DropDown(anchor);
        }

        private static void PopulateContextMenu(DropdownMenu menu)
        {
            menu.AppendAction("Open Build Manager", _ => BuildManagerWindow.Open());
            menu.AppendAction("Manage Environments…", _ => BuildManagerWindow.Open("Environments"));
            menu.AppendSeparator();
            menu.AppendAction("Switch To Next Environment", _ =>
            {
                EnvironmentManager.ActivateNext();
                Refresh();
            });
        }

        /// <summary>
        /// A small filled circle tinted with the environment colour, cached per colour. Generated
        /// rather than shipped so an environment can use any colour the user picks.
        /// </summary>
        private static Texture2D GetDot(Color color)
        {
            if (k_Dots.TryGetValue(color, out var cached) && cached != null)
                return cached;

            const int size = 16;
            const float radius = size * 0.30f;
            var centre = (size - 1) * 0.5f;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                name = "BMK Environment Dot"
            };

            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));

                    // One pixel of falloff keeps the circle from looking jagged at 16px.
                    var alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color(color.r, color.g, color.b, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            k_Dots[color] = texture;
            return texture;
        }
    }
}
#endif
