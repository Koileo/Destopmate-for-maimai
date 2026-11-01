using UnityEngine;

namespace DesktopMateOnMaimai.Core.Motion
{
    /// <summary>关键帧之间的插值手感。区分「起步/落定」比统一 smoothstep 更接近手绘动画。</summary>
    internal enum Ease
    {
        Linear,
        Smooth,
        InQuad,
        OutQuad,
        InOutSine,
        OutCubic,
        InOutCubic,
        OutBack,
        InBack
    }

    internal static class EaseUtil
    {
        public static float Apply(Ease ease, float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            switch (ease)
            {
                case Ease.Linear:
                    return t;
                case Ease.InQuad:
                    return t * t;
                case Ease.OutQuad:
                    return 1f - (1f - t) * (1f - t);
                case Ease.InOutSine:
                    return 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
                case Ease.OutCubic:
                {
                    float u = 1f - t;
                    return 1f - u * u * u;
                }
                case Ease.InOutCubic:
                {
                    if (t < 0.5f) return 4f * t * t * t;
                    float u = -2f * t + 2f;
                    return 1f - u * u * u * 0.5f;
                }
                case Ease.OutBack:
                {
                    const float c = 1.70158f;
                    float u = t - 1f;
                    return 1f + (c + 1f) * u * u * u + c * u * u;
                }
                case Ease.InBack:
                {
                    const float c = 1.70158f;
                    return (c + 1f) * t * t * t - c * t * t;
                }
                default:
                    return t * t * (3f - 2f * t);
            }
        }
    }

    /// <summary>
    /// 1D 值噪声。正弦叠加的问题是所有通道共用时间基，会「一起到顶」，
    /// 一眼看出是程序；噪声给每条通道独立的相位，抖动才不会同拍。
    /// </summary>
    internal static class MotionNoise
    {
        private static float Hash(int i, int seed)
        {
            unchecked
            {
                uint h = (uint)(i * 374761393 + seed * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>返回 -1..1，带平滑插值，周期由 x 的整数步长决定。</summary>
        public static float Value(float x, int seed)
        {
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            float a = Hash(i, seed);
            float b = Hash(i + 1, seed);
            float u = f * f * (3f - 2f * f);
            return (a + (b - a) * u) * 2f - 1f;
        }

        /// <summary>两层叠加：低频给「主体」，高频给「细颤」。</summary>
        public static float Fbm(float x, int seed)
        {
            return Value(x, seed) * 0.68f + Value(x * 2.31f + 5.7f, seed + 977) * 0.32f;
        }
    }

    /// <summary>指数趋近（帧率无关）。tau 越大越黏，用于视线跟随与表情过渡。</summary>
    internal struct Damp
    {
        public float Value;

        public Damp(float value)
        {
            Value = value;
        }

        public float Step(float target, float tau, float dt)
        {
            if (tau <= 0.0001f)
            {
                Value = target;
                return Value;
            }
            float k = 1f - Mathf.Exp(-dt / tau);
            Value += (target - Value) * k;
            return Value;
        }

        public void Reset(float value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// 欠阻尼弹簧，专用于「落地后的回弹」。没有这个，所有动作都会在末帧硬停。
    /// </summary>
    internal struct Spring
    {
        public float Value;
        public float Velocity;

        public void Kick(float impulse)
        {
            Velocity += impulse;
        }

        public float Step(float stiffness, float damping, float dt)
        {
            // 半隐式欧拉，dt 已在调用侧钳制，不会发散
            Velocity += (-Value * stiffness - Velocity * damping) * dt;
            Value += Velocity * dt;
            return Value;
        }

        public void Reset()
        {
            Value = 0f;
            Velocity = 0f;
        }
    }
}
