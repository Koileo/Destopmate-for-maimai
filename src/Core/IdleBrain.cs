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
    /// 机台侧固定朝向玩家，不再随机移动视线。
    ///
    /// 与旧版的三处差别：
    ///   · 只从已配置且驱动支持的动作中选择，动作之间留出安静停顿
    ///   · 同一个动作不连续出现两次，避免复读感
    ///   · 微笑独立调度；眨眼交给驱动，视线始终朝前
    /// </summary>
    internal sealed class IdleBrain
    {
        public enum Mood { Idle, Peek, Stretch, Wave, Shift, LookAround, Nod, Sigh, Sleep, Think, HandsOnHips }

        private readonly ModConfig _cfg;
        private readonly ICharacterDriver _driver;
        private readonly System.Random _rng = new System.Random(Environment.TickCount);

        private Mood _mood = Mood.Idle;
        private Mood _lastMood = Mood.Idle;
        private float _gestureTimer;
        private float _blinkTimer;
        private bool _seated;
        private Mood[] _combo;
        private int _comboStep;
        private int _lastCombo = -1;
        private float _touchCooldown;
        private float _touchFaceTime;
        private float _expressionTimer;

        public Mood CurrentMood { get { return _mood; } }
        // Touch is feedback, not an extra idle phrase. Never queue or interrupt clips.
        public bool ReactToTouch()
        {
            if (!_cfg.TouchEnabled || _driver == null || !_driver.Ready || _driver.Busy || _touchCooldown > 0f)
                return false;
            _touchCooldown = _cfg.TouchCooldownSeconds;
            _combo = null;
            _comboStep = 0;
            if (!_seated && !_cfg.ReducedMotion)
            {
                foreach (var mood in new[] { Mood.Nod, Mood.Wave, Mood.Shift, Mood.Think })
                {
                    if (!Supported(mood)) continue;
                    Apply(mood);
                    _lastMood = mood;
                    _gestureTimer = GestureSeconds(mood) / Mathf.Max(0.2f, _cfg.MotionSpeed) + 0.8f;
                    break;
                }
            }
            _driver.PlayFace("Smile");
            _touchFaceTime = 1.2f;
            return true;
        }

        public void SetSeated(bool seated)
        {
            _seated = seated;
            if (seated) _driver?.SetBool("Idle", true);
        }

        public IdleBrain(ModConfig cfg, ICharacterDriver driver)
        {
            _cfg = cfg;
            _driver = driver;
            _blinkTimer = Rand(2.5f, 5.5f);
            _gestureTimer = Rand(1.2f, 3.5f);
            _expressionTimer = Rand(4f, 7f);
        }

        public void Reset()
        {
            _combo = null;
            _comboStep = 0;
            _touchFaceTime = 0f;
            _expressionTimer = Rand(4f, 7f);
            _seated = false;
            _mood = Mood.Idle;
            _lastMood = Mood.Idle;
            _gestureTimer = Rand(1.2f, 3.5f);
            if (_driver != null)
            {
                _driver.SetBool("Idle", true);
                _driver.SetBool("Sleep", false);
                _driver.PlayFace("Default");
            }
        }

        public void Tick(float dt)
        {
            if (_driver == null || !_driver.Ready) return;
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            _touchCooldown = Mathf.Max(0f, _touchCooldown - dt);
            if (_touchFaceTime > 0f)
            {
                _touchFaceTime -= dt;
                if (_touchFaceTime <= 0f) _driver.PlayFace("Default");
            }
            // Facial life is independent of the gesture lottery, including
            // native characters and seated/reduced-motion companionship.
            _expressionTimer -= dt;
            if (_expressionTimer <= 0f && _touchFaceTime <= 0f && !_driver.Busy)
            {
                _driver.PlayFace("Smile");
                _touchFaceTime = 2.2f;
                _expressionTimer = Rand(10f, 18f);
            }

            // ---- 状态机轨 ----
            if (!_seated && !_cfg.ReducedMotion) _gestureTimer -= dt;
            if (!_seated && !_cfg.ReducedMotion && _gestureTimer <= 0f)
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

            // Keep attention on the player, also when an older config still
            // enables random looking. Explicitly clear any previous gaze offset.
            _driver.LookAt(new Vector3(0f, 0f, 0.4f));
        }

        // ---------------- 手势调度 ----------------

        private void BeginGesture()
        {
            if (_combo != null)
            {
                Apply(_combo[_comboStep++]);
                _gestureTimer = GestureSeconds(_mood) / Mathf.Max(0.2f, _cfg.MotionSpeed) + 0.8f;
                if (_comboStep >= _combo.Length)
                {
                    _combo = null;
                    _gestureTimer += Rand(_cfg.IdleMinSeconds, Mathf.Max(_cfg.IdleMinSeconds, _cfg.IdleMaxSeconds));
                }
                return;
            }
            Mood mood = PickWeighted();
            if (_cfg.EnableActionCombos && mood != Mood.Idle && TryCombo()) { BeginGesture(); return; }

            // A quiet pause is intentional. Do not fill it with unconfigured
            // swaying, or turn an unsupported action into another silent idle.
            if (Configured(Mood.Idle) && mood == _lastMood && mood != Mood.Idle)
                mood = Mood.Idle;

            if (!Supported(mood)) mood = Mood.Idle;

            Apply(mood);
            _lastMood = mood;

            float baseTime = _cfg.IdleMinSeconds;
            if (_cfg.IdleMaxSeconds > baseTime) baseTime = Rand(_cfg.IdleMinSeconds, _cfg.IdleMaxSeconds);
            _gestureTimer = GestureSeconds(mood) + baseTime * (1f + Rand(-_cfg.TimeJitter, _cfg.TimeJitter));
            if (_gestureTimer < 0.8f) _gestureTimer = 0.8f;

        }

        private bool Supported(Mood mood)
        {
            return mood == Mood.Idle || !(_driver is ICompanionDriver companion) || companion.SupportsGesture(mood.ToString());
        }

        private bool TryCombo()
        {
            // Short phrases, separated by quiet pauses; never interrupt a clip.
            Mood[][] phrases = {
                new[] { Mood.Wave, Mood.Nod },
                new[] { Mood.Think, Mood.Shift },
                new[] { Mood.HandsOnHips, Mood.Think },
                new[] { Mood.Shift, Mood.HandsOnHips },
                new[] { Mood.LookAround, Mood.Nod }
            };
            var available = new System.Collections.Generic.List<int>();
            for (int i = 0; i < phrases.Length; i++)
            {
                bool valid = true;
                foreach (Mood mood in phrases[i])
                {
                    if (!Supported(mood) || !Configured(mood)) { valid = false; break; }
                }
                if (valid) available.Add(i);
            }
            if (available.Count > 1) available.Remove(_lastCombo);
            if (available.Count == 0) return false;
            _lastCombo = available[_rng.Next(available.Count)];
            _combo = phrases[_lastCombo];
            _comboStep = 0;
            return true;
        }

        private bool Configured(Mood mood)
        {
            if (_cfg.IdleVariants == null) return false;
            for (int i = 0; i < _cfg.IdleVariants.Length; i++)
                if (Parse(_cfg.IdleVariants[i]) == mood && (_cfg.IdleWeights == null ||
                    i >= _cfg.IdleWeights.Length || _cfg.IdleWeights[i] > 0f)) return true;
            return false;
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
                case Mood.Think: return 2.7f;
                case Mood.HandsOnHips: return 5.8f;
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
                if (Supported(Parse(variants[i])))
                    total += weights != null && i < weights.Length ? Mathf.Max(0f, weights[i]) : 1f;
            if (total <= 0f) return Mood.Idle;

            float r = (float)_rng.NextDouble() * total;
            for (int i = 0; i < variants.Length; i++)
            {
                if (!Supported(Parse(variants[i]))) continue;
                float w = weights != null && i < weights.Length ? Mathf.Max(0f, weights[i]) : 1f;
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
                case "think": return Mood.Think;
                case "handsonhips": return Mood.HandsOnHips;
                default: return Mood.Idle;
            }
        }

        private void Apply(Mood m)
        {
            _mood = m;
            if (_touchFaceTime <= 0f) _driver.PlayFace("Default");

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
                case Mood.Think:
                    _driver.SetTrigger("Think");
                    break;
                case Mood.HandsOnHips:
                    _driver.SetTrigger("HandsOnHips");
                    _driver.PlayFace("Smile");
                    _touchFaceTime = 2.2f;
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
