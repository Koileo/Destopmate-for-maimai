using System;
using UnityEngine;

namespace DesktopMateOnMaimai.Core.Motion
{
    /// <summary>
    /// 动作运行时：三层叠加。
    ///
    ///   基础层  : 呼吸 + 重心漂移 + 噪声微动，永远在跑，保证"不动的时候也在动"
    ///   手势层  : 一段关键帧动作，按权重叠加在基础层之上（相对静止位的增量），带淡入淡出与回弹
    ///   视线/表情: 眨眼、眼球与头部软钳制、表情过渡，独立于前两层，所以做动作时不会忘记眨眼
    ///
    /// 这三层的时间基互不同步：呼吸周期会随噪声缓慢加减速，重心漂移有独立相位，
    /// 于是不会出现"所有部位同一拍到顶"的机械感。
    /// </summary>
    internal sealed class MotionRig
    {
        private const float BlinkClose = 0.07f;
        private const float BlinkHold = 0.03f;
        private const float BlinkOpen = 0.13f;

        private readonly Pose _base = new Pose();
        private readonly Pose _clipPose = new Pose();
        private readonly Pose _out = new Pose();
        private readonly Pose _gestureDelta = new Pose();
        private readonly Pose _previousDelta = new Pose();
        private float _transitionTime = 1f;
        private bool _entranceDriven;

        private readonly System.Random _rng = new System.Random(Environment.TickCount);

        private float _time;
        private float _breathPhase = 0.35f;
        private float _shiftPhase = 1.1f;
        private float _breathPeriod = 3.9f;
        private float _shiftPeriod = 9.2f;

        private PoseClip _clip;
        private float _clipTime;
        private float _clipWeight;
        private float _intensity = 1f;
        private float _speed = 1f;

        private Spring _settle;

        private Damp _eyeYaw, _eyePitch, _headYaw, _headPitch;
        private Vector3 _gaze = new Vector3(0f, 0f, 0.4f);

        private int _blinkStage;      // 0 等待 / 1 闭 / 2 停 / 3 开 / 4 二连眨间隔
        private float _blinkTimer;
        private float _blinkValue;
        private float _nextBlink = 2.6f;
        private float _doubleChance = 0.18f;
        private float _blinkMaster = 1f;

        private float _faceSmile;
        private Damp _smile, _squint, _mouth;
        private float _drowsy;
        private bool _legMotion = true;
        private readonly float _idleMotion;

        public Pose Output { get { return _out; } }
        public bool Busy { get { return _clip != null; } }
        public string CurrentGesture { get { return _clip != null ? _clip.Name : null; } }

        public MotionRig(float breathSeconds, float intensity, float doubleBlinkChance, bool enableBlink, bool enableLegMotion, float idleMotion = 1f)
        {
            _idleMotion = Mathf.Clamp01(idleMotion);
            if (breathSeconds > 0.5f) _breathPeriod = breathSeconds;
            _intensity = Mathf.Clamp(intensity, 0f, 1.5f);
            _doubleChance = Mathf.Clamp01(doubleBlinkChance);
            _blinkMaster = enableBlink ? 1f : 0f;
            _legMotion = enableLegMotion;
            _nextBlink = NextBlinkDelay();
            _out.CopyFrom(ClipLibrary.Neutral);
        }

        public void Reset()
        {
            _clip = null;
            _clipTime = 0f;
            _clipWeight = 0f;
            _entranceDriven = false;
            _gestureDelta.Reset();
            _previousDelta.Reset();
            _transitionTime = 1f;
            _settle.Reset();
            _eyeYaw.Reset(0f);
            _eyePitch.Reset(0f);
            _headYaw.Reset(0f);
            _headPitch.Reset(0f);
            _blinkStage = 0;
            _blinkValue = 0f;
            _nextBlink = NextBlinkDelay();
            _gaze = new Vector3(0f, 0f, 0.4f);
            _faceSmile = _drowsy = 0f;
            _smile.Reset(0f);
            _squint.Reset(0f);
            _mouth.Reset(0f);
        }

        public void SetSpeed(float speed)
        {
            _speed = Mathf.Clamp(speed, 0.2f, 2.5f);
        }

        public void SetIntensity(float intensity)
        {
            _intensity = Mathf.Clamp(intensity, 0f, 1.5f);
        }

        public void LookAt(Vector3 localPoint)
        {
            _gaze = localPoint;
        }

        // ---------------- 触发 ----------------

        public bool Play(string clipName)
        {
            if (string.Equals(clipName, "Blink", StringComparison.OrdinalIgnoreCase))
            {
                StartBlink();
                return true;
            }

            PoseClip clip = ClipLibrary.Get(clipName);
            if (clip == null) return false;

            // 同一段动作正在播且还没过半，就不重开，避免连点打断节奏
            if (_clip == clip && _clipTime < clip.Length * 0.5f) return true;

            _clip = clip;
            _clipTime = 0f;
            _clipWeight = 0f;
            _previousDelta.CopyFrom(_gestureDelta);
            _transitionTime = 0f;
            _entranceDriven = false;
            return true;
        }

