using System;
using DesktopMateOnMaimai.Config;
using UnityEngine;

namespace DesktopMateOnMaimai.Core
{
    /// <summary>
    /// 待机大脑：把 DesktopMate 角色包里的三轨逻辑，在机台侧用纯 C# 重放一遍。
    ///
    /// DesktopMate 的角色是「数据驱动」的（DesktopMate.Sdk.Character.CharaStateMachineData），
    /// 其行为由三类动作构成：
    ///   1) 状态机轨：SetBool / SetTrigger / SetRandomBool —— 在若干 Idle 变体之间随机迁徙
    ///   2) 时间抖动轨：AddRandomFloat / SetRandomFloat —— 给每次停留时长加随机扰动，避免机械感
    ///   3) 表情轨：BlinkData(眨眼间隔) / LookAtSoftClip(视线软钳制) / PlayFace(表情切换)
    ///
    /// 机台没有鼠标可跟随，所以视线轨改为「在玩家位置附近缓慢漂移 + 软钳制」，
    /// 这正是 LookAtSoftClip 的原始语义。
    /// </summary>
    internal sealed class IdleBrain
    {
        public enum Mood { Idle, Peek, Stretch, Wave, Sleep }

        private readonly ModConfig _cfg;
        private readonly ICharacterDriver _driver;
        private readonly System.Random _rng = new System.Random(Environment.TickCount);

        private Mood _mood = Mood.Idle;
        private float _moodTimer;
        private float _blinkTimer;
        private float _gazeTimer;
        private Vector3 _gaze;
        private Vector3 _gazeSmooth;
        private bool _sleeping;

        public Mood CurrentMood => _mood;

        public IdleBrain(ModConfig cfg, ICharacterDriver driver)
        {
            _cfg = cfg;
            _driver = driver;
            _blinkTimer = Rand(2.5f, 5.5f);
            _gazeTimer = Rand(2f, 5f);
        }

        public void Reset()
        {
            _mood = Mood.Idle;
            _sleeping = false;
            _moodTimer = Rand(_cfg.IdleMinSeconds, _cfg.IdleMaxSeconds);
            if (_driver != null) _driver.SetBool("Idle", true);
        }

        public void Tick(float dt)
        {
            if (_driver == null || !_driver.Ready) return;

            // ---- 状态机轨 ----
            _moodTimer -= dt;
            if (_moodTimer <= 0f) NextMood();

            // ---- 表情轨：眨眼 ----
            if (_cfg.EnableBlink)
            {
                _blinkTimer -= dt;
                if (_blinkTimer <= 0f)
                {
                    _driver.SetTrigger("Blink");
                    _blinkTimer = Rand(2.2f, 5.8f);
                }
            }

            // ---- 表情轨：视线漂移（软钳制到 ±GazeRadius）----
            if (_cfg.EnableLookAt)
            {
                _gazeTimer -= dt;
                if (_gazeTimer <= 0f)
                {
                    _gaze = new Vector3(
                        Rand(-1f, 1f) * _cfg.GazeRadius,
                        Rand(-0.4f, 0.4f) * _cfg.GazeRadius,
                        0.4f);
                    _gazeTimer = Rand(2.5f, 7f);
                }
                _gazeSmooth = Vector3.Lerp(_gazeSmooth, _gaze, Mathf.Clamp01(dt * 2.5f));
                _driver.LookAt(_gazeSmooth);
            }
        }

        private void NextMood()
        {
            if (_sleeping)
            {
                _mood = Mood.Idle;
                _sleeping = false;
                _driver.SetBool("Sleep", false);
            }
            else
            {
                _mood = PickWeighted();
                Apply(_mood);
            }

            float baseTime = _mood == Mood.Idle
                ? Rand(_cfg.IdleMinSeconds, _cfg.IdleMaxSeconds)
                : Rand(_cfg.IdleMinSeconds * 0.5f, _cfg.IdleMaxSeconds * 0.6f);

            // AddRandomFloat 语义：时长再叠一层抖动
            _moodTimer = baseTime * (1f + Rand(-_cfg.TimeJitter, _cfg.TimeJitter));
        }

        private Mood PickWeighted()
        {
            var variants = _cfg.IdleVariants;
            var weights = _cfg.IdleWeights;
            if (variants == null || variants.Length == 0) return Mood.Idle;

            float total = 0f;
            for (int i = 0; i < variants.Length; i++)
                total += i < weights.Length ? Mathf.Max(0f, weights[i]) : 1f;
            if (total <= 0f) return Mood.Idle;

            float r = (float)_rng.NextDouble() * total;
            for (int i = 0; i < variants.Length; i++)
            {
                float w = i < weights.Length ? Mathf.Max(0f, weights[i]) : 1f;
                if (r < w) return Parse(variants[i]);
                r -= w;
            }
            return Mood.Idle;
        }

        private static Mood Parse(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "peek": return Mood.Peek;
                case "stretch": return Mood.Stretch;
                case "wave": return Mood.Wave;
                case "sleep": return Mood.Sleep;
                default: return Mood.Idle;
            }
        }

        private void Apply(Mood m)
        {
            // 所有变体先用 Trigger 切换，避免 Bool 组合导致的状态卡死
            _driver.SetBool("Idle", m == Mood.Idle);
            _driver.SetBool("Sleep", m == Mood.Sleep);
            _driver.SetTrigger(m.ToString());
            _driver.SetFloat("IdleVariant", (float)(int)m);

            if (m == Mood.Wave) _driver.PlayFace("Smile");
            else if (m == Mood.Peek) _driver.PlayFace("Blink");
            else _driver.PlayFace("Default");
        }

        private float Rand(float min, float max)
        {
            if (max <= min) return min;
            return min + (float)_rng.NextDouble() * (max - min);
        }
    }
}
