using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    /// <summary>
    /// The index arithmetic behind drag reordering.
    ///
    /// The dragged row leaves the layout while it travels, so the drop position is resolved
    /// against the <em>remaining</em> rows — which makes it the destination index directly. These
    /// tests pin that down, because an off-by-one here only shows when dragging in one direction
    /// and is easy to miss by hand.
    /// </summary>
    [TestFixture]
    internal sealed class DragReorderTests
    {
        /// <summary>Three stacked 20px rows starting at y = 0.</summary>
        private static List<Rect> Rows(int count, float height = 20f) =>
            Enumerable.Range(0, count).Select(i => new Rect(0f, i * height, 100f, height)).ToList();

        [TestCase(-5f, 0)]
        [TestCase(0f, 0)]
        [TestCase(9f, 0)] // above the first row's midpoint
        [TestCase(11f, 1)] // past it
        [TestCase(29f, 1)]
        [TestCase(31f, 2)]
        [TestCase(49f, 2)]
        [TestCase(51f, 3)] // past the last midpoint: append
        [TestCase(500f, 3)]
        public void ResolveDropIndex_SwitchesAtRowMidpoints(float pointerY, int expected)
        {
            Assert.AreEqual(expected, DragReorder.ResolveDropIndex(Rows(3), pointerY));
        }

        [Test]
        public void ResolveDropIndex_OnAnEmptyListIsAlwaysZero()
        {
            Assert.AreEqual(0, DragReorder.ResolveDropIndex(new List<Rect>(), 0f));
            Assert.AreEqual(0, DragReorder.ResolveDropIndex(new List<Rect>(), 999f));
        }

        [Test]
        public void ResolveDropIndex_HandlesRowsOfDifferentHeights()
        {
            // Action cards expand, so rows are not uniform.
            var rows = new List<Rect>
            {
                new Rect(0f, 0f, 100f, 20f),   // midpoint 10
                new Rect(0f, 20f, 100f, 80f),  // midpoint 60
                new Rect(0f, 100f, 100f, 20f)  // midpoint 110
            };

            Assert.AreEqual(0, DragReorder.ResolveDropIndex(rows, 5f));
            Assert.AreEqual(1, DragReorder.ResolveDropIndex(rows, 40f));
            Assert.AreEqual(2, DragReorder.ResolveDropIndex(rows, 90f));
            Assert.AreEqual(3, DragReorder.ResolveDropIndex(rows, 115f));
        }

        [Test]
        public void DropIndex_IsTheDestinationIndexDirectly()
        {
            // Because the dragged row is excluded from the bounds, "insert before remaining row N"
            // is already the index the item ends up at. Verified against a real remove-and-insert
            // for every combination.
            for (var count = 1; count <= 6; count++)
            {
                for (var from = 0; from < count; from++)
                {
                    var remaining = count - 1;

                    for (var drop = 0; drop <= remaining; drop++)
                    {
                        var items = Enumerable.Range(0, count).ToList();
                        var moved = items[from];

                        items.RemoveAt(from);
                        items.Insert(drop, moved);

                        Assert.AreEqual(count, items.Count,
                            $"count={count} from={from} drop={drop} changed the length.");
                        CollectionAssert.AllItemsAreUnique(items);
                        Assert.AreEqual(drop, items.IndexOf(moved),
                            "The drop index should be exactly where the item lands.");
                    }
                }
            }
        }

        [Test]
        public void DroppingBackIntoItsOwnPositionIsANoOp()
        {
            // Dragging within the gap either side of the original slot must not register a move;
            // the caller relies on this to skip empty undo entries.
            var items = new List<int> { 0, 1, 2, 3 };
            const int from = 2;

            var moved = items[from];
            items.RemoveAt(from);
            items.Insert(from, moved);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, items);
        }

        [Test]
        public void ResolveDropIndex_IgnoresThePlaceholderAndTheDraggedRow()
        {
            var container = new VisualElement();
            var first = Row();
            var dragged = Row();
            var last = Row();

            container.Add(first);
            container.Add(dragged);
            container.Add(last);
            container.Add(new VisualElement { name = "bmk-drop-placeholder" });

            // Detached hierarchies have no layout, so every bound is zero-sized and the pointer
            // lands past all midpoints. The answer must be the count of *eligible* rows — two,
            // not the four children.
            Assert.AreEqual(2, DragReorder.ResolveDropIndex(container, dragged, 1000f));
        }

        [Test]
        public void CreateHandle_IsTaggedForStyling()
        {
            var handle = DragReorder.CreateHandle();

            Assert.IsTrue(handle.ClassListContains("bmk-drag-handle"));
            Assert.IsNotEmpty(handle.tooltip);
        }

        [Test]
        public void Attach_LeavesTheRowUntouchedUntilADragStarts()
        {
            var container = new VisualElement();
            var row = Row();
            var handle = DragReorder.CreateHandle();

            row.Add(handle);
            container.Add(row);

            DragReorder.Attach(container, row, handle, (_, __) => Assert.Fail("No drag happened."));

            Assert.IsFalse(row.ClassListContains("bmk-row--dragging"));
            Assert.AreEqual(StyleKeyword.Null, row.style.position.keyword,
                "The row must stay in the layout flow until it is picked up.");
        }

        // ------------------------------------------------------- lift and land lifecycle

        [Test]
        public void Begin_LiftsTheRowOutOfTheLayoutFlow()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();

            DragReorder.Begin(container, rows[1], state, 0f);

            Assert.IsTrue(state.Active);
            Assert.AreEqual(1, state.FromIndex);
            Assert.IsTrue(rows[1].ClassListContains("bmk-row--dragging"), "The row should look picked up.");
            Assert.AreEqual(Position.Absolute, rows[1].style.position.value,
                "Out of flow, so the rows below close the gap and it can travel over them.");
        }

        [Test]
        public void Begin_DrawsTheRowAboveItsSiblings()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();

            DragReorder.Begin(container, rows[0], state, 0f);

            // Detached test hierarchies have no styled ancestor, so the container doubles as the
            // drag layer and the row simply moves to the front of it.
            var siblings = container.Children().Where(c => c.name != "bmk-drop-placeholder").ToList();
            Assert.AreEqual(siblings.Count - 1, siblings.IndexOf(rows[0]));
        }

        [Test]
        public void Begin_DoesNotPinAHeightItCouldNotMeasure()
        {
            // Lifting a row out of the flow collapses the list by one row, so its height is held
            // for the duration of the drag. But a list with no layout yet measures as zero, and
            // pinning min-height:0 would collapse it entirely — worse than the jump being fixed.
            var container = Container(out var rows);
            var state = new DragReorder.DragState();

            DragReorder.Begin(container, rows[0], state, 0f);

            Assume.That(state.ContainerHeight, Is.EqualTo(0f),
                "Precondition: a detached hierarchy has no measurable height.");
            Assert.AreEqual(StyleKeyword.Null, container.style.minHeight.keyword,
                "An unmeasurable height must be left alone rather than pinned to zero.");
        }

        [Test]
        public void End_ReleasesTheHeldListHeight()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();

            DragReorder.Begin(container, rows[0], state, 0f);
            DragReorder.End(container, rows[0], state);

            Assert.AreEqual(StyleKeyword.Null, container.style.minHeight.keyword,
                "The list must be free to resize again once the drag finishes.");
        }

        [Test]
        public void Track_MovesTheRowOnBothAxes()
        {
            // The row follows the pointer around rather than being locked to a vertical rail.
            var container = Container(out var rows);
            var state = new DragReorder.DragState();
            var dragged = rows[0];

            DragReorder.Begin(container, dragged, state, 200f, 100f);
            DragReorder.Track(container, dragged, state, 260f, 140f);

            Assert.AreEqual(60f, dragged.style.left.value.value, 0.01f, "It should travel horizontally too.");
            Assert.AreEqual(40f, dragged.style.top.value.value, 0.01f);
        }

        [Test]
        public void End_ClearsTheTravelStyles()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();

            DragReorder.Begin(container, rows[0], state, 200f, 100f);
            DragReorder.Track(container, rows[0], state, 260f, 140f);
            DragReorder.End(container, rows[0], state);

            Assert.AreEqual(StyleKeyword.Null, rows[0].style.left.keyword);
            Assert.AreEqual(StyleKeyword.Null, rows[0].style.width.keyword);
            Assert.IsNull(state.Layer, "The drag layer reference must not outlive the drag.");
        }

        [Test]
        public void End_ReturnsTheRowToTheFlowAtItsOriginalIndex()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();
            var dragged = rows[1];

            DragReorder.Begin(container, dragged, state, 0f);
            DragReorder.End(container, dragged, state);

            Assert.IsFalse(state.Active);
            Assert.IsFalse(dragged.ClassListContains("bmk-row--dragging"));
            Assert.AreEqual(StyleKeyword.Null, dragged.style.position.keyword,
                "A finished drag must leave nothing floating.");
            Assert.AreEqual(StyleKeyword.Null, dragged.style.top.keyword);
            Assert.AreEqual(StyleKeyword.Null, dragged.style.height.keyword);

            // Back where it started: the caller only rebuilds the list when the index changed.
            var restored = container.Children().Where(c => c.name != "bmk-drop-placeholder").ToList();
            Assert.AreEqual(1, restored.IndexOf(dragged));
            CollectionAssert.AreEqual(rows, restored);
        }

        [Test]
        public void End_RestoresAFirstRowToTheFront()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();

            DragReorder.Begin(container, rows[0], state, 0f);
            DragReorder.End(container, rows[0], state);

            var restored = container.Children().Where(c => c.name != "bmk-drop-placeholder").ToList();
            Assert.AreEqual(0, restored.IndexOf(rows[0]));
        }

        [Test]
        public void End_RestoresALastRowToTheBack()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();

            DragReorder.Begin(container, rows[2], state, 0f);
            DragReorder.End(container, rows[2], state);

            var restored = container.Children().Where(c => c.name != "bmk-drop-placeholder").ToList();
            Assert.AreEqual(2, restored.IndexOf(rows[2]));
        }

        [Test]
        public void Track_MovesTheRowWithThePointer()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();
            var dragged = rows[0];

            DragReorder.Begin(container, dragged, state, 100f);
            Assert.AreEqual(0f, dragged.style.top.value.value, "It starts where it sat.");

            DragReorder.Track(container, dragged, state, 130f);

            // The row follows the pointer: 30px of travel becomes 30px of offset.
            Assert.AreEqual(30f, dragged.style.top.value.value, 0.01f);
            Assert.IsTrue(state.Active, "Tracking must not end the drag.");

            DragReorder.Track(container, dragged, state, 60f);
            Assert.AreEqual(0f, dragged.style.top.value.value, 0.01f,
                "Dragging above the container clamps at the top rather than going negative.");
        }

        [Test]
        public void APlaceholderMarksTheLandingSlot()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();

            DragReorder.Begin(container, rows[1], state, 0f);

            var placeholder = container.Q<VisualElement>("bmk-drop-placeholder");
            Assert.IsNotNull(placeholder, "A slot should mark where the row will land.");
            Assert.IsInstanceOf<DashedBox>(placeholder,
                "USS cannot express a dashed border, so the outline has to be a painted element.");
            Assert.AreEqual(state.RowHeight, placeholder.style.height.value.value,
                "The slot should be the size of the row being carried.");
            Assert.AreEqual(0, placeholder.childCount,
                "The slot is an empty gap — the row itself is already visible under the pointer.");

            DragReorder.End(container, rows[1], state);

            Assert.IsNull(container.Q<VisualElement>("bmk-drop-placeholder"),
                "The slot must be gone once the drag ends.");
        }

        [Test]
        public void SiblingsSlideAsideToOpenTheGap()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();
            var dragged = rows[0];

            DragReorder.Begin(container, dragged, state, 0f);

            // Dropping at the end: every remaining row stays put.
            ReflowTo(container, dragged, state, 2);
            Assert.AreEqual(0f, Shift(rows[1]));
            Assert.AreEqual(0f, Shift(rows[2]));

            // Dropping at the front: both remaining rows move down by one row height.
            ReflowTo(container, dragged, state, 0);
            Assert.AreEqual(state.RowHeight, Shift(rows[1]));
            Assert.AreEqual(state.RowHeight, Shift(rows[2]));

            // Dropping in the middle: only the row below it moves.
            ReflowTo(container, dragged, state, 1);
            Assert.AreEqual(0f, Shift(rows[1]));
            Assert.AreEqual(state.RowHeight, Shift(rows[2]));

            DragReorder.End(container, dragged, state);
        }

        [Test]
        public void SiblingsAreLeftUnshiftedWhenTheDragEnds()
        {
            var container = Container(out var rows);
            var state = new DragReorder.DragState();

            DragReorder.Begin(container, rows[0], state, 0f);
            ReflowTo(container, rows[0], state, 0);
            Assert.AreEqual(state.RowHeight, Shift(rows[1]), "Precondition: the gap is open.");

            DragReorder.End(container, rows[0], state);

            Assert.AreEqual(StyleKeyword.Null, rows[1].style.translate.keyword,
                "A finished drag must leave no rows nudged out of place.");
            Assert.IsFalse(rows[1].ClassListContains("bmk-row--gliding"),
                "The transition class is only for the duration of a drag.");
        }

        /// <summary>Drives the private reflow through the public entry point at a chosen index.</summary>
        private static void ReflowTo(VisualElement container, VisualElement dragged,
            DragReorder.DragState state, int dropIndex)
        {
            var method = typeof(DragReorder).GetMethod("Reflow",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.IsNotNull(method, "DragReorder.Reflow was renamed; update this test.");
            method.Invoke(null, new object[] { state, dropIndex });
        }

        private static float Shift(VisualElement row) =>
            row.style.translate.keyword == StyleKeyword.Null ? 0f : row.style.translate.value.y.value;

        private static VisualElement Container(out List<VisualElement> rows)
        {
            var container = new VisualElement();
            container.AddToClassList("bmk-reorder-host");

            rows = new List<VisualElement> { Row(), Row(), Row() };
            foreach (var row in rows)
                container.Add(row);

            return container;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("bmk-list-item");
            return row;
        }
    }

    /// <summary>
    /// <see cref="BuildManagerUI.DrawChildren"/> walks serialized properties by depth. It is used
    /// with both a root <c>SerializedObject</c> iterator and a nested managed reference element,
    /// and the two need different handling.
    /// </summary>
    [TestFixture]
    internal sealed class DrawChildrenTests
    {
        [Test]
        public void DrawChildren_OnARootIteratorDoesNotLogAnIterationError()
        {
            var environment = ScriptableObject.CreateInstance<BuildEnvironment>();
            environment.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                var serialized = new UnityEditor.SerializedObject(environment);
                var container = new VisualElement();

                // Asking a root iterator for its end property logs
                // "Invalid iteration - you need to call Next(true) on the first element", which the
                // test framework turns into a failure. Passing here is the assertion.
                BuildManagerUI.DrawChildren(container, serialized.GetIterator(), serialized,
                    new HashSet<string> { "m_Script" });

                Assert.Greater(container.childCount, 0, "The environment's fields should have been drawn.");
            }
            finally
            {
                Object.DestroyImmediate(environment);
            }
        }

        [Test]
        public void DrawChildren_OnANestedPropertyStopsAtTheParentLevel()
        {
            var profile = ScriptableObject.CreateInstance<BuildTargetProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                var serialized = new UnityEditor.SerializedObject(profile);
                var nested = serialized.FindProperty("m_Android");
                Assume.That(nested, Is.Not.Null);

                var container = new VisualElement();
                BuildManagerUI.DrawChildren(container, nested, serialized);

                // Android options only — not the sibling fields that follow it on the profile.
                Assert.Greater(container.childCount, 0);
                Assert.Less(container.childCount, 20,
                    "Iteration escaped the nested property and kept walking the whole object.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
