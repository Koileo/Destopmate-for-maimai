using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DesktopMateOnMaimai.Core
{
    /// <summary>
    /// 人物驱动器：待机逻辑（IdleBrain）只通过这个接口说话，
    /// 因此 3D(Animator/VRM) 与 2D(序列帧) 两种实现对上层完全透明。
    /// 接口命名对齐 DesktopMate 的 CharacterManifest 动作集合
    /// (SetBool / SetFloat / SetTrigger / PlayFace / LookAt)。
    /// </summary>
    internal interface ICharacterDriver
    {
        bool Ready { get; }
        void SetTrigger(string name);
        void SetBool(string name, bool value);
        void SetFloat(string name, float value);
        void PlayFace(string name);
        void LookAt(Vector3 localPoint);
        void SetSpeed(float speed);
        void Dispose();
    }

    /// <summary>3D：直接驱动角色预制体上的 Animator。</summary>
    internal sealed class AnimatorDriver : ICharacterDriver
    {
        private readonly Animator _anim;
        private readonly HashSet<string> _bools = new HashSet<string>();
        private readonly HashSet<string> _floats = new HashSet<string>();
        private readonly HashSet<string> _triggers = new HashSet<string>();
        private readonly Transform _lookTarget;
        private readonly int _faceLayer;

        public bool Ready => _anim != null;

        public AnimatorDriver(Animator anim, Transform lookTarget, int faceLayer = -1)
        {
            _anim = anim;
            _lookTarget = lookTarget;
            _faceLayer = faceLayer;

            if (_anim == null) return;
            foreach (var p in _anim.parameters)
            {
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Bool: _bools.Add(p.name); break;
                    case AnimatorControllerParameterType.Float: _floats.Add(p.name); break;
                    case AnimatorControllerParameterType.Trigger: _triggers.Add(p.name); break;
                }
            }
        }

        public void SetTrigger(string name)
        {
            if (_anim != null && _triggers.Contains(name)) _anim.SetTrigger(name);
        }

        public void SetBool(string name, bool value)
        {
            if (_anim != null && _bools.Contains(name)) _anim.SetBool(name, value);
        }

        public void SetFloat(string name, float value)
        {
            if (_anim != null && _floats.Contains(name)) _anim.SetFloat(name, value);
        }

        public void PlayFace(string name)
        {
            if (_anim == null || string.IsNullOrEmpty(name)) return;
            if (_faceLayer >= 0) _anim.Play(name, _faceLayer, 0f);
            else _anim.CrossFade(name, 0.12f);
        }

        public void LookAt(Vector3 localPoint)
        {
            if (_lookTarget != null) _lookTarget.localPosition = localPoint;
        }

        public void SetSpeed(float speed)
        {
            if (_anim != null) _anim.speed = speed;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// 2D 兜底：从目录里读 PNG 序列帧播放。无需额外 shader、无需 Unity 2018.4 重导出，
    /// 适合作为「没有可用 3D 资源」时的默认方案。
    /// 目录结构：Characters/&lt;id&gt;/sprites/&lt;clip&gt;_0.png, _1.png, ...
    /// </summary>
    internal sealed class SpriteDriver : ICharacterDriver
    {
        private readonly RawImage _image, _head;
        private readonly Dictionary<string, Sprite[]> _clips = new Dictionary<string, Sprite[]>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Sprite> _owned = new List<Sprite>();
        private Sprite[] _current;
        private float _fps = 12f, _timer;
        private int _frame;
        private Vector3 _lookBase;
        private string _idleClip;
        private bool _playingOneShot;
        private float _oneShotTimer;

        public bool Ready => _image != null && _clips.Count > 0;

        public SpriteDriver(RawImage image, RawImage head, string spriteDir)
        {
            _image = image;
            _head = head;
            if (_image == null || !Directory.Exists(spriteDir)) return;

            foreach (var file in Directory.GetFiles(spriteDir, "*.png"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                int us = name.LastIndexOf('_');
                string clip = us > 0 ? name.Substring(0, us) : name;

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(file))) { UnityEngine.Object.Destroy(tex); continue; }
                tex.wrapMode = TextureWrapMode.Clamp;

                var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), 100f);
                sp.name = name;
                _owned.Add(sp);

                if (!_clips.TryGetValue(clip, out var arr))
                {
                    arr = new Sprite[0];
                }
                var list = new List<Sprite>(arr) { sp };
                list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                _clips[clip] = list.ToArray();
            }

            if (_clips.Count > 0)
            {
                _idleClip = FirstClipName();
                Play(_idleClip);
            }
        }

        private string FirstClipName()
        {
            foreach (var k in _clips.Keys)
                if (string.Equals(k, "idle", StringComparison.OrdinalIgnoreCase)) return k;
            foreach (var k in _clips.Keys) return k;
            return null;
        }

        public void Play(string clip)
        {
            if (string.IsNullOrEmpty(clip) || !_clips.TryGetValue(clip, out var arr) || arr.Length == 0) return;
            _playingOneShot = string.Equals(clip, _idleClip, StringComparison.OrdinalIgnoreCase) ? false : _playingOneShot;
            if (_current == arr) return;
            _current = arr;
            _frame = 0;
            SetFrame(arr[0]);
        }

        /// <summary>播一次就回到 idle（对应 Trigger / 表情的语义）。</summary>
        private void PlayOnce(string clip)
        {
            if (string.IsNullOrEmpty(clip) || !_clips.TryGetValue(clip, out var arr) || arr.Length == 0) return;
            _current = arr;
            _frame = 0;
            SetFrame(arr[0]);
            _playingOneShot = true;
            _oneShotTimer = Mathf.Max(0.12f, arr.Length / Mathf.Max(1f, _fps));
        }

        public void Tick(float dt)
        {
            if (_current == null) return;

            if (_playingOneShot)
            {
                _oneShotTimer -= dt;
                if (_oneShotTimer <= 0f)
                {
                    _playingOneShot = false;
                    if (!string.IsNullOrEmpty(_idleClip)) Play(_idleClip);
                }
            }

            if (_current.Length <= 1) return;
            _timer += dt * _fps;
            if (_timer < 1f) return;
            _timer = 0f;
            _frame = (_frame + 1) % _current.Length;
            SetFrame(_current[_frame]);
        }

        private void SetFrame(Sprite sprite)
        {
            _image.texture = sprite.texture;
            if (_head != null) _head.texture = sprite.texture;
        }

        public void SetTrigger(string name)
        {
            if (!string.IsNullOrEmpty(name) && !string.Equals(name, "Idle", StringComparison.OrdinalIgnoreCase))
                PlayOnce(name);
        }

        public void SetBool(string name, bool value)
        {
            if (name == "Blink" && value) PlayOnce("Blink");
            else if (name == "Idle" && value) Play(_idleClip);
        }

        public void SetFloat(string name, float value) { if (name == "Speed") _fps = Mathf.Lerp(6f, 24f, Mathf.Clamp01(value)); }
        public void PlayFace(string name) { PlayOnce(name); }
        public void LookAt(Vector3 localPoint) { _lookBase = localPoint; }
        public void SetSpeed(float speed) { _fps = Mathf.Clamp(speed * 12f, 1f, 60f); }

        public void Dispose()
        {
            foreach (var sp in _owned)
            {
                if (sp == null) continue;
                if (sp.texture != null) UnityEngine.Object.Destroy(sp.texture);
                UnityEngine.Object.Destroy(sp);
            }
            _owned.Clear();
            _clips.Clear();
        }
    }
}
