using UnityEngine;
using UnityEngine.UI;

namespace DesktopMateOnMaimai.Core
{
    // A small pastel cushion. Geometry only: no imported texture or stencil state.
    internal sealed class CompanionSeatGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            var tint = new Color(0.57f, 0.89f, 0.85f, 0.9f);
            vh.AddVert(new Vector3(r.center.x, r.center.y), tint, Vector2.zero);
            const int segments = 32;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                vh.AddVert(new Vector3(r.center.x + Mathf.Cos(angle) * r.width * 0.5f,
                    r.center.y + Mathf.Sin(angle) * r.height * 0.5f), tint, Vector2.zero);
                if (i > 0) vh.AddTriangle(0, i, i + 1);
            }
        }
    }

}
