using UnityEngine;
using UnityEngine.UI;

namespace DesktopMateOnMaimai.Core
{
    // Clip the character's body geometry on the CPU. No stencil writes: the game
    // uses the stencil buffer for its own circular monitor.
    internal sealed class CircleClippedImage : RawImage
    {
        private const int Segments = 128;
        private Vector2[] _a = new Vector2[Segments + 8], _b = new Vector2[Segments + 8];
        private RectTransform _slot;
        private Vector2 _center;
        private float _radius;
        private float _reveal = 1f;

        public void SetCircle(RectTransform slot, Vector2 centerFromBottomLeft, float radius, float reveal)
        {
            _slot = slot;
            _center = centerFromBottomLeft - slot.rect.size * 0.5f;
            _radius = radius;
            _reveal = Mathf.Clamp01(reveal);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_slot == null || texture == null || _radius <= 0f || _reveal <= 0f) return;
            Matrix4x4 matrix = rectTransform.worldToLocalMatrix * _slot.localToWorldMatrix;
            int count = Segments;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                Vector3 point = matrix.MultiplyPoint3x4(new Vector3(_center.x + Mathf.Cos(angle) * _radius,
                    _center.y + Mathf.Sin(angle) * _radius, 0f));
                _a[i] = new Vector2(point.x, point.y);
            }
            Rect rect = rectTransform.rect;
            // Reveal from the neck down after the head's hold, while keeping
            // texture coordinates tied to the original full body rectangle.
            Rect clipRect = Rect.MinMaxRect(rect.xMin, Mathf.Lerp(rect.yMax, rect.yMin, _reveal), rect.xMax, rect.yMax);
            for (int plane = 0; plane < 4 && count > 0; plane++)
            {
                int next = 0;
                Vector2 previous = _a[count - 1];
                float previousDistance = Distance(previous, plane, clipRect);
                for (int i = 0; i < count; i++)
                {
                    Vector2 current = _a[i];
                    float distance = Distance(current, plane, clipRect);
                    if ((distance >= 0f) != (previousDistance >= 0f))
                    {
                        float t = previousDistance / (previousDistance - distance);
                        _b[next++] = Vector2.Lerp(previous, current, t);
                    }
                    if (distance >= 0f) _b[next++] = current;
                    previous = current;
                    previousDistance = distance;
                }
                var swap = _a; _a = _b; _b = swap;
                count = next;
            }
            if (count < 3) return;
            for (int i = 0; i < count; i++)
            {
                Vector2 point = _a[i];
                var uv = new Vector2(uvRect.x + (point.x - rect.xMin) / rect.width * uvRect.width,
                    uvRect.y + (point.y - rect.yMin) / rect.height * uvRect.height);
                vh.AddVert(new Vector3(point.x, point.y, 0f), color, uv);
            }
            for (int i = 1; i < count - 1; i++) vh.AddTriangle(0, i, i + 1);
        }

        private static float Distance(Vector2 p, int plane, Rect rect)
        {
            switch (plane)
            {
                case 0: return p.x - rect.xMin;
                case 1: return rect.xMax - p.x;
                case 2: return p.y - rect.yMin;
                default: return rect.yMax - p.y;
            }
        }
    }
}
