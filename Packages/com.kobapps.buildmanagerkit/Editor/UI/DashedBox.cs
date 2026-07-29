using UnityEngine;
using UnityEngine.UIElements;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// An empty box outlined with a dashed border.
    ///
    /// USS has no <c>border-style</c>, so a dashed outline cannot be expressed in the stylesheet
    /// at all — it has to be stroked. The perimeter is walked once and emitted as a run of short
    /// segments, which keeps the dash length constant however the box is sized.
    /// </summary>
    internal sealed class DashedBox : VisualElement
    {
        private Color m_LineColor = new Color(0.62f, 0.62f, 0.62f, 0.85f);
        private float m_DashLength = 5f;
        private float m_GapLength = 4f;
        private float m_LineWidth = 1f;

        /// <summary>Creates the box and hooks up painting.</summary>
        internal DashedBox()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
        }

        /// <summary>Colour of the dashes.</summary>
        internal Color LineColor
        {
            get => m_LineColor;
            set
            {
                m_LineColor = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Length of each dash in pixels.</summary>
        internal float DashLength
        {
            get => m_DashLength;
            set
            {
                m_DashLength = Mathf.Max(1f, value);
                MarkDirtyRepaint();
            }
        }

        /// <summary>Gap between dashes in pixels.</summary>
        internal float GapLength
        {
            get => m_GapLength;
            set
            {
                m_GapLength = Mathf.Max(0f, value);
                MarkDirtyRepaint();
            }
        }

        /// <summary>Thickness of the dashes in pixels.</summary>
        internal float LineWidth
        {
            get => m_LineWidth;
            set
            {
                m_LineWidth = Mathf.Max(0.5f, value);
                MarkDirtyRepaint();
            }
        }

        private void Paint(MeshGenerationContext context)
        {
            var rect = contentRect;

            // Nothing sensible to draw before the first layout pass.
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height) || rect.width <= 1f || rect.height <= 1f)
                return;

            var painter = context.painter2D;
            painter.lineWidth = m_LineWidth;
            painter.strokeColor = m_LineColor;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();

            // Inset by half the line width so the stroke sits inside the box rather than
            // straddling its edge and being clipped.
            var inset = m_LineWidth * 0.5f;
            var left = rect.xMin + inset;
            var top = rect.yMin + inset;
            var right = rect.xMax - inset;
            var bottom = rect.yMax - inset;

            var topLeft = new Vector2(left, top);
            var topRight = new Vector2(right, top);
            var bottomRight = new Vector2(right, bottom);
            var bottomLeft = new Vector2(left, bottom);

            AddDashes(painter, topLeft, topRight);
            AddDashes(painter, topRight, bottomRight);
            AddDashes(painter, bottomRight, bottomLeft);
            AddDashes(painter, bottomLeft, topLeft);

            painter.Stroke();
        }

        private void AddDashes(Painter2D painter, Vector2 from, Vector2 to)
        {
            var length = Vector2.Distance(from, to);

            if (length <= 0f)
                return;

            var direction = (to - from) / length;
            var stride = m_DashLength + m_GapLength;

            for (var travelled = 0f; travelled < length; travelled += stride)
            {
                var end = Mathf.Min(travelled + m_DashLength, length);

                painter.MoveTo(from + direction * travelled);
                painter.LineTo(from + direction * end);
            }
        }
    }
}