        // 进场由同一条空间时间线直接采样，不受随机待机的速度倍率影响。
        public void SampleEntrance(string clipName, float progress)
        {
            if (clipName == null)
            {
                if (!_entranceDriven) return;
                _previousDelta.CopyFrom(_gestureDelta);
                _transitionTime = 0f;
                _clip = null;
                _entranceDriven = false;
                return;
            }
            PoseClip clip = ClipLibrary.Get(clipName);
            if (clip == null) return;
            if (!_entranceDriven || _clip != clip)
            {
                _previousDelta.CopyFrom(_gestureDelta);
                _transitionTime = 0f;
            }
            _clip = clip;
            _entranceDriven = true;
            _clipTime = Mathf.Clamp01(progress) * clip.Length;
        }

        public void PlayFace(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            string lower = name.ToLowerInvariant();
            if (lower == "blink") StartBlink();
            else if (lower.Contains("smile") || lower.Contains("joy")) _faceSmile = 1f;
            else if (lower.Contains("sleep")) _drowsy = 1f;
            else if (lower == "default" || lower == "normal") { _faceSmile = 0f; _drowsy = 0f; }
        }

        public void SetSleep(bool asleep)
        {
            _drowsy = asleep ? 1f : 0f;
        }

        // ---------------- 每帧 ----------------

        public void Tick(float dt)
        {
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            if (dt <= 0f) return;
            // 消耗完整的共享 dt，同时把弹簧与眨眼积分拆成小步。
            int steps = (int)Math.Ceiling(dt / (1f / 60f));
            float step = dt / steps;
            for (int i = 0; i < steps; i++) TickStep(step);
        }

        private void TickStep(float dt)
        {

            _time += dt * _speed;
            BuildBase(dt);

            _out.CopyFrom(_base);
            AdvanceGesture(dt);
            ApplyGaze(dt);
            UpdateBlink(dt);
            UpdateFace(dt);
            ApplySettle(dt);

            if (!_legMotion)
            {
                Pose neutral = ClipLibrary.Neutral;
                for (int i = Pose.ThighFwdL; i <= Pose.AnkleR; i++) _out.V[i] = neutral.V[i];
            }
        }

        /// <summary>基础层：呼吸、重心漂移、噪声微动。这些量都很小，但它们决定"活人感"。</summary>
        private void BuildBase(float dt)
        {
            _base.CopyFrom(ClipLibrary.Neutral);

            // 呼吸：吸气快、顶点略停、呼气慢（等速正弦是"机械感"的最大来源）
            _breathPhase += dt / _breathPeriod * (1f + 0.08f * MotionNoise.Fbm(_time * 0.07f, 41));
            float br = BreathCurve(_breathPhase - Mathf.Floor(_breathPhase));

            _base[Pose.HipsDropY] += 0.0045f * br;
            _base[Pose.ChestPitch] += -1.6f * br;
            _base[Pose.Spine2Pitch] += -0.9f * br;
            _base[Pose.Spine1Pitch] += -0.5f * br;
            _base[Pose.NeckPitch] += -0.4f * br;
            _base[Pose.HeadPitch] += 0.5f - 0.7f * br;
            _base[Pose.ShoulderLiftL] += 1.1f * br;
            _base[Pose.ShoulderLiftR] += 1.1f * br;

            // 重心漂移：胯先走，脊柱反向、头再回一半，最后腿微调（承重腿伸直）
            _shiftPhase += dt * (2f * Mathf.PI / _shiftPeriod) * (1f + 0.25f * MotionNoise.Fbm(_time * 0.05f, 91));
            float ws = Mathf.Sin(_shiftPhase);
            _base[Pose.HipsShiftX] += 0.008f * ws;
            _base[Pose.HipsRoll] += 0.9f * ws;
            _base[Pose.Spine2Roll] += -0.5f * ws;
            _base[Pose.ChestRoll] += -0.45f * ws;
            _base[Pose.HeadRoll] += 0.7f * ws;
            _base[Pose.ThighOutL] += 0.8f * ws;
            _base[Pose.KneeL] += -1.2f * Mathf.Max(0f, ws);
            _base[Pose.KneeR] += -1.2f * Mathf.Max(0f, -ws);

            // 噪声微动：每条通道独立相位，绝不与呼吸同拍
            _base[Pose.HeadYaw] += 1.7f * MotionNoise.Fbm(_time * 0.23f, 11);
            _base[Pose.HeadPitch] += 1.1f * MotionNoise.Fbm(_time * 0.19f, 23);
            _base[Pose.HeadRoll] += 1.3f * MotionNoise.Fbm(_time * 0.17f, 37);
            _base[Pose.Spine2Yaw] += 0.9f * MotionNoise.Fbm(_time * 0.11f, 53);
            _base[Pose.ChestYaw] += 0.7f * MotionNoise.Fbm(_time * 0.09f, 67);
            _base[Pose.ArmDownL] += 1.4f * MotionNoise.Fbm(_time * 0.27f, 71);
            _base[Pose.ArmDownR] += 1.4f * MotionNoise.Fbm(_time * 0.24f, 83);
            _base[Pose.ElbowL] += 1.8f * MotionNoise.Fbm(_time * 0.31f, 89);
            _base[Pose.ElbowR] += 1.8f * MotionNoise.Fbm(_time * 0.29f, 97);

            // 困倦：肩塌、脊柱松、头沉
            if (_drowsy > 0.001f)
            {
                _base[Pose.HeadPitch] += 6f * _drowsy;
                _base[Pose.NeckPitch] += 2f * _drowsy;
                _base[Pose.ChestPitch] += 2.5f * _drowsy;
                _base[Pose.ShoulderLiftL] -= 2f * _drowsy;
                _base[Pose.ShoulderLiftR] -= 2f * _drowsy;
                _base[Pose.KneeL] += 1.5f * _drowsy;
                _base[Pose.KneeR] += 1.5f * _drowsy;
            }
            // The idle layer must obey the same amplitude as native standing.
            // Gestures, blinking and expressions are layered separately.
            Pose.Lerp(_base, ClipLibrary.Neutral, _base, _idleMotion);
        }

