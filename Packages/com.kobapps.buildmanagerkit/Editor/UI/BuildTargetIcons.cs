using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Maps a <see cref="BuildTarget"/> to Unity's own build settings icon, so a platform is
    /// recognisable at a glance instead of being just another line of text.
    /// </summary>
    internal static class BuildTargetIcons
    {
        /// <summary>
        /// The 16px platform icon. Textures come from
        /// the editor's own icon cache, so they must never be destroyed by callers.
        /// </summary>
        /// <param name="target">Platform to look up.</param>
        /// <param name="subtarget">Server subtargets get the dedicated server icon.</param>
        internal static Texture2D Get(BuildTarget target,
            StandaloneBuildSubtarget subtarget = StandaloneBuildSubtarget.Player)
        {
            if (subtarget == StandaloneBuildSubtarget.Server &&
                BuildPipeline.GetBuildTargetGroup(target) == BuildTargetGroup.Standalone)
                return Find("BuildSettings.DedicatedServer.Small");

            return Find(GetIconName(target));
        }

        /// <summary>The icon paired with a label, ready for a <see cref="GenericMenu"/> entry.</summary>
        internal static GUIContent GetContent(BuildTarget target, string label,
            StandaloneBuildSubtarget subtarget = StandaloneBuildSubtarget.Player)
        {
            var icon = Get(target, subtarget);
            return icon != null ? new GUIContent(label, icon) : new GUIContent(label);
        }

        private static string GetIconName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "BuildSettings.Windows.Small";
                case BuildTarget.StandaloneOSX:
                    return "BuildSettings.OSX.Small";
                case BuildTarget.StandaloneLinux64:
                    return "BuildSettings.Linux.Small";
                case BuildTarget.Android:
                    return "BuildSettings.Android.Small";
                case BuildTarget.iOS:
                    return "BuildSettings.iPhone.Small";
                case BuildTarget.tvOS:
                    return "BuildSettings.tvOS.Small";
                case BuildTarget.WebGL:
                    return "BuildSettings.WebGL.Small";
                case BuildTarget.WSAPlayer:
                    return "BuildSettings.Metro.Small";
                case BuildTarget.PS4:
                    return "BuildSettings.PS4.Small";
                case BuildTarget.PS5:
                    return "BuildSettings.PS5.Small";
                case BuildTarget.XboxOne:
                    return "BuildSettings.XboxOne.Small";
                case BuildTarget.Switch:
                    return "BuildSettings.Switch.Small";
                default:
                    return "BuildSettings.Standalone.Small";
            }
        }

        private static Texture2D Find(string name)
        {
            // FindTexture returns the editor's shared instance and null for unknown names, which
            // is exactly the fallback behaviour we want on platforms Unity has no icon for.
            var texture = EditorGUIUtility.FindTexture(name);
            return texture != null ? texture : EditorGUIUtility.FindTexture("BuildSettings.Standalone.Small");
        }
    }
}
