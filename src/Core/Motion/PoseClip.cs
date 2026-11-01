using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesktopMateOnMaimai.Core.Motion
{
    internal struct PoseKey
    {
        public float Time;
        public Ease Ease;
        public Pose Pose;
    }

    /// <summary>
    /// 一段关键帧动作。采样按「左邻键的缓动」插值：
    /// 每段自己的 ease 决定了是「快速起步」还是「缓慢落定」，
    /// 这就是补出来的过渡帧，而不是在两个姿态之间线性硬插。
    /// </summary>
    internal sealed class PoseClip
    {
        public readonly string Name;
        public readonly float Length;
        public readonly float FadeIn;
        public readonly float FadeOut;
        public readonly Pose Rest;
        private readonly PoseKey[] _keys;

        public PoseClip(string name, float length, float fadeIn, float fadeOut, PoseKey[] keys)
        {
            Name = name;
            Length = Mathf.Max(0.05f, length);
            FadeIn = Mathf.Max(0f, fadeIn);
            FadeOut = Mathf.Max(0f, fadeOut);
            _keys = keys;
            Rest = keys.Length > 0 ? keys[0].Pose : new Pose();
        }

        public void Sample(float t, Pose dst)
        {
            if (_keys.Length == 0) { dst.Reset(); return; }
            if (t <= _keys[0].Time) { dst.CopyFrom(_keys[0].Pose); return; }
            for (int i = 1; i < _keys.Length; i++)
            {
                if (t > _keys[i].Time) continue;
                PoseKey a = _keys[i - 1];
                PoseKey b = _keys[i];
                float span = b.Time - a.Time;
                float u = span <= 0.0001f ? 1f : (t - a.Time) / span;
                Pose.Lerp(dst, a.Pose, b.Pose, EaseUtil.Apply(b.Ease, u));
                return;
            }
            dst.CopyFrom(_keys[_keys.Length - 1].Pose);
        }
    }

    /// <summary>
    /// 动作库。每个关键帧只需写「相对站姿的改动」，静止位由 Neutral 统一提供，
    /// 这样改一处站姿，所有动作的基准一起跟着变。
    /// </summary>
    internal static class ClipLibrary
    {
        private static readonly Dictionary<string, PoseClip> Clips =
            new Dictionary<string, PoseClip>(StringComparer.OrdinalIgnoreCase);

        /// <summary>静止站姿：手臂 74° 下垂、肘微屈、膝微松。所有 clip 的 0 帧都是它。</summary>
        private static Pose _neutral;

        public static Pose Neutral
        {
            get
            {
                if (_neutral == null) _neutral = BuildNeutral();
                return _neutral;
            }
        }

        private static Pose BuildNeutral()
        {
            var p = new Pose();
            p[Pose.ArmDownL] = 74f;
            p[Pose.ArmDownR] = 74f;
            p[Pose.ArmFwdL] = 3f;
            p[Pose.ArmFwdR] = 3f;
            p[Pose.ArmTwistL] = 6f;
            p[Pose.ArmTwistR] = 6f;
            p[Pose.ElbowL] = 9f;
            p[Pose.ElbowR] = 9f;
            p[Pose.WristL] = 2f;
            p[Pose.WristR] = 2f;
            p[Pose.ShoulderLiftL] = -1.5f;
            p[Pose.ShoulderLiftR] = -1.5f;
            p[Pose.ThighOutL] = 2.2f;
            p[Pose.ThighOutR] = 2.2f;
            p[Pose.KneeL] = 4f;
            p[Pose.KneeR] = 4f;
            p[Pose.AnkleL] = 0.5f;
            p[Pose.AnkleR] = 0.5f;
            p[Pose.Spine1Pitch] = 0.8f;
            p[Pose.Spine2Pitch] = 0.4f;
            p[Pose.ChestPitch] = -1.2f;
            p[Pose.NeckPitch] = 0.8f;
            p[Pose.HeadPitch] = -0.6f;
            return p;
        }

        public static PoseClip Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Build();
            PoseClip clip;
            return Clips.TryGetValue(name, out clip) ? clip : null;
        }

        public static IEnumerable<string> Names()
        {
            Build();
            return Clips.Keys;
        }

        private static void Build()
        {
            if (Clips.Count > 0) return;
            Add(Wave());
            Add(Peek());
            Add(Stretch());
            Add(Sigh());
            Add(Shift());
            Add(LookAround());
            Add(Nod());
        }

        private static void Add(PoseClip clip)
        {
            Clips[clip.Name] = clip;
        }

        private static PoseKey K(float time, Ease ease, Action<Pose> fill)
        {
            var p = new Pose();
            p.CopyFrom(Neutral);
            if (fill != null) fill(p);
            return new PoseKey { Time = time, Ease = ease, Pose = p };
        }

        private static PoseClip Make(string name, float length, float fadeIn, float fadeOut, params PoseKey[] keys)
        {
            return new PoseClip(name, length, fadeIn, fadeOut, keys);
        }

        // ---------------- 挥手 ----------------
        // 抬起 → 过冲 → 三次摆动（幅度递减）→ 落下过冲 → 回位。
        // 摆动全靠肘/腕的反向相位，上臂几乎不动，这是「挥手」不是「摆臂」的关键。
        private static PoseClip Wave()
        {
            var keys = new List<PoseKey>
            {
                K(0f, Ease.Linear, null),
                // 预备：手臂先微微下沉，头部略垂 —— 抬臂前必须有反向蓄力
                K(0.18f, Ease.InQuad, p =>
                {
                    p[Pose.ArmDownR] = 79f;
                    p[Pose.ElbowR] = 13f;
                    p[Pose.ShoulderLiftR] = -4.5f;
                    p[Pose.HipsShiftX] = -0.004f;
                    p[Pose.HeadPitch] = 1.8f;
                    p[Pose.HeadRoll] = -1.2f;
                    p[Pose.ChestYaw] = -1.5f;
                }),
                // 抬起：肩先动、肘后跟，重心移到另一侧腿
                K(0.52f, Ease.OutCubic, p =>
                {
                    p[Pose.ArmDownR] = -6f;
                    p[Pose.ArmFwdR] = 12f;
                    p[Pose.ArmTwistR] = 16f;
                    p[Pose.ElbowR] = 46f;
                    p[Pose.WristR] = -10f;
                    p[Pose.ShoulderLiftR] = 8f;
                    p[Pose.HeadYaw] = -5f;
                    p[Pose.HeadRoll] = -2f;
                    p[Pose.HeadPitch] = -1f;
                    p[Pose.HipsShiftX] = 0.009f;
                    p[Pose.HipsRoll] = -0.8f;
                    p[Pose.Spine2Roll] = 1f;
                    p[Pose.ChestYaw] = -3.5f;
                    p[Pose.ThighOutL] = 5f;
                    p[Pose.KneeL] = 2f;
                    p[Pose.ArmDownL] = 76f;
                }),
                // 过冲：比最终位再高一点，然后回落
                K(0.70f, Ease.OutBack, p =>
                {
                    p[Pose.ArmDownR] = -14f;
                    p[Pose.ElbowR] = 54f;
                    p[Pose.WristR] = -18f;
                    p[Pose.ArmTwistR] = 16f;
                    p[Pose.ShoulderLiftR] = 10f;
                    p[Pose.HeadYaw] = -6.5f;
                    p[Pose.HeadRoll] = -2.6f;
                    p[Pose.HipsShiftX] = 0.009f;
                    p[Pose.ThighOutL] = 5f;
                    p[Pose.ArmDownL] = 76f;
                })
            };

            // 三次摆动，幅度逐次收敛；腕比肘晚 1/2 拍，形成跟随动作
            float[] wristAmp = { -30f, -25f, -19f };
            float[] elbowAmp = { 56f, 53f, 48f };
            float[] armAmp = { -9f, -12f, -13f };
            for (int i = 0; i < 3; i++)
            {
                float t0 = 0.92f + i * 0.34f;
                float w1 = wristAmp[i], e1 = elbowAmp[i], a1 = armAmp[i];
                keys.Add(K(t0, Ease.InOutSine, p =>
                {
                    p[Pose.ArmDownR] = a1;
                    p[Pose.ElbowR] = e1;
                    p[Pose.WristR] = w1;
                    p[Pose.ShoulderLiftR] = 8.5f;
                    p[Pose.HeadYaw] = -6f;
                    p[Pose.HeadRoll] = -2.4f;
                    p[Pose.HeadPitch] = -1.4f;
                    p[Pose.HipsShiftX] = 0.009f;
                    p[Pose.ThighOutL] = 5f;
                    p[Pose.ArmDownL] = 76f;
                }));
                keys.Add(K(t0 + 0.17f, Ease.InOutSine, p =>
                {
                    p[Pose.ArmDownR] = a1 - 8f;
                    p[Pose.ElbowR] = e1 - 13f;
                    p[Pose.WristR] = -w1 * 0.45f;
                    p[Pose.ShoulderLiftR] = 7.5f;
                    p[Pose.HeadYaw] = -3.5f;
                    p[Pose.HeadRoll] = -1.6f;
                    p[Pose.HipsShiftX] = 0.008f;
                    p[Pose.ThighOutL] = 5f;
                    p[Pose.ArmDownL] = 76f;
                }));
            }

            keys.Add(K(1.95f, Ease.InOutCubic, p =>
            {
                p[Pose.ArmDownR] = 24f;
                p[Pose.ElbowR] = 40f;
                p[Pose.WristR] = 0f;
                p[Pose.ArmFwdR] = 8f;
                p[Pose.ShoulderLiftR] = 3f;
                p[Pose.HeadYaw] = -2f;
                p[Pose.HeadRoll] = -0.8f;
                p[Pose.HipsShiftX] = 0.004f;
                p[Pose.ArmDownL] = 75f;
            }));
            // 落下过冲：手臂略低于站姿再弹回，肩随之下沉（跟随动作）
            keys.Add(K(2.28f, Ease.InOutCubic, p =>
            {
                p[Pose.ArmDownR] = 82f;
                p[Pose.ElbowR] = 11f;
                p[Pose.ShoulderLiftR] = -3.5f;
                p[Pose.HipsShiftX] = -0.002f;
                p[Pose.HeadYaw] = 0.6f;
                p[Pose.HeadRoll] = 0.4f;
                p[Pose.ChestYaw] = -0.6f;
            }));
            keys.Add(K(2.55f, Ease.OutQuad, p =>
            {
                p[Pose.ArmDownR] = 73f;
                p[Pose.ElbowR] = 9.5f;
            }));
            keys.Add(K(2.90f, Ease.Linear, null));

            return Make("Wave", 2.90f, 0.18f, 0.40f, keys.ToArray());
        }

        // ---------------- 探头打量 ----------------
        private static PoseClip Peek()
        {
            return Make("Peek", 2.30f, 0.24f, 0.38f,
                K(0f, Ease.Linear, null),
                K(0.40f, Ease.OutCubic, p =>
                {
                    p[Pose.HeadYaw] = 10f;
                    p[Pose.NeckYaw] = 3.5f;
                    p[Pose.ChestYaw] = 5f;
                    p[Pose.Spine2Yaw] = 2.5f;
                    p[Pose.HipsYaw] = 1.5f;
                    p[Pose.HeadPitch] = -3f;
                    p[Pose.NeckPitch] = -1.5f;
                    p[Pose.ChestPitch] = -2.7f;
                    p[Pose.HeadRoll] = 2f;
                    p[Pose.HipsShiftX] = 0.006f;
                    p[Pose.ArmFwdR] = 7f;
                    p[Pose.ElbowR] = 13f;
                    p[Pose.ThighOutR] = 3.5f;
                    p[Pose.KneeR] = 6f;
                }),
                K(0.95f, Ease.InOutSine, p =>
                {
                    p[Pose.HeadYaw] = 12f;
                    p[Pose.NeckYaw] = 4f;
                    p[Pose.ChestYaw] = 6f;
                    p[Pose.Spine2Yaw] = 3f;
                    p[Pose.HipsYaw] = 1.8f;
                    p[Pose.HeadPitch] = -4f;
                    p[Pose.NeckPitch] = -1.8f;
                    p[Pose.ChestPitch] = -3.2f;
                    p[Pose.HeadRoll] = 2.6f;
                    p[Pose.HipsShiftX] = 0.007f;
                    p[Pose.ArmFwdR] = 8f;
                    p[Pose.ElbowR] = 14f;
                    p[Pose.ThighOutR] = 3.5f;
                    p[Pose.KneeR] = 6f;
                }),
                // 歪头换角度：小动作也要有第二拍，否则像定住
                K(1.35f, Ease.InOutSine, p =>
                {
                    p[Pose.HeadYaw] = 9f;
                    p[Pose.ChestYaw] = 4.5f;
                    p[Pose.HeadPitch] = -2f;
                    p[Pose.HeadRoll] = -1.5f;
                    p[Pose.HipsShiftX] = 0.005f;
                    p[Pose.ElbowR] = 12f;
                }),
                K(1.75f, Ease.InOutCubic, p =>
                {
                    p[Pose.HeadYaw] = 3f;
                    p[Pose.ChestYaw] = 1.5f;
                    p[Pose.HipsShiftX] = 0.002f;
                    p[Pose.HeadRoll] = 0.3f;
                }),
                K(2.30f, Ease.Linear, null));
        }

        // ---------------- 伸展 + 打哈欠 ----------------
        private static PoseClip Stretch()
        {
            return Make("Stretch", 3.40f, 0.30f, 0.45f,
                K(0f, Ease.Linear, null),
                K(0.55f, Ease.InOutCubic, p =>
                {
                    p[Pose.ArmDownL] = -18f;
                    p[Pose.ArmDownR] = -20f;
                    // 双臂略向前伸，避免伸展时超出竖向 RenderTexture 的左右边缘。
                    p[Pose.ArmFwdL] = 35f;
                    p[Pose.ArmFwdR] = 35f;
                    p[Pose.ElbowL] = 22f;
                    p[Pose.ElbowR] = 24f;
                    p[Pose.ShoulderLiftL] = 7f;
                    p[Pose.ShoulderLiftR] = 7.5f;
                    p[Pose.ChestPitch] = -5f;
                    p[Pose.Spine2Pitch] = -3f;
                    p[Pose.Spine1Pitch] = -1.5f;
                    p[Pose.HeadPitch] = -7f;
                    p[Pose.NeckPitch] = -3f;
                    p[Pose.HipsDropY] = 0.006f;
                    p[Pose.MouthOpen] = 0.3f;
                    p[Pose.KneeL] = 1.5f;
                    p[Pose.KneeR] = 1.5f;
                }),
                K(1.25f, Ease.InOutSine, p =>
                {
                    // 顶点留出头顶余量；初音实机中 -34/-36 度会切掉手部。
                    p[Pose.ArmDownL] = -20f;
                    p[Pose.ArmDownR] = -22f;
                    p[Pose.ArmFwdL] = 35f;
                    p[Pose.ArmFwdR] = 35f;
                    p[Pose.ElbowL] = 14f;
                    p[Pose.ElbowR] = 15f;
                    p[Pose.ShoulderLiftL] = 9f;
                    p[Pose.ShoulderLiftR] = 9f;
                    p[Pose.ChestPitch] = -7.5f;
                    p[Pose.Spine2Pitch] = -4f;
                    p[Pose.Spine1Pitch] = -2f;
                    p[Pose.HeadPitch] = -9.5f;
                    p[Pose.NeckPitch] = -4f;
                    p[Pose.HipsDropY] = 0.010f;
                    p[Pose.MouthOpen] = 0.55f;
                    p[Pose.KneeL] = 1f;
                    p[Pose.KneeR] = 1f;
                }),
                K(1.90f, Ease.InOutSine, p =>
                {
                    p[Pose.ArmDownL] = -19f;
                    p[Pose.ArmDownR] = -21f;
                    p[Pose.ArmFwdL] = 35f;
                    p[Pose.ArmFwdR] = 35f;
                    p[Pose.ElbowL] = 15f;
                    p[Pose.ElbowR] = 16f;
                    p[Pose.ShoulderLiftL] = 9f;
                    p[Pose.ShoulderLiftR] = 9f;
                    p[Pose.ChestPitch] = -7f;
                    p[Pose.HeadPitch] = -9f;
                    p[Pose.HipsDropY] = 0.009f;
                    p[Pose.MouthOpen] = 0.5f;
                }),
                // 收：先塌肩再落臂，避免两条手臂同时砸下来
                K(2.45f, Ease.InOutCubic, p =>
                {
                    p[Pose.ArmDownL] = 40f;
                    p[Pose.ArmDownR] = 38f;
                    p[Pose.ArmFwdL] = 25f;
                    p[Pose.ArmFwdR] = 25f;
                    p[Pose.ElbowL] = 30f;
                    p[Pose.ElbowR] = 31f;
                    p[Pose.ChestPitch] = 2f;
                    p[Pose.Spine2Pitch] = 1f;
                    p[Pose.HeadPitch] = 4f;
                    p[Pose.HipsDropY] = -0.002f;
                    p[Pose.MouthOpen] = 0.12f;
                    p[Pose.ShoulderLiftL] = -2f;
                    p[Pose.ShoulderLiftR] = -2f;
                }),
                K(2.85f, Ease.OutQuad, p =>
                {
                    p[Pose.ArmDownL] = 75f;
                    p[Pose.ArmDownR] = 73f;
                    p[Pose.ElbowL] = 12f;
                    p[Pose.ElbowR] = 12f;
                    p[Pose.ChestPitch] = -0.5f;
                    p[Pose.HeadPitch] = 0.5f;
                }),
                K(3.40f, Ease.Linear, null));
        }

        // ---------------- 叹气 ----------------
        private static PoseClip Sigh()
        {
            return Make("Sigh", 2.90f, 0.30f, 0.45f,
                K(0f, Ease.Linear, null),
                K(0.45f, Ease.OutCubic, p =>
                {
                    p[Pose.ShoulderLiftL] = 5f;
                    p[Pose.ShoulderLiftR] = 5f;
                    p[Pose.ChestPitch] = -2.5f;
                    p[Pose.HeadPitch] = -2f;
                    p[Pose.NeckPitch] = -1f;
                    p[Pose.MouthOpen] = 0.05f;
                }),
                K(0.85f, Ease.InOutCubic, p =>
                {
                    p[Pose.ShoulderLiftL] = -4f;
                    p[Pose.ShoulderLiftR] = -4f;
                    p[Pose.ChestPitch] = 3.5f;
                    p[Pose.Spine2Pitch] = 2f;
                    p[Pose.Spine1Pitch] = 1.5f;
                    p[Pose.HeadPitch] = 5f;
                    p[Pose.NeckPitch] = 2f;
                    p[Pose.ArmDownL] = 77f;
                    p[Pose.ArmDownR] = 77f;
                    p[Pose.ElbowL] = 12f;
                    p[Pose.ElbowR] = 12f;
                    p[Pose.MouthOpen] = 0.2f;
                }),
                K(1.55f, Ease.InOutSine, p =>
                {
                    p[Pose.ShoulderLiftL] = -4.2f;
                    p[Pose.ShoulderLiftR] = -4.2f;
                    p[Pose.ChestPitch] = 3.8f;
                    p[Pose.Spine2Pitch] = 2.2f;
                    p[Pose.HeadPitch] = 6f;
                    p[Pose.ArmDownL] = 77f;
                    p[Pose.ArmDownR] = 77f;
                    p[Pose.MouthOpen] = 0.08f;
                }),
                K(2.30f, Ease.InOutCubic, p =>
                {
                    p[Pose.ChestPitch] = 0.5f;
                    p[Pose.HeadPitch] = 1f;
                }),
                K(2.90f, Ease.Linear, null));
        }

        // ---------------- 重心转移 ----------------
        // 站立时最容易被忽略、但最能消除「雕像感」的一条：胯先走，脊柱反向，头再回一点。
        private static PoseClip Shift()
        {
            return Make("Shift", 3.20f, 0.40f, 0.55f,
                K(0f, Ease.Linear, null),
                K(0.85f, Ease.InOutCubic, p =>
                {
                    p[Pose.HipsShiftX] = 0.016f;
                    p[Pose.HipsRoll] = 1.6f;
                    p[Pose.HipsYaw] = -1.2f;
                    p[Pose.Spine1Roll] = -0.8f;
                    p[Pose.Spine2Roll] = -1.2f;
                    p[Pose.ChestRoll] = -1.2f;
                    p[Pose.ChestYaw] = 1f;
                    p[Pose.HeadRoll] = 1.6f;
                    p[Pose.HeadYaw] = -2f;
                    p[Pose.ThighOutL] = 5.5f;
                    p[Pose.ThighOutR] = 1f;
                    p[Pose.KneeL] = 1.5f;
                    p[Pose.KneeR] = 7.5f;
                    p[Pose.ArmDownL] = 76.5f;
                    p[Pose.ArmDownR] = 72f;
                }),
                K(1.70f, Ease.InOutSine, p =>
                {
                    p[Pose.HipsShiftX] = 0.017f;
                    p[Pose.HipsRoll] = 1.7f;
                    p[Pose.HipsYaw] = -1.2f;
                    p[Pose.Spine2Roll] = -1.3f;
                    p[Pose.ChestRoll] = -1.2f;
                    p[Pose.ChestYaw] = 1f;
                    p[Pose.HeadRoll] = 1.9f;
                    p[Pose.HeadYaw] = -2.2f;
                    p[Pose.ThighOutL] = 5.5f;
                    p[Pose.ThighOutR] = 1f;
                    p[Pose.KneeL] = 1.5f;
                    p[Pose.KneeR] = 7.5f;
                    p[Pose.ArmDownL] = 76.5f;
                    p[Pose.ArmDownR] = 72f;
                }),
                K(2.45f, Ease.InOutCubic, p =>
                {
                    p[Pose.HipsShiftX] = 0.003f;
                    p[Pose.HipsRoll] = 0.2f;
                    p[Pose.HeadRoll] = 0.4f;
                }),
                K(3.20f, Ease.Linear, null));
        }

        // ---------------- 左右张望 ----------------
        private static PoseClip LookAround()
        {
            return Make("LookAround", 2.60f, 0.26f, 0.40f,
                K(0f, Ease.Linear, null),
                K(0.35f, Ease.OutCubic, p =>
                {
                    p[Pose.HeadYaw] = 15f;
                    p[Pose.NeckYaw] = 5f;
                    p[Pose.ChestYaw] = 3f;
                    p[Pose.Spine2Yaw] = 1.5f;
                    p[Pose.HeadPitch] = -1.5f;
                    p[Pose.HeadRoll] = 1.2f;
                }),
                K(0.85f, Ease.InOutSine, p =>
                {
                    p[Pose.HeadYaw] = 16f;
                    p[Pose.NeckYaw] = 5f;
                    p[Pose.ChestYaw] = 3f;
                    p[Pose.HeadPitch] = -0.5f;
                    p[Pose.HeadRoll] = 0.8f;
                }),
                K(1.20f, Ease.InOutCubic, p =>
                {
                    p[Pose.HeadYaw] = -7f;
                    p[Pose.NeckYaw] = -2f;
                    p[Pose.ChestYaw] = -1.5f;
                    p[Pose.HeadRoll] = -1.5f;
                    p[Pose.HeadPitch] = 0.5f;
                }),
                K(1.70f, Ease.InOutSine, p =>
                {
                    p[Pose.HeadYaw] = -8.5f;
                    p[Pose.NeckYaw] = -2.5f;
                    p[Pose.HeadRoll] = -1.8f;
                }),
                K(2.10f, Ease.InOutCubic, p =>
                {
                    p[Pose.HeadYaw] = -1f;
                    p[Pose.ChestYaw] = 0f;
                }),
                K(2.60f, Ease.Linear, null));
        }

        // ---------------- 点头 ----------------
        private static PoseClip Nod()
        {
            return Make("Nod", 1.00f, 0.14f, 0.28f,
                K(0f, Ease.Linear, null),
                K(0.26f, Ease.InQuad, p =>
                {
                    p[Pose.HeadPitch] = 8f;
                    p[Pose.NeckPitch] = 2.5f;
                }),
                K(0.44f, Ease.OutQuad, p =>
                {
                    p[Pose.HeadPitch] = -2f;
                    p[Pose.NeckPitch] = -0.5f;
                }),
                K(0.66f, Ease.InOutSine, p =>
                {
                    p[Pose.HeadPitch] = 5f;
                    p[Pose.NeckPitch] = 1.5f;
                }),
                K(1.00f, Ease.Linear, null));
        }
    }
}
