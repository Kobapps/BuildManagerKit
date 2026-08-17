using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// The icons of one legacy <see cref="IconKind"/>, stored as asset GUIDs so the set survives
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

    /// <summary>
    /// The layers of a single platform icon slot — one entry for a plain icon, two for an Android
    /// adaptive icon (background then foreground), more for a layered tvOS icon.
    /// </summary>
    [Serializable]
    public sealed class PlatformIconLayerSnapshot
    {
        /// <summary>One GUID per layer, empty for a layer that had no texture.</summary>
        public string[] guids = Array.Empty<string>();
    }

    /// <summary>Every size of one platform icon kind, e.g. all of Android's adaptive icons.</summary>
    [Serializable]
    public sealed class PlatformIconKindSnapshot
    {
        /// <summary>
        /// Position of the kind in the target's supported kinds. Unity's
        /// <see cref="PlatformIconKind"/> is a reference type that cannot be serialised, and the
        /// order is fixed for a target within an Editor version, so the index is what identifies
        /// the kind again on restore.
        /// </summary>
        public int index;

        /// <summary>Kind name, kept for the log and to spot a mismatched restore.</summary>
        public string kind = string.Empty;

        /// <summary>One entry per icon slot, in the order the target reports them.</summary>
        public List<PlatformIconLayerSnapshot> icons = new List<PlatformIconLayerSnapshot>();
    }

    /// <summary>Every icon kind captured for a single target.</summary>
    [Serializable]
    public sealed class IconSnapshot
    {
        /// <summary>Captured legacy kinds, in the order they were read.</summary>
        public List<IconKindSnapshot> kinds = new List<IconKindSnapshot>();

        /// <summary>
        /// Captured platform kinds — Android's adaptive, round and legacy icons, iOS's application,
        /// spotlight and marketing icons — which is what the built player actually ships.
        /// </summary>
        public List<PlatformIconKindSnapshot> platformKinds = new List<PlatformIconKindSnapshot>();

        /// <summary>True when at least one kind was captured.</summary>
        public bool HasData =>
            (kinds != null && kinds.Count > 0) || (platformKinds != null && platformKinds.Count > 0);
    }

    /// <summary>
    /// Reads and writes the application icons of a build target.
    ///
    /// Environments use this to ship a different icon per flavour — a tinted or badged icon for
    /// dev and stage builds makes it obvious which build is on a device. Because the icons live in
    /// the project's player settings, every change is captured before a build and restored
    /// afterwards along with the rest of the settings.
    ///
    /// Android and iOS keep their icons in the *platform* icon set, which is where the built player
    /// reads them from: an Android launcher on API 26 and up draws the adaptive icon and ignores the
    /// legacy one, and the App Store rejects a build whose 1024 marketing icon is missing. Writing
    /// only the legacy <see cref="IconKind.Application"/> slots therefore changes what the Editor
    /// shows and not what the app ships, so every supported kind, size and layer is written here.
    ///
    /// The legacy slots are written as well as the platform ones, not instead of them. Unity fills an
    /// unassigned platform slot from the target's legacy icon on its way into the Xcode asset catalog
    /// or the Android resources, so a legacy list left on the project's own artwork puts that artwork
    /// back into every slot the environment's icon does not cover — the home screen shows the
    /// environment icon and the app switcher, Spotlight and Settings show the project's.
    /// </summary>
    public static class ApplicationIconService
    {
        // Notification and settings icons follow their own design rules on a platform that draws them
        // as a silhouette or a cropped glyph rather than as the app icon, so a launcher icon is never
        // written into them there. Matching on the kind name rather than an allow-list means a kind
        // added by a future Editor version is covered rather than silently skipped.
        private static readonly string[] k_UnmanagedKindNames = { "Notification", "Settings" };

        // Apple targets are the exception, and the reason this list exists. Every kind an iOS, tvOS or
        // visionOS target reports is a full-colour app icon in one asset catalog, differing only in
        // size: the 29pt "settings" and 20pt "notification" icons are the ones iOS draws on the card
        // of an app the user swipes up in the switcher, in a Spotlight result, in the Settings list and
        // above a notification. Skipping them leaves those places on the project's own icon while the
        // home screen shows the environment's, which is the mismatch the override exists to prevent.
        private static readonly string[] k_AppleTargetNames = { "iPhone", "tvOS", "VisionOS" };

        /// <summary>
        /// Fills every application icon slot of <paramref name="namedTarget"/> with
        /// <paramref name="icon"/> — the platform icon kinds where the target has them, and the legacy
        /// kinds either way, so nothing is left for Unity to fall back to.
        /// </summary>
        /// <param name="namedTarget">Target whose icons are replaced.</param>
        /// <param name="icon">Texture to use for every slot.</param>
        /// <param name="log">Optional log for progress and problems.</param>
        /// <returns>True when at least one slot was written.</returns>
        public static bool Apply(NamedBuildTarget namedTarget, Texture2D icon, IBuildLog log = null)
        {
            if (icon == null)
                return false;

            var largestSlot = 0;

            // Both paths run, and neither short-circuits the other: the platform set is what a mobile
            // player ships, and the legacy list is both what a desktop player ships and what Unity
            // falls back to for a platform slot the project left empty.
            var platform = ApplyPlatformIcons(namedTarget, icon, log, ref largestSlot);
            var legacy = ApplyLegacyIcons(namedTarget, icon, log, ref largestSlot);
            var applied = platform || legacy;

            if (!applied)
            {
                log?.Warning($"{namedTarget.TargetName} exposes no application icon slots; icon override skipped.");
                return false;
            }

            WarnWhenTooSmall(icon, largestSlot, namedTarget, log);
            return true;
        }

        /// <summary>Captures the current icons so they can be put back later.</summary>
        public static IconSnapshot Capture(NamedBuildTarget namedTarget)
        {
            var snapshot = new IconSnapshot();

            CapturePlatformIcons(namedTarget, snapshot);
            CaptureLegacyIcons(namedTarget, snapshot);

            return snapshot;
        }

        /// <summary>Writes a captured icon set back into the player settings.</summary>
        public static void Restore(NamedBuildTarget namedTarget, IconSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.HasData)
                return;

            RestorePlatformIcons(namedTarget, snapshot);
            RestoreLegacyIcons(namedTarget, snapshot);
        }

        // ---------------------------------------------------------------- platform icons

        private static bool ApplyPlatformIcons(
            NamedBuildTarget namedTarget,
            Texture2D icon,
            IBuildLog log,
            ref int largestSlot)
        {
            var kinds = SupportedPlatformKinds(namedTarget);
            if (kinds.Length == 0)
                return false;

            var applied = false;

            foreach (var kind in kinds)
            {
                if (!IsManaged(namedTarget, kind))
                    continue;

                PlatformIcon[] slots;

                try
                {
                    slots = PlayerSettings.GetPlatformIcons(namedTarget, kind);
                }
                catch (Exception exception)
                {
                    log?.Warning($"Could not read the {kind} icons of {namedTarget.TargetName}: {exception.Message}");
                    continue;
                }

                if (slots == null || slots.Length == 0)
                    continue;

                foreach (var slot in slots)
                {
                    Fill(slot, icon);
                    largestSlot = Math.Max(largestSlot, Math.Max(slot.width, slot.height));
                }

                try
                {
                    PlayerSettings.SetPlatformIcons(namedTarget, kind, slots);
                    applied = true;
                    log?.Info($"Application icon: '{icon.name}' applied to {slots.Length} {kind} slot(s).");
                }
                catch (Exception exception)
                {
                    log?.Warning($"Could not set the {kind} icons: {exception.Message}");
                }
            }

            return applied;
        }

        private static void CapturePlatformIcons(NamedBuildTarget namedTarget, IconSnapshot snapshot)
        {
            var kinds = SupportedPlatformKinds(namedTarget);

            for (var index = 0; index < kinds.Length; index++)
            {
                var kind = kinds[index];
                if (!IsManaged(namedTarget, kind))
                    continue;

                try
                {
                    var slots = PlayerSettings.GetPlatformIcons(namedTarget, kind);
                    if (slots == null || slots.Length == 0)
                        continue;

                    var entry = new PlatformIconKindSnapshot { index = index, kind = kind.ToString() };

                    foreach (var slot in slots)
                    {
                        var textures = slot.GetTextures() ?? Array.Empty<Texture2D>();
                        entry.icons.Add(new PlatformIconLayerSnapshot
                        {
                            guids = textures.Select(ToGuid).ToArray()
                        });
                    }

                    snapshot.platformKinds.Add(entry);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[BuildManagerKit] Could not capture the {kind} icons of {namedTarget.TargetName}: {exception.Message}");
                }
            }
        }

        private static void RestorePlatformIcons(NamedBuildTarget namedTarget, IconSnapshot snapshot)
        {
            if (snapshot.platformKinds == null || snapshot.platformKinds.Count == 0)
                return;

            var kinds = SupportedPlatformKinds(namedTarget);

            foreach (var entry in snapshot.platformKinds)
            {
                if (entry == null || entry.index < 0 || entry.index >= kinds.Length)
                {
                    Debug.LogWarning(
                        $"[BuildManagerKit] Could not restore the {entry?.kind} icons of {namedTarget.TargetName}: "
                        + "the target no longer reports that icon kind.");
                    continue;
                }

                var kind = kinds[entry.index];

                try
                {
                    var slots = PlayerSettings.GetPlatformIcons(namedTarget, kind);
                    if (slots == null)
                        continue;

                    for (var i = 0; i < slots.Length; i++)
                    {
                        // A slot the snapshot does not cover is cleared rather than left holding the
                        // icon that was just applied: an unwritten slot would leak a dev icon into
                        // the next build made from these settings.
                        var guids = i < entry.icons.Count && entry.icons[i] != null
                            ? entry.icons[i].guids ?? Array.Empty<string>()
                            : Array.Empty<string>();

                        for (var layer = 0; layer < slots[i].maxLayerCount; layer++)
                            slots[i].SetTexture(layer < guids.Length ? FromGuid(guids[layer]) : null, layer);
                    }

                    PlayerSettings.SetPlatformIcons(namedTarget, kind, slots);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[BuildManagerKit] Could not restore the {kind} icons: {exception.Message}");
                }
            }
        }

        /// <summary>
        /// The platform icon kinds of a target, or an empty array for a target that has none — a
        /// standalone or WebGL player — or whose platform module is not installed.
        /// </summary>
        private static PlatformIconKind[] SupportedPlatformKinds(NamedBuildTarget namedTarget)
        {
            try
            {
                return PlayerSettings.GetSupportedIconKinds(namedTarget) ?? Array.Empty<PlatformIconKind>();
            }
            catch (Exception)
            {
                return Array.Empty<PlatformIconKind>();
            }
        }

        /// <summary>
        /// Puts <paramref name="icon"/> in every layer the slot requires and clears the optional
        /// ones. Android's adaptive icon needs both its background and its foreground layer — a
        /// missing layer sends the launcher back to the legacy icon, which is exactly the mismatch
        /// this service exists to prevent.
        /// </summary>
        private static void Fill(PlatformIcon slot, Texture2D icon)
        {
            var required = Mathf.Clamp(slot.minLayerCount, 1, Math.Max(slot.maxLayerCount, 1));

            for (var layer = 0; layer < slot.maxLayerCount; layer++)
                slot.SetTexture(layer < required ? icon : null, layer);
        }

        /// <summary>
        /// True for a kind the override owns. Every kind of an Apple target is owned; elsewhere the
        /// notification and settings kinds are left to the project.
        /// </summary>
        private static bool IsManaged(NamedBuildTarget namedTarget, PlatformIconKind kind)
        {
            if (IsAppleTarget(namedTarget))
                return true;

            var name = kind != null ? kind.ToString() : string.Empty;

            return !k_UnmanagedKindNames.Any(
                unmanaged => name.IndexOf(unmanaged, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>True for the targets whose every icon kind is a size of the same app icon.</summary>
        internal static bool IsAppleTarget(NamedBuildTarget namedTarget) =>
            k_AppleTargetNames.Any(
                name => string.Equals(namedTarget.TargetName, name, StringComparison.OrdinalIgnoreCase));

        // ---------------------------------------------------------------- legacy icons

        private static bool ApplyLegacyIcons(
            NamedBuildTarget namedTarget,
            Texture2D icon,
            IBuildLog log,
            ref int largestSlot)
        {
            var applied = false;

            foreach (var kind in k_ManagedLegacyKinds)
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
                {
                    icons[i] = icon;
                    largestSlot = Math.Max(largestSlot, sizes[i]);
                }

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

            return applied;
        }

        private static void CaptureLegacyIcons(NamedBuildTarget namedTarget, IconSnapshot snapshot)
        {
            foreach (var kind in k_ManagedLegacyKinds)
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
        }

        private static void RestoreLegacyIcons(NamedBuildTarget namedTarget, IconSnapshot snapshot)
        {
            if (snapshot.kinds == null)
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

        /// <summary>
        /// The legacy icon kinds worth writing: the default list every player falls back to — including
        /// a mobile player, for a platform slot the project left empty — and the application list a
        /// desktop player builds from. There is no API that reports which of them a target supports —
        /// an unsupported one has no sizes, so the callers skip it.
        ///
        /// Notification and settings are deliberately absent, and <see cref="IconKind.Spotlight"/>
        /// and <see cref="IconKind.Store"/> belong to platforms that keep their icons in the
        /// platform set, where <see cref="ApplyPlatformIcons"/> has already covered them.
        /// </summary>
        private static readonly IconKind[] k_ManagedLegacyKinds = { IconKind.Any, IconKind.Application };

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Warns when the assigned texture is smaller than the largest slot it was written to.
        /// Unity upscales it silently, and a blurry marketing icon is found by a store reviewer
        /// rather than by the person who set the icon.
        /// </summary>
        private static void WarnWhenTooSmall(
            Texture2D icon,
            int largestSlot,
            NamedBuildTarget namedTarget,
            IBuildLog log)
        {
            if (log == null || largestSlot <= 0)
                return;

            var smallest = Math.Min(icon.width, icon.height);
            if (smallest >= largestSlot)
                return;

            log.Warning(
                $"Application icon '{icon.name}' is {icon.width}×{icon.height} but {namedTarget.TargetName} "
                + $"has slots up to {largestSlot}×{largestSlot}; Unity will upscale it.");
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
