using System;
using DesktopMateOnMaimai.Config;
using MelonLoader;
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
        public RawImage HeadSurface;
        private RectTransform _circle, _bodyProxy;
        private CircleClippedImage _bodySurface;
        private float _aspect = 0.7f;
        private float _motionTime;
        private ModConfig _cfg;

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
                    default: slotMin = 0f; slotMax = 1f; break;
                }
            }

            SlotRect.anchorMin = new Vector2(slotMin, 0f);
            SlotRect.anchorMax = new Vector2(slotMax, 1f);
            SlotRect.offsetMin = Vector2.zero;
            SlotRect.offsetMax = Vector2.zero;
            SlotRect.pivot = new Vector2(0.5f, 0.5f);

            if (cfg.CircleEntrance)
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
            if (!_cfg.CircleEntrance) return Surface(name, CharacterRect, 0f, 1f);
            if (_circle == null)
            {
                var circleGo = new GameObject("BodyCircleClip", typeof(RectTransform));
                _circle = (RectTransform)circleGo.transform;
                _circle.SetParent(SlotRect, false);
                _circle.SetAsFirstSibling();
                _circle.anchorMin = _circle.anchorMax = Vector2.zero;
                _circle.pivot = new Vector2(0.5f, 0.5f);
                var proxyGo = new GameObject("BodyPose", typeof(RectTransform));
                _bodyProxy = (RectTransform)proxyGo.transform;
                _bodyProxy.SetParent(_circle, false);
                _bodyProxy.anchorMin = _bodyProxy.anchorMax = Vector2.one * 0.5f;
                _bodyProxy.pivot = new Vector2(0.5f, 0f);
            }
            float split = 1f - _cfg.HeadFraction;
            var body = Surface(name + "_Body", _bodyProxy, 0f, split, true);
            _bodySurface = (CircleClippedImage)body;
            HeadSurface = Surface(name + "_Head", CharacterRect, split, 1f);
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
            if (!cfg.CircleEntrance || SlotRect == null || CharacterRect == null) return;
            // Work in the canvas slot, so portrait output and a changed window size
            // do not inherit the original 1920x1080 corner assumptions.
            float width = SlotRect.rect.width, height = SlotRect.rect.height;
            if (width <= 1f || height <= 1f) return;
            float shorter = Mathf.Min(width, height);
            // The game's portrait monitor layout is 1080x1920, letterboxed when
            // the actual desktop height is shorter (this machine: 1080x1600).
            float gameWidth = height > width ? Mathf.Min(width, height * 1080f / 1920f) : shorter;
            var center = new Vector2(width * cfg.CircleCenterX,
                cfg.CircleCenterY < 0f ? gameWidth * 0.5f : height * Mathf.Clamp01(cfg.CircleCenterY));
            float radius = gameWidth * cfg.CircleRadius;
            float h = Mathf.Min(cfg.CharacterHeightPixels * Mathf.Max(0.05f, cfg.Scale), radius * 1.5f);
            CharacterRect.sizeDelta = new Vector2(h * _aspect, h);
            EntrancePose pose = EntranceMotion.Sample(time, cfg);
            Vector2 peekHead = center + new Vector2(radius * 0.80f, radius * 0.65f);
            Vector2 fullFoot = center + new Vector2(radius * 0.30f, -radius * 0.70f);
            Vector2 fullHead = fullFoot + new Vector2(0f, h * (1f - cfg.HeadFraction * 0.5f));
            Vector2 hiddenHead = new Vector2(width + h * _aspect, peekHead.y);
            Vector2 head = pose.Reveal <= 1f ? Vector2.Lerp(hiddenHead, peekHead, pose.Reveal)
                : Vector2.Lerp(peekHead, fullHead, pose.Reveal - 1f);
            Quaternion rotation = Quaternion.Euler(0f, 0f, pose.Tilt);
            Vector3 headFromFoot = rotation * new Vector3(0f, h * (1f - cfg.HeadFraction * 0.5f), 0f);
            CharacterRect.anchoredPosition = head - new Vector2(headFromFoot.x, headFromFoot.y);
            CharacterRect.localRotation = rotation;
            if (_circle != null)
            {
                _circle.anchoredPosition = center;
                _circle.sizeDelta = Vector2.one * radius * 2f;
                _bodyProxy.sizeDelta = CharacterRect.sizeDelta;
                _bodyProxy.anchoredPosition = CharacterRect.anchoredPosition - center;
                _bodyProxy.localRotation = rotation;
                if (_bodySurface != null)
                {
                    _bodySurface.SetCircle(SlotRect, center, radius, pose.Reveal - 1f);
                }
            }
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
