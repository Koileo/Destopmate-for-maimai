using System;
using DesktopMateOnMaimai.Config;
using UnityEngine;
using UnityEngine.UI;

namespace DesktopMateOnMaimai.Core
{
    /// <summary>
    /// 覆盖在游戏画面上的一层 uGUI 画布。
    /// 关键约束：不挂 GraphicRaycaster、CanvasGroup.blocksRaycasts = false，
    /// 保证完全不参与触摸判定（舞萌的输入来自触摸板，不能被打断）。
    /// </summary>
    internal sealed class OverlayCanvas
    {
        public GameObject Go;
        public Canvas Canvas;
        public CanvasGroup Group;
        public RectTransform SlotRect;     // 单个玩家区域的容器
        public RectTransform CharacterRect;
        private CircleClippedImage _bodySurface;
        private float _aspect = 0.7f;
        private float _motionTime;
        private ModConfig _cfg;
        private bool _upperSeat;
        private Vector2 _seatAnchor = new Vector2(0.5f, 0.52f);
        private RectTransform _seatRect;

        public bool HasUpperScreen
        {
            get
            {
                if (SlotRect == null) return false;
                float width = SlotRect.rect.width, height = SlotRect.rect.height;
                return height > width && height - Mathf.Min(width, height * 1080f / 1920f) > width * 0.4f;
            }
        }

        public void SetUpperSeat(bool seated)
        {
            _upperSeat = seated;
            ApplyLayout(_cfg);
        }

        public void SetSeatAnchor(Vector2 anchor)
        {
            _seatAnchor = new Vector2(Mathf.Clamp01(anchor.x), Mathf.Clamp01(anchor.y));
            if (_upperSeat) SetEntrance(_motionTime, _cfg);
        }

