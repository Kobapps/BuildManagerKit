using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Drag-to-reorder for hand-styled rows.
    ///
    /// Unity's <c>ListView</c> brings its own chrome and virtualisation, which fights variable
    /// height cards and looks nothing like the rest of this window. This keeps the rows exactly as
    /// authored — environment rows, action cards — and adds the grip, the lift and the index
    /// arithmetic on top.
    ///
    /// While dragging: the row leaves the layout and travels with the pointer, a greyed ghost of
    /// it occupies the slot it would land in, and the rows between glide out of the way.
    /// </summary>
    internal static class DragReorder
    {
        private const string k_PlaceholderName = "bmk-drop-placeholder";

        /// <summary>Used when a row's real height cannot be measured yet. Matches the row height in USS.</summary>
        private const float k_FallbackRowHeight = 22f;

        /// <summary>Live drag bookkeeping. Internal so tests can drive the lifecycle without a panel.</summary>
        internal sealed class DragState
        {
            internal bool Active;
            internal int FromIndex;
            internal int DropIndex;
            internal float PointerStartX;
            internal float PointerStartY;
            internal float RowStartTop;
            internal float RowStartLeft;
            internal float RowWidth;
            internal float RowHeight;

            /// <summary>Element the row is parented to while it travels.</summary>
            internal VisualElement Layer;

            /// <summary>Height the list had before the row was lifted out of it.</summary>
            internal float ContainerHeight;

            /// <summary>Rows still in the flow, in order, captured when the drag began.</summary>
            internal List<VisualElement> Siblings = new List<VisualElement>();

            /// <summary>The greyed slot showing where the row will land.</summary>
            internal VisualElement Placeholder;
        }

        /// <summary>Creates the grip that starts a drag. Add it as the first child of a row.</summary>
        internal static VisualElement CreateHandle(string tooltip = "Drag to reorder")
        {
            var handle = new Label("≡") { tooltip = tooltip };
            handle.AddToClassList("bmk-drag-handle");
            return handle;
        }

        /// <summary>
        /// Makes <paramref name="row"/> draggable within <paramref name="container"/>.
        /// </summary>
        /// <param name="container">
        /// Element whose direct children are the reorderable rows. It must establish a positioning
        /// context — add the <c>bmk-reorder-host</c> class.
        /// </param>
        /// <param name="row">The row being made draggable.</param>
        /// <param name="handle">Element that starts the drag, usually <see cref="CreateHandle"/>.</param>
        /// <param name="onReorder">
        /// Invoked with the original and destination indices once the pointer is released, and only
        /// when the position actually changed.
        /// </param>
        internal static void Attach(
            VisualElement container,
            VisualElement row,
            VisualElement handle,
            Action<int, int> onReorder)
        {
            var state = new DragState();

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                Begin(container, row, state, evt.position.x, evt.position.y);
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!state.Active)
                    return;

                Track(container, row, state, evt.position.x, evt.position.y);
                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!state.Active)
                    return;

                var from = state.FromIndex;
                var to = state.DropIndex;

                End(container, row, state);

                if (handle.HasPointerCapture(evt.pointerId))
                    handle.ReleasePointer(evt.pointerId);

                if (from >= 0 && to >= 0 && to != from)
                    onReorder?.Invoke(from, to);

                evt.StopPropagation();
            });

            // Losing capture — another element grabs it, the window loses focus — must not leave
            // the row floating out of flow forever.
            handle.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (state.Active)
                    End(container, row, state);
            });
        }

        internal static void Begin(VisualElement container, VisualElement row, DragState state, float pointerY) =>
            Begin(container, row, state, float.NaN, pointerY);

        internal static void Begin(VisualElement container, VisualElement row, DragState state,
            float pointerX, float pointerY)
        {
            var containerBounds = container.worldBound;
            var rowBounds = row.worldBound;

            state.Active = true;
            state.FromIndex = IndexOf(container, row);
            state.DropIndex = state.FromIndex;
            state.PointerStartX = float.IsNaN(pointerX) ? 0f : pointerX;
            state.PointerStartY = pointerY;
            // A row that has not been laid out reports NaN bounds. Left alone that NaN spreads
            // into the placeholder height and every sibling translation, and NaN-positioned
            // elements simply vanish.
            state.RowHeight = PositiveOr(rowBounds.height, PositiveOr(row.resolvedStyle.height, k_FallbackRowHeight));
            state.RowWidth = PositiveOr(rowBounds.width, PositiveOr(container.resolvedStyle.width, 0f));
            state.ContainerHeight = PositiveOr(container.layout.height,
                PositiveOr(container.resolvedStyle.height, 0f));
            state.Siblings = Rows(container, row);

            // The row is lifted onto a layer high enough that it can travel across the whole
            // window instead of being clipped to the list it came from.
            state.Layer = FindDragLayer(container) ?? container;

            var layerBounds = state.Layer.worldBound;
            state.RowStartLeft = Finite(rowBounds.x - layerBounds.x, 0f);
            state.RowStartTop = Finite(rowBounds.y - layerBounds.y, 0f);

            // Holding the list at the height it had stops it collapsing by one row the instant
            // the row leaves the flow, which otherwise makes the whole page jump on mouse-down.
            if (state.ContainerHeight > 0f)
                container.style.minHeight = state.ContainerHeight;

            row.AddToClassList("bmk-row--dragging");
            row.style.position = Position.Absolute;
            row.style.left = state.RowStartLeft;
            row.style.top = state.RowStartTop;
            row.style.height = state.RowHeight;

            if (state.RowWidth > 0f)
                row.style.width = state.RowWidth;

            if (state.Layer != container)
                state.Layer.Add(row);

            row.BringToFront();

            foreach (var sibling in state.Siblings)
                sibling.AddToClassList("bmk-row--gliding");

            state.Placeholder = CreatePlaceholder(state.RowHeight);
            container.Add(state.Placeholder);

            Reflow(state, state.DropIndex);
        }

        internal static void Track(VisualElement container, VisualElement row, DragState state, float pointerY) =>
            Track(container, row, state, float.NaN, pointerY);

        /// <summary>
        /// Moves the row to follow the pointer. It travels on both axes across the whole drag
        /// layer — bounded only by the window — while the drop index is still resolved against the
        /// list it came from.
        /// </summary>
        internal static void Track(VisualElement container, VisualElement row, DragState state,
            float pointerX, float pointerY)
        {
            var layer = state.Layer ?? container;

            if (!float.IsNaN(pointerX))
                row.style.left = ClampToLayer(
                    state.RowStartLeft + (pointerX - state.PointerStartX),
                    layer.layout.width,
                    state.RowWidth);

            row.style.top = ClampToLayer(
                state.RowStartTop + (pointerY - state.PointerStartY),
                layer.layout.height,
                state.RowHeight);

            state.DropIndex = ResolveDropIndex(container, state.Siblings, pointerY);
            Reflow(state, state.DropIndex);
        }

        /// <summary>
        /// Keeps the travelling row inside the drag layer. A layer that has not been laid out
        /// reports NaN, and NaN silently poisons Mathf.Clamp's comparisons — the row would be
        /// positioned at NaN and disappear — so that case falls back to a lower bound only.
        /// </summary>
        private static float ClampToLayer(float desired, float layerExtent, float rowExtent)
        {
            var limit = layerExtent - rowExtent;

            return !float.IsNaN(limit) && limit > 0f
                ? Mathf.Clamp(desired, 0f, limit)
                : Mathf.Max(0f, desired);
        }

        /// <summary>
        /// The element a dragged row is parented to while it travels: the nearest ancestor that
        /// carries a stylesheet, which is the window, inspector or overlay root this UI was built
        /// under. Going higher would reach the panel root, where the row would lose every style.
        /// </summary>
        private static VisualElement FindDragLayer(VisualElement container)
        {
            VisualElement layer = null;

            for (var candidate = container; candidate != null; candidate = candidate.parent)
            {
                if (candidate.styleSheets.count > 0)
                    layer = candidate;
            }

            return layer;
        }

        internal static void End(VisualElement container, VisualElement row, DragState state)
        {
            state.Active = false;

            row.RemoveFromClassList("bmk-row--dragging");
            row.style.position = StyleKeyword.Null;
            row.style.left = StyleKeyword.Null;
            row.style.right = StyleKeyword.Null;
            row.style.top = StyleKeyword.Null;
            row.style.width = StyleKeyword.Null;
            row.style.height = StyleKeyword.Null;

            container.style.minHeight = StyleKeyword.Null;
            state.Layer = null;

            foreach (var sibling in state.Siblings)
            {
                sibling.RemoveFromClassList("bmk-row--gliding");
                sibling.style.translate = StyleKeyword.Null;
            }

            state.Siblings.Clear();

            state.Placeholder?.RemoveFromHierarchy();
            state.Placeholder = null;

            ReturnToFlow(container, row, state.FromIndex);
        }

        /// <summary>
        /// Opens the gap at <paramref name="dropIndex"/>: rows at or past it slide down by the
        /// dragged row's height, and the greyed slot moves into the space they left.
        /// </summary>
        private static void Reflow(DragState state, int dropIndex)
        {
            for (var i = 0; i < state.Siblings.Count; i++)
            {
                // Translate rather than reordering: a transform animates, and it leaves `layout`
                // untouched so the hit testing below cannot chase its own tail.
                state.Siblings[i].style.translate = i >= dropIndex
                    ? new Translate(0f, state.RowHeight)
                    : new Translate(0f, 0f);
            }

            if (state.Placeholder == null)
                return;

            state.Placeholder.style.top = SlotTop(state.Siblings, dropIndex);
        }

        /// <summary>
        /// Untransformed top of the slot a drop at <paramref name="index"/> would occupy.
        /// </summary>
        private static float SlotTop(IReadOnlyList<VisualElement> siblings, int index)
        {
            if (siblings.Count == 0)
                return 0f;

            var top = index < siblings.Count
                ? siblings[index].layout.yMin
                : siblings[siblings.Count - 1].layout.yMax;

            return float.IsNaN(top) ? 0f : top;
        }

        /// <summary>
        /// The empty, dashed-outlined slot the row will drop into. Deliberately has no content:
        /// the row itself is visible under the pointer, so a second copy of its name here would
        /// just be noise.
        /// </summary>
        private static VisualElement CreatePlaceholder(float height)
        {
            var placeholder = new DashedBox { name = k_PlaceholderName };
            placeholder.AddToClassList("bmk-drop-placeholder");
            placeholder.style.height = height;
            return placeholder;
        }

        /// <summary>
        /// Puts the row back where it started in the child order. The caller rebuilds the list
        /// after a real move, but a drag that ends where it began must still leave the hierarchy
        /// exactly as it found it.
        /// </summary>
        private static void ReturnToFlow(VisualElement container, VisualElement row, int originalIndex)
        {
            // The row may be sitting on the drag layer rather than in the list, so this cannot
            // bail out when the parent differs — that is the normal case.
            row.RemoveFromHierarchy();

            var rows = Rows(container);
            var index = Mathf.Clamp(originalIndex, 0, rows.Count);

            if (index >= rows.Count)
                container.Add(row);
            else
                container.Insert(container.IndexOf(rows[index]), row);
        }

        /// <summary>
        /// Where the row would land: an index into the list <em>without</em> the dragged row,
        /// which is the destination index directly — no off-by-one adjustment needed.
        /// </summary>
        internal static int ResolveDropIndex(VisualElement container, VisualElement dragged, float pointerY) =>
            ResolveDropIndex(container, Rows(container, dragged), pointerY);

        private static int ResolveDropIndex(VisualElement container, IReadOnlyList<VisualElement> siblings,
            float pointerY)
        {
            // `layout` rather than `worldBound`, so the sliding rows do not move the very
            // midpoints being tested against — that feedback loop makes the target oscillate
            // between two slots as soon as the gap opens.
            var localY = pointerY - container.worldBound.y;
            return ResolveDropIndex(siblings.Select(sibling => sibling.layout).ToList(), localY);
        }

        /// <summary>
        /// The pure form: the first row whose vertical midpoint the pointer sits above, or the row
        /// count when it is past all of them.
        /// </summary>
        /// <param name="rowBounds">Bounds of the rows, excluding the one being dragged.</param>
        /// <param name="pointerY">Pointer position in the same space as the bounds.</param>
        internal static int ResolveDropIndex(IReadOnlyList<Rect> rowBounds, float pointerY)
        {
            for (var i = 0; i < rowBounds.Count; i++)
            {
                if (pointerY < rowBounds[i].y + rowBounds[i].height * 0.5f)
                    return i;
            }

            return rowBounds.Count;
        }

        private static float Finite(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        private static float PositiveOr(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? fallback : value;

        private static int IndexOf(VisualElement container, VisualElement row) => Rows(container).IndexOf(row);

        private static List<VisualElement> Rows(VisualElement container, VisualElement exclude = null) =>
            container.Children()
                .Where(child => child.name != k_PlaceholderName && child != exclude)
                .ToList();
    }
}