        /// <summary>0..1 的呼吸曲线：40% 吸气、8% 停、52% 呼气。</summary>
        private static float BreathCurve(float phase)
        {
            if (phase < 0.40f) return EaseUtil.Apply(Ease.OutCubic, phase / 0.40f);
            if (phase < 0.48f) return 1f;
            return 1f - EaseUtil.Apply(Ease.InOutSine, (phase - 0.48f) / 0.52f);
        }

        private void AdvanceGesture(float dt)
        {
            _gestureDelta.Reset();
            _transitionTime += dt;
            if (_clip != null)
            {
                if (!_entranceDriven) _clipTime += dt * _speed;

                float weight = 1f;
                if (!_entranceDriven && _clip.FadeIn > 0.001f && _clipTime < _clip.FadeIn)
                    weight = _clipTime / _clip.FadeIn;
                float tail = _clip.Length - _clipTime;
                if (_clip.FadeOut > 0.001f && tail < _clip.FadeOut)
                    weight = Mathf.Min(weight, Mathf.Max(0f, tail / _clip.FadeOut));

                _clipWeight = Mathf.Clamp01(EaseUtil.Apply(Ease.InOutSine, weight));
                _clip.Sample(_clipTime, _clipPose);
                Pose.AddDelta(_gestureDelta, _clipPose, ClipLibrary.Neutral, _clipWeight * _intensity);

                if (!_entranceDriven && _clipTime >= _clip.Length)
                {
                    _clip = null;
                    _clipWeight = 0f;
                    // 收尾回弹：身体不是"停住"，而是落定
                    _settle.Kick(0.6f);
                }
            }
            else _clipWeight = 0f;

            // 中途换段保留旧姿态再柔和交接，肩、肘、腕不会一帧归零。
            float blend = EaseUtil.Apply(Ease.Smooth, _transitionTime / 0.16f);
            Pose.Lerp(_gestureDelta, _previousDelta, _gestureDelta, blend);
            for (int i = 0; i < Pose.Count; i++) _out.V[i] += _gestureDelta.V[i];
        }

        private void ApplySettle(float dt)
        {
            if (Mathf.Abs(_settle.Value) < 0.0005f && Mathf.Abs(_settle.Velocity) < 0.0005f) return;
            float s = _settle.Step(90f, 9.5f, dt);
            _out[Pose.HipsDropY] += -0.0035f * s;
            _out[Pose.HeadPitch] += 1.6f * s;
            _out[Pose.ChestPitch] += -0.8f * s;
            _out[Pose.KneeL] += 1.2f * s;
            _out[Pose.KneeR] += 1.2f * s;
        }

