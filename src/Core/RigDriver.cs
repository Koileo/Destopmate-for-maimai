using System;
using DesktopMateOnMaimai.Config;
using DesktopMateOnMaimai.Core.Motion;
using UnityEngine;

namespace DesktopMateOnMaimai.Core
{
    /// <summary>
    /// 本地角色（DMCHAR01）的 3D 驱动。
    ///
    /// 旧实现只有 4 根骨骼在动（Head / Spine2 / Arm_L / Arm_R），而且挥手是布尔开关，
    /// 一帧内直接从 -65° 跳到 -15°，没有过渡、没有跟随、没有收尾。
    /// 现在改为：MotionRig 负责"动什么"（分层姿态），PoseBinder 负责"怎么动到骨头上"，
    /// 本类只做两者的接线与动作原语翻译。
    /// </summary>
    internal sealed class RigDriver : ICharacterDriver, ICompanionDriver
    {
        private readonly PortableCharacter _model;
        private readonly PoseBinder _binder;
        private readonly MotionRig _motion;
        private readonly NativeMotion _native;
        private readonly StandingAnchor _standingAnchor;
        private bool _seated;
        private float _speed;

        public bool Ready
        {
            get { return _model != null && _model.Root != null && _binder != null && _binder.Ready; }
        }

        public bool ManagesBlink { get { return true; } }
        public bool Busy { get { return _native != null ? _native.Busy : _motion != null && _motion.Busy; } }
        public bool SupportsSeat => _native != null && _native.SupportsSeat;
        public bool SupportsGesture(string name) => _native != null ? _native.SupportsGesture(name) : ClipLibrary.Get(name) != null;
        public void SetSeated(bool seated)
        {
            _seated = seated && SupportsSeat;
            _native?.SetSeated(_seated);
        }
        public Transform SeatHip => _model == null ? null : Array.Find(_model.Nodes, t => t != null && t.name == "Hips");

        public RigDriver(PortableCharacter model, ModConfig cfg, string motionPath = null)
        {
            _model = model;
            if (motionPath != null && System.IO.File.Exists(motionPath))
            {
                try { _native = new NativeMotion(model, motionPath, cfg.ReducedMotion ? Mathf.Min(0.08f, cfg.StandingIdleMotion) : cfg.StandingIdleMotion); }
                catch (Exception e) { MelonLoader.MelonLogger.Warning("[DesktopMate] 原版动作加载失败: " + e.Message); }
            }
            _binder = new PoseBinder(model);
            _motion = new MotionRig(cfg.BreathSeconds, cfg.GestureIntensity, cfg.DoubleBlinkChance,
                cfg.EnableBlink, cfg.EnableLegMotion, cfg.ReducedMotion ? Mathf.Min(0.08f, cfg.StandingIdleMotion) : cfg.StandingIdleMotion);
            _motion.SetSpeed(cfg.MotionSpeed);
            _speed = cfg.MotionSpeed;
            if (_native != null) MelonLoader.MelonLogger.Msg("[DesktopMate] 使用原版 Humanoid 动作曲线（固定整体朝向）");
            // 先摆一帧，避免首帧还停在 T-pose
            Tick(0.016f);
            _standingAnchor = new StandingAnchor(model);
        }

        public void Tick(float dt)
        {
            if (!Ready) return;
            _motion.Tick(dt);
            bool poseChanged = true;
            if (_native != null)
            {
                poseChanged = _native.Tick(Mathf.Clamp(dt, 0f, 0.1f), _speed);
                _binder.ApplyFace(_motion.Output);
            }
            else _binder.Apply(_motion.Output);
            if (!_seated && poseChanged) _standingAnchor?.Apply();
        }

        public void SetTrigger(string name)
        {
            if (!Ready || string.IsNullOrEmpty(name)) return;
            if (_native != null && !string.Equals(name, "Blink", StringComparison.OrdinalIgnoreCase)) _native.Play(name);
            else _motion.Play(name);
        }

        public void SetBool(string name, bool value)
        {
            if (!Ready) return;
            if (string.Equals(name, "Sleep", StringComparison.OrdinalIgnoreCase)) _motion.SetSleep(value);
            else if (string.Equals(name, "Blink", StringComparison.OrdinalIgnoreCase) && value) _motion.Play("Blink");
        }

        public void SetFloat(string name, float value)
        {
            if (!Ready) return;
            if (string.Equals(name, "Speed", StringComparison.OrdinalIgnoreCase))
                SetSpeed(value);
            else if (string.Equals(name, "Intensity", StringComparison.OrdinalIgnoreCase))
                _motion.SetIntensity(value);
        }

        public void PlayFace(string name)
        {
            if (!Ready) return;
            _motion.PlayFace(name);
        }

        public void LookAt(Vector3 localPoint)
        {
            if (!Ready) return;
            _motion.LookAt(new Vector3(0f, 0f, 0.4f));
        }

        public void SetSpeed(float speed)
        {
            if (!Ready) return;
            _motion.SetSpeed(speed <= 0f ? 1f : speed);
            _speed = Mathf.Clamp(speed <= 0f ? 1f : speed, 0.2f, 2.5f);
        }

        public void ResetPose()
        {
            _motion.Reset();
            _native?.Reset();
        }

        public void SampleEntrance(float time, ModConfig cfg)
        {
            if (_native != null || !cfg.AnimatedEntrance) return;
            EntrancePhase phase = EntranceMotion.PhaseAt(time, cfg);
            if (phase == EntrancePhase.Peeking)
                _motion.SampleEntrance("RimPeek", time / cfg.PeekInSeconds);
            else if (phase == EntrancePhase.Calling)
                _motion.SampleEntrance("Call", (time - cfg.PeekInSeconds) / cfg.PeekHoldSeconds);
            else if (phase == EntrancePhase.Entering)
                _motion.SampleEntrance("Enter", (time - cfg.PeekInSeconds - cfg.PeekHoldSeconds) / cfg.EnterSeconds);
            else _motion.SampleEntrance(null, 0f);
        }

        public void Dispose()
        {
            _native?.Dispose();
            if (_model != null) _model.Dispose();
        }
    }
}
