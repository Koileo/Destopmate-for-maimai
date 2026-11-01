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
    /// 机台没有鼠标可跟随，所以视线轨改为「在玩家位置附近固视 + 软钳制」。
    ///
    /// 与旧版的三处差别：
    ///   · 大动作之间插入微动作（点头/重心转移/张望/叹气），12 秒的空档不再"石化"
    ///   · 同一个动作不连续出现两次，避免复读感
    ///   · 眨眼交给 RigDriver 的状态机；视线改成"固视 1~3 秒再跳"，而不是连续平移
    /// </summary>
    internal sealed class IdleBrain
    {
        public enum Mood { Idle, Peek, Stretch, Wave, Shift, LookAround, Nod, Sigh, Sleep }

        private readonly ModConfig _cfg;
        private readonly ICharacterDriver _driver;
        private readonly System.Random _rng = new System.Random(Environment.TickCount);

        private Mood _mood = Mood.Idle;
        private Mood _lastMood = Mood.Idle;
        private float _gestureTimer;
        private float _blinkTimer;
        private float _gazeTimer;
        private Vector3 _gazeTarget;
        private Vector3 _gazeSmooth;

        public Mood CurrentMood { get { return _mood; } }

        public IdleBrain(ModConfig cfg, ICharacterDriver driver)
        {
            _cfg = cfg;
            _driver = driver;
            _blinkTimer = Rand(2.5f, 5.5f);
            _gazeTimer = Rand(0.8f, 2.4f);
            _gestureTimer = Rand(1.2f, 3.5f);
        }

        public void Reset()
        {
            _mood = Mood.Idle;
            _lastMood = Mood.Idle;
            _gazeSmooth = Vector3.zero;
            _gazeTarget = new Vector3(0f, 0f, 0.4f);
            _gestureTimer = Rand(1.2f, 3.5f);
            _gazeTimer = Rand(0.8f, 2.4f);
            if (_driver != null)
            {
                _driver.SetBool("Idle", true);
                _driver.SetBool("Sleep", false);
            }
        }

        public void Tick(float dt)
        {
            if (_driver == null || !_driver.Ready) return;
            dt = Mathf.Clamp(dt, 0f, 0.1f);

            // ---- 状态机轨 ----
            _gestureTimer -= dt;
            if (_gestureTimer <= 0f)
            {
                // 上一段动作还没收尾就等一会儿，绝不打断
                if (_driver.Busy) _gestureTimer = 0.35f;
                else BeginGesture();
            }

            // ---- 表情轨：眨眼（驱动自管时不重复下发）----
            if (_cfg.EnableBlink && !_driver.ManagesBlink)
            {
                _blinkTimer -= dt;
                if (_blinkTimer <= 0f)
                {
                    _driver.SetTrigger("Blink");
                    _blinkTimer = Rand(2.2f, 5.8f);
                }
            }

            // ---- 表情轨：视线固视 + 软钳制 ----
            if (_cfg.EnableLookAt)
            {
                _gazeTimer -= dt;
                if (_gazeTimer <= 0f)
                {
                    _gazeTarget = PickGaze();
                    // 固视时长 0.9~3.2s：人眼是"跳-停-跳"，不是匀速平移
                    _gazeTimer = Rand(0.9f, 3.2f);
                }
                // 仅做很短的平滑（0.12s），保留跳视的锐度
                float k = 1f - Mathf.Exp(-dt / 0.12f);
                _gazeSmooth = Vector3.Lerp(_gazeSmooth, _gazeTarget, k);
                _driver.LookAt(_gazeSmooth);
            }
        }

        private Vector3 PickGaze()
        {
            float radius = Mathf.Max(0.05f, _cfg.GazeRadius);
            // 六成时间看玩家（略偏中心），四成时间看向一侧（像是被别处吸引）
            if (_rng.NextDouble() < 0.6)
                return new Vector3(
                    Rand(-0.45f, 0.45f) * radius,
                    Rand(-0.45f, 0.30f) * radius,
                    0.4f);
            return new Vector3(
                Rand(-1.5f, 1.5f) * radius,
                Rand(-0.7f, 0.4f) * radius,
                0.4f);
        }

        // ---------------- 手势调度 ----------------

        private void BeginGesture()
        {
            Mood mood = PickWeighted();

            // 抽到 Idle 时并非什么都不做：多数时间用微动作填满空档，
            // 这是"细腻"与"像雕像"的分界线。只配了 Idle 一个变体时视为"只想站桩"，不插微动作。
            bool microAllowed = _cfg.IdleVariants != null && _cfg.IdleVariants.Length > 1;
            if (mood == Mood.Idle && microAllowed)
            {
                double r = _rng.NextDouble();
                if (r < 0.30) mood = Mood.Shift;
                else if (r < 0.55) mood = Mood.LookAround;
                else if (r < 0.72) mood = Mood.Nod;
                else if (r < 0.84) mood = Mood.Sigh;
            }

            // 单一变体尊重用户选择；Idle 可以连续保持，不应被防重复逻辑变成手势。
            if (microAllowed && mood == _lastMood && mood != Mood.Idle && mood != Mood.Shift)
                mood = Mood.Shift;

            Apply(mood);
            _lastMood = mood;

            float baseTime = _cfg.IdleMinSeconds;
            if (_cfg.IdleMaxSeconds > baseTime) baseTime = Rand(_cfg.IdleMinSeconds, _cfg.IdleMaxSeconds);
            _gestureTimer = GestureSeconds(mood) + baseTime * (1f + Rand(-_cfg.TimeJitter, _cfg.TimeJitter));
            if (_gestureTimer < 0.8f) _gestureTimer = 0.8f;

            // 做动作时把视线收回玩家身上
            _gazeTarget = new Vector3(Rand(-0.3f, 0.3f) * _cfg.GazeRadius, 0f, 0.4f);
        }

        private static float GestureSeconds(Mood mood)
        {
            switch (mood)
            {
                case Mood.Peek: return 2.3f;
                case Mood.Stretch: return 3.4f;
                case Mood.Wave: return 2.9f;
                case Mood.Shift: return 3.2f;
                case Mood.LookAround: return 2.6f;
                case Mood.Nod: return 1.0f;
                case Mood.Sigh: return 2.9f;
                case Mood.Sleep: return 3.0f;
                default: return 0f;
            }
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
                case "shift": return Mood.Shift;
                case "lookaround": return Mood.LookAround;
                case "nod": return Mood.Nod;
                case "sigh": return Mood.Sigh;
                case "sleep": return Mood.Sleep;
                default: return Mood.Idle;
            }
        }

        private void Apply(Mood m)
        {
            _mood = m;

            // 所有变体用 Trigger 切换，避免 Bool 组合导致状态卡死
            _driver.SetBool("Idle", m == Mood.Idle);
            _driver.SetBool("Sleep", m == Mood.Sleep);
            _driver.SetFloat("IdleVariant", (float)(int)m);

            switch (m)
            {
                case Mood.Wave:
                    _driver.SetTrigger("Wave");
                    _driver.PlayFace("Smile");
                    break;
                case Mood.Stretch:
                    _driver.SetTrigger("Stretch");
                    _driver.PlayFace("Smile");
                    break;
                case Mood.Peek:
                    _driver.SetTrigger("Peek");
                    _driver.PlayFace("Default");
                    break;
                case Mood.Sigh:
                    _driver.SetTrigger("Sigh");
                    _driver.PlayFace("Default");
                    break;
                case Mood.Sleep:
                    _driver.SetTrigger("Sigh");
                    _driver.PlayFace("Sleep");
                    break;
                case Mood.Shift:
                    _driver.SetTrigger("Shift");
                    break;
                case Mood.LookAround:
                    _driver.SetTrigger("LookAround");
                    break;
                case Mood.Nod:
                    _driver.SetTrigger("Nod");
                    _driver.PlayFace("Smile");
                    break;
            }
        }

        private float Rand(float min, float max)
        {
            if (max <= min) return min;
            return min + (float)_rng.NextDouble() * (max - min);
        }
    }
}
