using System;
using DesktopMateOnMaimai.Config;

namespace DesktopMateOnMaimai.Core
{
    /// <summary>进场姿态。</summary>
    internal struct EntrancePose
    {
        /// <summary>0 = 完全在窗外，1 = 头探进圆内，2 = 身体整个进来。</summary>
        public float Reveal;

        /// <summary>整体倾斜（度），探头时歪头，进来后归零。</summary>
        public float Tilt;

        /// <summary>落定时的下沉量（像素），配合回弹使用。</summary>
        public float Dip;

        /// <summary>进入途中的横向弧线权重（0..1），让路径不是直线。</summary>
        public float Arc;
    }

    /// <summary>
    /// 进场时间线：探头 → 停顿 → 身体进入 → 落定回弹。
    ///
    /// 与旧版的差别：
    ///   · 探头用 OutCubic（快起步、缓收住），不再是匀速到位再硬停
    ///   · 歪头用 OutBack，带一点点过冲再回来，符合"探头"的惯性
    ///   · 身体进来后新增 settle 段：整体下沉一下再弹回，占位符不再"啪"地定格
    ///   · 进入路径带一点横向弧度，看起来是从桌边绕进来，不是直线平移
    /// </summary>
    internal static class EntranceMotion
    {
        public static float Duration(ModConfig cfg)
        {
            return cfg.PeekInSeconds + cfg.PeekHoldSeconds + cfg.EnterSeconds + Math.Max(0f, cfg.EntranceSettleSeconds);
        }

        public static EntrancePose Sample(float time, ModConfig cfg)
        {
            if (time < cfg.PeekInSeconds)
            {
                float t = Clamp01(cfg.PeekInSeconds <= 0.001f ? 1f : time / cfg.PeekInSeconds);
                return new EntrancePose { Reveal = OutCubic(t), Tilt = cfg.PeekTiltDegrees * OutBack(t) };
            }
            time -= cfg.PeekInSeconds;

            if (time < cfg.PeekHoldSeconds)
                return new EntrancePose { Reveal = 1f, Tilt = cfg.PeekTiltDegrees };

            time -= cfg.PeekHoldSeconds;

            if (time < cfg.EnterSeconds)
            {
                float e = Clamp01(cfg.EnterSeconds <= 0.001f ? 1f : time / cfg.EnterSeconds);
                float s = InOutCubic(e);
                return new EntrancePose
                {
                    Reveal = 1f + s,
                    Tilt = cfg.PeekTiltDegrees * (1f - s),
                    Arc = (float)Math.Sin(Math.PI * e)
                };
            }
            time -= cfg.EnterSeconds;

            float settleSeconds = Math.Max(0f, cfg.EntranceSettleSeconds);
            float settle = settleSeconds <= 0.001f ? 1f : Clamp01(time / settleSeconds);
            // 必须整段衰减到 0：Duration 之后姿态要完全静止，否则每次采样都会抖
            float decay = 1f - settle;
            float wobble = (float)Math.Exp(-settle * 5.5f) * (float)Math.Sin(settle * 11f) * decay;
            // 从零位移、零速度开始下沉，避免进入落定段时直接跳低 7px。
            // 平方包络也让结束处的位移与速度一起归零。
            float sink = 150f * settle * settle * (float)Math.Exp(-settle * 6.5f)
                * (float)Math.Sin(settle * 9f) * decay * decay;

            return new EntrancePose
            {
                Reveal = 2f,
                Tilt = -1.8f * wobble,
                Dip = 7f * sink
            };
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        private static float OutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private static float InOutCubic(float t)
        {
            if (t < 0.5f) return 4f * t * t * t;
            float u = -2f * t + 2f;
            return 1f - u * u * u * 0.5f;
        }

        private static float OutBack(float t)
        {
            const float c = 1.70158f;
            float u = t - 1f;
            return 1f + (c + 1f) * u * u * u + c * u * u;
        }
    }
}
