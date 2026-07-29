using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// The icons of one <see cref="IconKind"/>, stored as asset GUIDs so the set survives
    /// serialisation to disk and can be restored after a domain reload.
    /// </summary>
    [Serializable]
    public sealed class IconKindSnapshot
    {
        /// <summary>The <see cref="IconKind"/> this entry belongs to.</summary>
        public int kind;

        /// <summary>One GUID per icon slot, empty for slots that had no texture.</summary>
        public string[] guids = Array.Empty<string>();
    }

    /// <summary>Every icon kind captured for a single target.</summary>
    [Serializable]
    public sealed class IconSnapshot
    {
        /// <summary>Captured kinds, in the order they were read.</summary>
        public List<IconKindSnapshot> kinds = new List<IconKindSnapshot>();

        /// <summary>True when at least one kind was captured.</summary>
        public bool HasData => kinds != null && kinds.Count > 0;
    }

    /// <summary>
    /// Reads and writes the application icons of a build target.
    ///
    /// Environments use this to ship a different icon per flavour — a tinted or badged icon for
    /// dev and stage builds makes it obvious which build is on a device. Because the icons live in
    /// the project's player settings, every change is captured before a build and restored
    /// afterwards along with the rest of the settings.
    /// </summary>
    public static class ApplicationIconService
    {
        // Application is the icon users mean by "the app icon". Notification and Settings icons on
        // Android have their own design rules (white silhouettes), so they are never overwritten.
        private static readonly IconKind[] k_ManagedKinds = { IconKind.Application };

        /// <summary>
        /// Fills every application icon slot of <paramref name="namedTarget"/> with
        /// <paramref name="icon"/>.
        /// </summary>
        /// <param name="namedTarget">Target whose icons are replaced.</param>
        /// <param name="icon">Texture to use for every slot.</param>
        /// <param name="log">Optional log for progress and problems.</param>
        /// <returns>True when at least one slot was written.</returns>
        public static bool Apply(NamedBuildTarget namedTarget, Texture2D icon, IBuildLog log = null)
        {
            if (icon == null)
                return false;

            var applied = false;

            foreach (var kind in k_ManagedKinds)
            {
                int[] sizes;

                try
                {
                    sizes = PlayerSettings.GetIconSizes(namedTarget, kind);
                }
                catch (Exception exception)
                {
                    log?.Warning($"Could not read {kind} icon sizes for {namedTarget.TargetName}: {exception.Message}");
                    continue;
                }

                if (sizes == null || sizes.Length == 0)
                    continue;

                var icons = new Texture2D[sizes.Length];
                for (var i = 0; i < icons.Length; i++)
                    icons[i] = icon;

                try
                {
                    PlayerSettings.SetIcons(namedTarget, icons, kind);
                    applied = true;
                    log?.Info($"Application icon: '{icon.name}' applied to {sizes.Length} {kind} slot(s).");
                }
                catch (Exception exception)
                {
                    log?.Warning($"Could not set the {kind} icons: {exception.Message}");
                }
            }

            if (!applied)
                log?.Warning($"{namedTarget.TargetName} exposes no application icon slots; icon override skipped.");

            return applied;
        }

        /// <summary>Captures the current icons so they can be put back later.</summary>
        public static IconSnapshot Capture(NamedBuildTarget namedTarget)
        {
            var snapshot = new IconSnapshot();

            foreach (var kind in k_ManagedKinds)
            {
                try
                {
                    if (PlayerSettings.GetIconSizes(namedTarget, kind).Length == 0)
                        continue;

                    var icons = PlayerSettings.GetIcons(namedTarget, kind) ?? Array.Empty<Texture2D>();

                    snapshot.kinds.Add(new IconKindSnapshot
                    {
                        kind = (int)kind,
                        guids = icons.Select(ToGuid).ToArray()
                    });
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[BuildManagerKit] Could not capture {kind} icons for {namedTarget.TargetName}: {exception.Message}");
                }
            }

            return snapshot;
        }

        /// <summary>Writes a captured icon set back into the player settings.</summary>
        public static void Restore(NamedBuildTarget namedTarget, IconSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.HasData)
                return;

            foreach (var entry in snapshot.kinds)
            {
                try
                {
                    var kind = (IconKind)entry.kind;

                    // Unity expects exactly one entry per icon size. A project that had no icons
                    // reports an empty array from GetIcons, so pad with nulls to the real slot
                    // count — writing a short array would leave the previous textures in place.
                    var slots = PlayerSettings.GetIconSizes(namedTarget, kind).Length;
                    var icons = new Texture2D[slots];

                    for (var i = 0; i < slots && i < entry.guids.Length; i++)
                        icons[i] = FromGuid(entry.guids[i]);

                    PlayerSettings.SetIcons(namedTarget, icons, kind);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[BuildManagerKit] Could not restore {(IconKind)entry.kind} icons: {exception.Message}");
                }
            }
        }

        private static string ToGuid(Texture2D texture)
        {
            if (texture == null)
                return string.Empty;

            var path = AssetDatabase.GetAssetPath(texture);
            return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        }

        private static Texture2D FromGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
