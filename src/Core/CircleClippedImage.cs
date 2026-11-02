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
        private bool _clipCircle = true;
        private bool _protectInformation = true;
        private bool _unclipped;
        public void SetUnclipped(bool value)
        {
            if (_unclipped == value) return;
            _unclipped = value;
            SetVerticesDirty();
        }

        public void SetCircle(RectTransform slot, Vector2 centerFromBottomLeft, float radius, float reveal, bool clipCircle = true, bool protectInformation = true)
        {
            _slot = slot;
            _center = centerFromBottomLeft - slot.rect.size * 0.5f;
            _radius = radius;
            _reveal = Mathf.Clamp01(reveal);
            _clipCircle = clipCircle;
            _protectInformation = protectInformation;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            if (_unclipped) { base.OnPopulateMesh(vh); return; }
            vh.Clear();
            if (_slot == null || texture == null || _radius <= 0f || _reveal <= 0f) return;
            Matrix4x4 matrix = rectTransform.worldToLocalMatrix * _slot.localToWorldMatrix;
            int count = _clipCircle ? Segments : 4;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                float px = _clipCircle ? Mathf.Cos(angle) : (i == 0 || i == 3 ? -3f : 3f);
                float py = _clipCircle ? Mathf.Sin(angle) : (i < 2 ? -3f : 3f);
                Vector3 point = matrix.MultiplyPoint3x4(new Vector3(_center.x + px * _radius,
                    _center.y + py * _radius, 0f));
                _a[i] = new Vector2(point.x, point.y);
            }
            Rect rect = rectTransform.rect;
            // Reveal from the neck down after the head's hold, while keeping
            // texture coordinates tied to the original full body rectangle.
            Rect clipRect = Rect.MinMaxRect(rect.xMin, Mathf.Lerp(rect.yMax, rect.yMin, _reveal), rect.xMax, rect.yMax);
            // 后两平面位于玩家区域坐标，旋转人物时也不会把安全带一起转走。
            Matrix4x4 toSlot = _slot.worldToLocalMatrix * rectTransform.localToWorldMatrix;
            for (int plane = 0; plane < (_protectInformation ? 6 : 4) && count > 0; plane++)
            {
                int next = 0;
                Vector2 previous = _a[count - 1];
                float previousDistance = ClipDistance(previous, plane, clipRect, toSlot);
                for (int i = 0; i < count; i++)
                {
                    Vector2 current = _a[i];
                    float distance = ClipDistance(current, plane, clipRect, toSlot);
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

        private float ClipDistance(Vector2 p, int plane, Rect rect, Matrix4x4 toSlot)
        {
            if (plane < 4) return Distance(p, plane, rect);
            Vector3 slotPoint = toSlot.MultiplyPoint3x4(new Vector3(p.x, p.y, 0f));
            return plane == 4 ? slotPoint.x - _center.x - CircleSafeLayout.Left * _radius
                : slotPoint.y - _center.y - CircleSafeLayout.Bottom * _radius;
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