        public static OverlayCanvas Create(ModConfig cfg)
        {
            var oc = new OverlayCanvas();

            oc.Go = new GameObject("DesktopMate_Overlay");
            UnityEngine.Object.DontDestroyOnLoad(oc.Go);

            oc.Canvas = oc.Go.AddComponent<Canvas>();
            oc.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            oc.Canvas.sortingOrder = cfg.SortingOrder;
            // 刻意不添加 GraphicRaycaster：本层不接受任何输入

            var scaler = oc.Go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(cfg.ReferenceWidth, cfg.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            oc.Group = oc.Go.AddComponent<CanvasGroup>();
            oc.Group.alpha = 0f;
            oc.Group.interactable = false;
            oc.Group.blocksRaycasts = false;

            var slotGo = new GameObject("Slot");
            slotGo.transform.SetParent(oc.Go.transform, false);
            oc.SlotRect = slotGo.AddComponent<RectTransform>();

            var charGo = new GameObject("Character");
            charGo.transform.SetParent(slotGo.transform, false);
            oc.CharacterRect = charGo.AddComponent<RectTransform>();

            var seatGo = new GameObject("LittleSeat", typeof(RectTransform));
            seatGo.transform.SetParent(slotGo.transform, false);
            seatGo.transform.SetAsFirstSibling();
            oc._seatRect = seatGo.GetComponent<RectTransform>();
            seatGo.AddComponent<CompanionSeatGraphic>().raycastTarget = false;
            seatGo.SetActive(false);

            oc.ApplyLayout(cfg);
            return oc;
        }

        /// <summary>舞萌双屏机台（1P/2P 各 1920x1080）在单帧缓冲里可能是 3840x1080。</summary>
        public static bool IsDualWide(int screenWidth, int screenHeight)
        {
            if (screenHeight <= 0) return false;
            return (float)screenWidth / screenHeight > 2.4f;
        }

        public void ApplyLayout(ModConfig cfg)
        {
            _cfg = cfg;
            int sw = Screen.width, sh = Screen.height;
            bool dual = IsDualWide(sw, sh);

            float slotMin = 0f, slotMax = 1f;
            if (dual)
            {
                switch ((cfg.PlayerSlot ?? "Auto").ToUpperInvariant())
                {
                    case "2P": slotMin = 0.5f; slotMax = 1f; break;
                    case "1P": slotMin = 0f; slotMax = 0.5f; break;
                    case "BOTH":
                    default: slotMin = 0f; slotMax = 0.5f; break;
                }
            }

            SlotRect.anchorMin = new Vector2(slotMin, 0f);
            SlotRect.anchorMax = new Vector2(slotMax, 1f);
            SlotRect.offsetMin = Vector2.zero;
            SlotRect.offsetMax = Vector2.zero;
            SlotRect.pivot = new Vector2(0.5f, 0.5f);

            if (cfg.CircleEntrance || _upperSeat)
            {
                CharacterRect.anchorMin = Vector2.zero;
                CharacterRect.anchorMax = Vector2.zero;
                CharacterRect.pivot = new Vector2(0.5f, 0f);
                CharacterRect.localScale = Vector3.one;
                SetEntrance(_motionTime, cfg);
                return;
            }

            Vector2 anchor, pivot;
            switch ((cfg.Anchor ?? "BottomRight").ToUpperInvariant())
            {
                case "BOTTOMLEFT": anchor = new Vector2(0f, 0f); pivot = new Vector2(0f, 0f); break;
                case "BOTTOMCENTER": anchor = new Vector2(0.5f, 0f); pivot = new Vector2(0.5f, 0f); break;
                case "CENTER": anchor = new Vector2(0.5f, 0.5f); pivot = new Vector2(0.5f, 0.5f); break;
                case "BOTTOMRIGHT":
                default: anchor = new Vector2(1f, 0f); pivot = new Vector2(1f, 0f); break;
            }

            CharacterRect.anchorMin = anchor;
            CharacterRect.anchorMax = anchor;
            CharacterRect.pivot = pivot;
            CharacterRect.anchoredPosition = new Vector2(cfg.OffsetX, cfg.OffsetY);
            float aspect = string.Equals(cfg.Renderer, "3D", StringComparison.OrdinalIgnoreCase)
                ? Mathf.Max(0.1f, (float)cfg.RenderTextureWidth / Mathf.Max(1, cfg.RenderTextureHeight))
                : 1f;
            CharacterRect.sizeDelta = new Vector2(cfg.CharacterHeightPixels * aspect, cfg.CharacterHeightPixels);
            CharacterRect.localScale = Vector3.one * Mathf.Max(0.05f, cfg.Scale);
        }

        public void SetAspect(float aspect)
        {
            _aspect = Mathf.Max(0.1f, aspect);
            if (_cfg != null) ApplyLayout(_cfg);
        }

        public RawImage CreateSurface(string name)
        {
            // One surface and one UV mapping for the entire character. A head
            // must never appear independently of the body beneath it.
            var body = Surface(name, CharacterRect, 0f, 1f, true);
            _bodySurface = (CircleClippedImage)body;
            if (!_cfg.CircleEntrance) _bodySurface.SetUnclipped(true);
            SetEntrance(_motionTime, _cfg);
            return body;
        }

        private static RawImage Surface(string name, Transform parent, float bottom, float top, bool clipped = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RawImage raw = clipped ? go.AddComponent<CircleClippedImage>() : go.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.uvRect = new Rect(0f, bottom, 1f, top - bottom);
            var rect = raw.rectTransform;
            rect.anchorMin = new Vector2(0f, bottom);
            rect.anchorMax = new Vector2(1f, top);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return raw;
        }

        public void SetEntrance(float time, ModConfig cfg)
        {
            _motionTime = time;
            if (SlotRect == null || CharacterRect == null) return;
            // Work in the canvas slot, so portrait output and a changed window size
            // do not inherit the original 1920x1080 corner assumptions.
            float width = SlotRect.rect.width, height = SlotRect.rect.height;
            if (width <= 1f || height <= 1f) return;
            float shorter = Mathf.Min(width, height);
            // The game's portrait monitor layout is 1080x1920, letterboxed when
            // the actual desktop height is shorter (this machine: 1080x1600).
            float gameWidth = height > width ? Mathf.Min(width, height * 1080f / 1920f) : shorter;
            if (_seatRect != null) _seatRect.gameObject.SetActive(_upperSeat && HasUpperScreen);
            if (_upperSeat && HasUpperScreen)
            {
                float upperHeight = height - gameWidth;
                float seatH = Mathf.Min(gameWidth * cfg.UpperSeatHeight,
                    upperHeight * 0.9f, width * 0.52f / _aspect);
                Vector2 point = new Vector2(width * cfg.UpperSeatX, gameWidth + upperHeight * cfg.UpperSeatY);
                point.x = Mathf.Clamp(point.x, seatH * _aspect * _seatAnchor.x + width * 0.45f,
                    width * 0.97f - seatH * _aspect * (1f - _seatAnchor.x));
                point.y = Mathf.Clamp(point.y, gameWidth + seatH * _seatAnchor.y + upperHeight * 0.04f,
                    height - seatH * (1f - _seatAnchor.y) - upperHeight * 0.04f);
                CharacterRect.pivot = _seatAnchor;
                CharacterRect.sizeDelta = new Vector2(seatH * _aspect, seatH);
                CharacterRect.anchoredPosition = point;
                CharacterRect.localRotation = Quaternion.identity;
                _bodySurface?.SetUnclipped(true);
                if (_seatRect != null)
                {
                    _seatRect.anchorMin = _seatRect.anchorMax = Vector2.zero;
                    _seatRect.pivot = new Vector2(0.5f, 0.5f);
                    _seatRect.anchoredPosition = point - new Vector2(0f, seatH * 0.04f);
                    _seatRect.sizeDelta = new Vector2(seatH * 0.42f, seatH * 0.08f);
                }
                return;
            }
            if (!cfg.CircleEntrance) { _bodySurface?.SetUnclipped(true); return; }
            CharacterRect.pivot = new Vector2(0.5f, 0f);
            _bodySurface?.SetUnclipped(false);
            var center = new Vector2(width * cfg.CircleCenterX,
                cfg.CircleCenterY < 0f ? gameWidth * 0.5f : height * Mathf.Clamp01(cfg.CircleCenterY));
            float radius = gameWidth * cfg.CircleRadius;
            float h = Mathf.Min(cfg.CharacterHeightPixels * Mathf.Max(0.05f, cfg.Scale),
                radius * cfg.CircleHeightRadius);
            EntrancePose pose;
            Vector2 head;
            if (cfg.AnimatedEntrance && !cfg.ReducedMotion)
            {
                h = radius * CircleSafeLayout.FitHeight(h / radius, _aspect);
                pose = EntranceMotion.Sample(time, cfg);
                CirclePathPose path = CircleEntrancePath.Sample(pose, cfg, h / radius, _aspect);
                head = center + new Vector2(path.HeadX * radius, path.HeadY * radius);
                head.y -= pose.Dip;
            }
            else
            {
                // Render textures include transparent margins. Fitting the entire
                // quad into a narrow lane makes the visible character too small.
                // Keep the requested height and let the circular surface clip the rim.
                pose = new EntrancePose { Reveal = 2f, BodyReveal = 1f };
                head = center + new Vector2(cfg.CircleResidentX * radius,
                    cfg.CircleResidentY * radius + h * (1f - cfg.HeadFraction * 0.5f));
            }
            CharacterRect.sizeDelta = new Vector2(h * _aspect, h);

            Quaternion rotation = Quaternion.Euler(0f, 0f, pose.Tilt);
            Vector3 headFromFoot = rotation * new Vector3(0f, h * (1f - cfg.HeadFraction * 0.5f), 0f);
            CharacterRect.anchoredPosition = head - new Vector2(headFromFoot.x, headFromFoot.y);
            CharacterRect.localRotation = rotation;
            if (_bodySurface != null)
                _bodySurface.SetCircle(SlotRect, center, radius, 1f, true, cfg.AnimatedEntrance && !cfg.ReducedMotion);
        }

        public void SetAlpha(float a)
        {
            if (Group != null) Group.alpha = Mathf.Clamp01(a);
        }

        public void SetVisible(bool v)
        {
            if (Go != null && Go.activeSelf != v) Go.SetActive(v);
        }

        public void Dispose()
        {
            if (Go != null) UnityEngine.Object.Destroy(Go);
            Go = null;
        }
    }
}
