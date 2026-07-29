using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Tests
{
    /// <summary>
    /// The dashed outline used by the drop slot. USS has no <c>border-style</c>, so this is
    /// stroked by hand and the geometry has to hold up on its own.
    /// </summary>
    [TestFixture]
    internal sealed class DashedBoxTests
    {
        [Test]
        public void Defaults_AreSaneAndVisible()
        {
            var box = new DashedBox();

            Assert.Greater(box.DashLength, 0f);
            Assert.GreaterOrEqual(box.GapLength, 0f);
            Assert.Greater(box.LineWidth, 0f);
            Assert.Greater(box.LineColor.a, 0f, "An invisible outline would defeat the point.");
        }

        [Test]
        public void ItIgnoresPointerInput()
        {
            // It sits over the list during a drag; it must never intercept the pointer.
            Assert.AreEqual(PickingMode.Ignore, new DashedBox().pickingMode);
        }

        [Test]
        public void DashMetricsAreClampedToDrawableValues()
        {
            // A zero or negative dash would emit degenerate segments; a zero line width would
            // stroke nothing at all.
            var box = new DashedBox { DashLength = -5f, GapLength = -5f, LineWidth = 0f };

            Assert.GreaterOrEqual(box.DashLength, 1f);
            Assert.GreaterOrEqual(box.GapLength, 0f);
            Assert.GreaterOrEqual(box.LineWidth, 0.5f);
        }

        [Test]
        public void PaintingAnUnsizedBoxDoesNotThrow()
        {
            // Painting can be requested before the first layout pass, when the rect is NaN.
            var box = new DashedBox();
            Assert.DoesNotThrow(() => box.MarkDirtyRepaint());
        }
    }
}
