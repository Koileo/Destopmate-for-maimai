using System;
using DesktopMateOnMaimai.Config;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DesktopMateOnMaimai.Core
{
    /// <summary>
    /// 人物显示台。
    /// 3D 模式：独立相机 + RenderTexture + RawImage，严格隔离在专用 Layer，
    ///          渲染时手动 cam.Render()，不参与主相机的后处理/遮挡。
    /// 2D 模式：直接 uGUI Image 播序列帧，零相机开销。
    /// </summary>
    internal sealed class CharacterStage : IDisposable
    {
        public OverlayCanvas Overlay;
        public ICharacterDriver Driver { get; private set; }
        public bool Is3D { get; private set; }
        public bool Ready => Driver != null && Driver.Ready;

        private RawImage _raw;
        private RawImage _image2D;
        private Camera _cam;
        private RenderTexture _rt;
        private GameObject _modelRoot;
        private GameObject _lightRoot;
        private int _layer;

        public static CharacterStage Create(OverlayCanvas oc, ModConfig cfg, string characterDir, string characterId)
        {
            var stage = new CharacterStage { Overlay = oc };
            try
            {
                bool want3D = string.Equals(cfg.Renderer, "3D", StringComparison.OrdinalIgnoreCase);
                string bundlePath = CharacterLoader.FindBundle(characterDir);
                GameObject prefab = null;

                string portablePath = System.IO.Path.Combine(characterDir, "character.dmc");
                if (want3D && System.IO.File.Exists(portablePath))
                {
                    var portable = PortableCharacter.Load(portablePath);
                    stage.Driver = new RigDriver(portable);
                    stage.Build3D(cfg, portable.Root, false);
                    return stage;
                }

                if (want3D && bundlePath != null)
                    prefab = CharacterLoader.LoadPrefab(bundlePath, characterId);

                if (prefab != null) stage.Build3D(cfg, prefab);
                else stage.Build2D(cfg, characterDir);
            }
            catch (Exception e)
            {
                MelonLogger.Error("[DesktopMate] 人物加载失败，已降级: " + e);
                try { stage.Build2D(cfg, characterDir); } catch { }
            }
            return stage;
        }

        // ---------------- 3D ----------------

        private void Build3D(ModConfig cfg, GameObject prefab, bool instantiate = true)
        {
            Is3D = true;
            _layer = Mathf.Clamp(cfg.CharacterLayer, 8, 31);

            // Closely layered clothing needs 24-bit depth; a 16-bit / 50-unit
            // frustum produces visible depth fighting on the imported mesh.
            _rt = new RenderTexture(Mathf.Max(64, cfg.RenderTextureWidth), Mathf.Max(64, cfg.RenderTextureHeight), 24, RenderTextureFormat.ARGB32);
            _rt.antiAliasing = 1;
            _rt.filterMode = FilterMode.Bilinear;
            _rt.Create();

            var camGo = new GameObject("DesktopMate_Camera");
            UnityEngine.Object.DontDestroyOnLoad(camGo);
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false;                       // 手动 Render，避免每帧两次摄像机
            _cam.cullingMask = 1 << _layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.targetTexture = _rt;
            _cam.orthographic = true;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 12f;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.transform.position = new Vector3(0f, 0f, -6f);
            _cam.transform.rotation = Quaternion.identity;

            // 灯光同样只照这个 Layer，绝不影响游戏画面
            _lightRoot = new GameObject("DesktopMate_Lights");
            UnityEngine.Object.DontDestroyOnLoad(_lightRoot);
            AddLight(_lightRoot.transform, new Vector3(30f, 150f, 0f), 1.15f, _layer);
            AddLight(_lightRoot.transform, new Vector3(0f, 20f, 0f), 0.45f, _layer);

            _modelRoot = new GameObject("DesktopMate_Model");
            UnityEngine.Object.DontDestroyOnLoad(_modelRoot);
            var inst = instantiate ? UnityEngine.Object.Instantiate(prefab) : prefab;
            inst.name = "Character";
            inst.transform.SetParent(_modelRoot.transform, false);
            Normalize(inst, out float height);

            SetLayerRecursively(inst, _layer);
            _cam.orthographicSize = height * 0.53f;
            _cam.transform.position = new Vector3(0f, height * 0.5f, -6f);

            _modelRoot.transform.rotation = Quaternion.Euler(0f, cfg.YawDegrees, 0f);
            // Keep the isolated model outside the game's scene camera frusta.
            _modelRoot.transform.position = new Vector3(20000f, 20000f, 0f);
            _cam.transform.position += _modelRoot.transform.position;

            _raw = Overlay.CreateSurface("CharacterSurface");
            _raw.texture = _rt;
            if (Overlay.HeadSurface != null) Overlay.HeadSurface.texture = _rt;
            Overlay.SetAspect((float)_rt.width / _rt.height);
            _raw.raycastTarget = false;

            var lookTarget = new GameObject("LookTarget").transform;
            lookTarget.SetParent(_modelRoot.transform, false);
            lookTarget.localPosition = new Vector3(0f, height * 0.78f, 0.4f);

            if (Driver == null) Driver = new AnimatorDriver(inst.GetComponentInChildren<Animator>(), lookTarget);
        }

        private static void AddLight(Transform parent, Vector3 euler, float intensity, int layer)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = intensity;
            l.color = Color.white;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << layer;   // 只照亮角色层
        }

        /// <summary>把模型归一到「脚在 y=0、高度 1.8 世界单位」，相机取景才可预期。</summary>
        private static void Normalize(GameObject go, out float height)
        {
            height = 1.8f;

            // SkinnedMeshRenderer 的 bounds 在首帧渲染前可能是空的，
            // 所以优先用 sharedMesh.localBounds 累加，拿不到再退回 Renderer.bounds。
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool has = false;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked);
                foreach (var vertex in baked.vertices)
                {
                    Vector3 world = smr.transform.TransformPoint(vertex);
                    if (!has) { b = new Bounds(world, Vector3.zero); has = true; }
                    else b.Encapsulate(world);
                }
                UnityEngine.Object.Destroy(baked);
            }

            if (!has)
            {
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return;
                b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            }

            float h = Mathf.Max(0.01f, b.size.y);
            float scale = 1.8f / h;
            go.transform.localScale *= scale;

            float footOffset = (-b.min.y) * scale;
            go.transform.position += new Vector3(-b.center.x * scale, footOffset, -b.center.z * scale);
            height = 1.8f;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            // VRM 蒙皮网格被相机裁掉时会整体消失，必须打开
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;
        }

        // ---------------- 2D ----------------

        private void Build2D(ModConfig cfg, string characterDir)
        {
            Is3D = false;

            _image2D = Overlay.CreateSurface("CharacterSprite");
            Driver = new SpriteDriver(_image2D, Overlay.HeadSurface, System.IO.Path.Combine(characterDir, "sprites"));
            if (_image2D.texture != null) Overlay.SetAspect((float)_image2D.texture.width / _image2D.texture.height);
        }

        // ---------------- 每帧 ----------------

        public void CaptureTexture(string path)
        {
            if (_rt == null) return;
            RenderTexture previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = _rt;
                image = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0f, 0f, _rt.width, _rt.height), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null) UnityEngine.Object.Destroy(image);
            }
        }

        public void Tick(float dt)
        {
            if (Driver is RigDriver rig) rig.Tick(dt);
            if (Is3D && _cam != null && Overlay.Go != null && Overlay.Go.activeInHierarchy)
                _cam.Render();

            if (Driver is SpriteDriver sp) sp.Tick(dt);
        }

        public void Dispose()
        {
            try
            {
                Driver?.Dispose();
                if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
                if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
                if (_modelRoot != null) UnityEngine.Object.Destroy(_modelRoot);
                if (_lightRoot != null) UnityEngine.Object.Destroy(_lightRoot);
            }
            catch (Exception e) { MelonLogger.Warning("[DesktopMate] 释放人物失败: " + e.Message); }
        }
    }
}
