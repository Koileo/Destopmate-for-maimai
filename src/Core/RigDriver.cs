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
    internal sealed class RigDriver : ICharacterDriver
    {
        private readonly PortableCharacter _model;
        private readonly PoseBinder _binder;
        private readonly MotionRig _motion;

        public bool Ready
        {
            get { return _model != null && _model.Root != null && _binder != null && _binder.Ready; }
        }

        public bool ManagesBlink { get { return true; } }
        public bool Busy { get { return _motion != null && _motion.Busy; } }

        public RigDriver(PortableCharacter model, ModConfig cfg)
        {
            _model = model;
            _binder = new PoseBinder(model);
            _motion = new MotionRig(cfg.BreathSeconds, cfg.GestureIntensity, cfg.DoubleBlinkChance,
                cfg.EnableBlink, cfg.EnableLegMotion);
            _motion.SetSpeed(cfg.MotionSpeed);
            // 先摆一帧，避免首帧还停在 T-pose
            Tick(0.016f);
        }

        public void Tick(float dt)
        {
            if (!Ready) return;
            _motion.Tick(dt);
            _binder.Apply(_motion.Output);
        }

        public void SetTrigger(string name)
        {
            if (!Ready || string.IsNullOrEmpty(name)) return;
            _motion.Play(name);
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
                _motion.SetSpeed(value <= 0f ? 1f : value);
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
            _motion.LookAt(localPoint);
        }

        public void SetSpeed(float speed)
        {
            if (!Ready) return;
            _motion.SetSpeed(speed <= 0f ? 1f : speed);
        }

        public void ResetPose()
        {
            _motion.Reset();
        }

        public void Dispose()
        {
            if (_model != null) _model.Dispose();
        }
    }
}