        /// <summary>
        /// 视线软钳制：眼睛先吃到 ±14°，还不够才转头（上限 26°）。
        /// 眼球用极短时间常数（急动），头用较长时间常数（跟动），于是"眼先到、头后到"。
        /// </summary>
        private void ApplyGaze(float dt)
        {
            float depth = Mathf.Max(0.08f, _gaze.z);
            float yaw = Mathf.Atan2(_gaze.x, depth) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(_gaze.y, depth) * Mathf.Rad2Deg;

            float sign = yaw > 0.001f ? 1f : (yaw < -0.001f ? -1f : 0f);
            float abs = Mathf.Abs(yaw);
            const float eyeMax = 14f;
            const float headMax = 26f;
            float eyesAbs = Mathf.Min(abs, eyeMax);
            float headAbs = Mathf.Clamp(abs * 0.5f, 0f, headMax);
            if (abs > eyeMax) headAbs = Mathf.Max(headAbs, Mathf.Min(abs - eyeMax + 5f, headMax));

            float psign = pitch > 0.001f ? 1f : (pitch < -0.001f ? -1f : 0f);
            float pabs = Mathf.Abs(pitch);
            float eyePitchAbs = Mathf.Min(pabs, 9f);
            float headPitchAbs = Mathf.Clamp(pabs * 0.45f, 0f, 16f);

            _eyeYaw.Step(sign * eyesAbs, 0.05f, dt);
            _eyePitch.Step(psign * eyePitchAbs, 0.06f, dt);
            _headYaw.Step(sign * headAbs, 0.30f, dt);
            _headPitch.Step(psign * headPitchAbs, 0.34f, dt);

            // 做动作时头部让位给动作本身，但眼睛继续看（这是"分心"的真实表现）
            float headShare = 1f - 0.75f * _clipWeight;

            _out[Pose.EyeYaw] += _eyeYaw.Value;
            _out[Pose.EyePitch] += _eyePitch.Value;
            _out[Pose.HeadYaw] += _headYaw.Value * headShare;
            _out[Pose.NeckYaw] += _headYaw.Value * 0.18f * headShare;
            _out[Pose.ChestYaw] += _headYaw.Value * 0.06f * headShare;
            _out[Pose.HeadPitch] += _headPitch.Value * headShare;
            _out[Pose.HeadRoll] += _headYaw.Value * 0.05f * headShare;
        }

        private void UpdateBlink(float dt)
        {
            if (_blinkMaster <= 0f)
            {
                _out[Pose.Blink] = 0f;
                return;
            }

            switch (_blinkStage)
            {
                case 1:
                    _blinkTimer += dt;
                    _blinkValue = EaseUtil.Apply(Ease.InQuad, _blinkTimer / BlinkClose);
                    if (_blinkTimer >= BlinkClose) { _blinkStage = 2; _blinkTimer = 0f; }
                    break;
                case 2:
                    _blinkValue = 1f;
                    _blinkTimer += dt;
                    if (_blinkTimer >= BlinkHold) { _blinkStage = 3; _blinkTimer = 0f; }
                    break;
                case 3:
                    _blinkTimer += dt;
                    _blinkValue = 1f - EaseUtil.Apply(Ease.OutQuad, _blinkTimer / BlinkOpen);
                    if (_blinkTimer >= BlinkOpen)
                    {
                        _blinkValue = 0f;
                        _nextBlink = NextBlinkDelay();
                        // 二连眨：真实眨眼有相当比例是两下，匀一下"机器感"
                        _blinkStage = _rng.NextDouble() < _doubleChance ? 4 : 0;
                        _blinkTimer = 0f;
                    }
                    break;
                case 4:
                    _blinkTimer += dt;
                    if (_blinkTimer >= 0.11f) { _blinkStage = 1; _blinkTimer = 0f; }
                    break;
                default:
                    _nextBlink -= dt;
                    if (_nextBlink <= 0f) { _blinkStage = 1; _blinkTimer = 0f; }
                    break;
            }

            _out[Pose.Blink] = Mathf.Clamp01(_blinkValue);
        }

        private void UpdateFace(float dt)
        {
            _smile.Step(_faceSmile, 0.20f, dt);
            _squint.Step(_drowsy * 0.5f + _smile.Value * 0.15f, 0.25f, dt);
            _mouth.Step(0f, 0.20f, dt);

            _out[Pose.Smile] = Mathf.Clamp01(_smile.Value);
            _out[Pose.Squint] = Mathf.Clamp01(_squint.Value);
            // 手势里的张嘴（打哈欠）与表情轨取较大值，避免相加后夸张
            _out[Pose.MouthOpen] = Mathf.Clamp01(Mathf.Max(_out[Pose.MouthOpen], _mouth.Value));
        }

        private void StartBlink()
        {
            if (_blinkMaster <= 0f) return;
            if (_blinkStage == 1 || _blinkStage == 2) return;
            _blinkStage = 1;
            _blinkTimer = 0f;
        }

        private float NextBlinkDelay()
        {
            // 2.4~6.4s，长间隔按 20% 概率出现，贴近真人眨眼分布
            float baseDelay = 2.4f + (float)_rng.NextDouble() * 2.4f;
            if (_rng.NextDouble() < 0.2f) baseDelay += 1.2f + (float)_rng.NextDouble() * 1.6f;
            return baseDelay;
        }
    }
}
