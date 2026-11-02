using System;
using DesktopMateOnMaimai.Config;

namespace DesktopMateOnMaimai.Core
{
    /// <summary>进场姿态。</summary>
    internal struct EntrancePose
    {
        /// <summary>0 = 完全在圆外，1 = 头探到圆边，2 = 身体整个进来。</summary>
        public float Reveal;

        /// <summary>整体倾斜（度），探头时歪头，进来后归零。</summary>
        public float Tilt;

        /// <summary>落定时的下沉量（像素），配合回弹使用。</summary>
        public float Dip;

        /// <summary>进入途中的切向弧线权重（0..1），让路径贴合圆周。</summary>
        public float Arc;

        // 上半身先露出，扶边和招手才不会被“只显示头部”的裁切吞掉。
        public float BodyReveal;
    }

    internal enum EntrancePhase
    {
        Hidden,
        Peeking,
        Calling,
        Entering,
        Settling,
        Inside
    }

    /// <summary>
    /// 单向进场时间线：探头 → 主动招呼 → 沿圆周进入 → 落定 → 屏内常驻。
    /// 时间只前进，不因普通隐藏而倒退；消失由透明度负责，不再倒放空间路径。
    /// </summary>
    internal static class EntranceMotion
    {
        public static float Duration(ModConfig cfg)
        {
            return cfg.PeekInSeconds + cfg.PeekHoldSeconds + cfg.EnterSeconds + Math.Max(0f, cfg.EntranceSettleSeconds);
        }

        public static EntrancePhase PhaseAt(float time, ModConfig cfg)
        {
            if (time <= 0f) return EntrancePhase.Hidden;
            // Presenter 恰好钳制到总时长；逐段减法的浮点误差不能让状态永远卡在 Settling。
            if (time >= Duration(cfg)) return EntrancePhase.Inside;
            if (time < cfg.PeekInSeconds) return EntrancePhase.Peeking;
            time -= cfg.PeekInSeconds;
            if (time < cfg.PeekHoldSeconds) return EntrancePhase.Calling;
            time -= cfg.PeekHoldSeconds;
            if (time < cfg.EnterSeconds) return EntrancePhase.Entering;
            time -= cfg.EnterSeconds;
            return time < Math.Max(0f, cfg.EntranceSettleSeconds) ? EntrancePhase.Settling : EntrancePhase.Inside;
        }

        public static EntrancePose Sample(float time, ModConfig cfg)
        {
            if (time < cfg.PeekInSeconds)
            {
                float t = Clamp01(cfg.PeekInSeconds <= 0.001f ? 1f : time / cfg.PeekInSeconds);
                float s = Smoother(t);
                return new EntrancePose { Reveal = s, Tilt = cfg.PeekTiltDegrees * s,
                    BodyReveal = 0.48f * Smoother(Clamp01((t - 0.35f) / 0.65f)) };
            }
            time -= cfg.PeekInSeconds;

            if (time < cfg.PeekHoldSeconds)
                return new EntrancePose { Reveal = 1f, Tilt = cfg.PeekTiltDegrees, BodyReveal = 0.48f };

            time -= cfg.PeekHoldSeconds;

            if (time < cfg.EnterSeconds)
            {
                float e = Clamp01(cfg.EnterSeconds <= 0.001f ? 1f : time / cfg.EnterSeconds);
                float s = Smoother(e);
                float arc = (float)Math.Sin(Math.PI * e);
                // 平方包络在两端位移和速度都为零；普通 sin 会在终点造成轻微径向回退。
                arc *= arc;
                return new EntrancePose
                {
                    Reveal = 1f + s,
                    // 身体沿切线先入，随后逐渐转正；不是整个人直挺挺地平移。
                    Tilt = cfg.PeekTiltDegrees * (1f - s) - 4f * arc,
                    Arc = arc,
                    BodyReveal = 0.48f + 0.52f * s
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
                Dip = 7f * sink,
                BodyReveal = 1f
            };
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        private static float Smoother(float t)
        {
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }
    }

    /// <summary>相对圆心、以圆半径为 1 的头部位置。</summary>
    internal struct CirclePathPose
    {
        public float HeadX;
        public float HeadY;
        public float Radius;
        public float AngleDegrees;
    }

    /// <summary>
    /// 圆周路径：探头点位于圆外右侧；进入时角度与径向距离同时收束，
    /// 形成沿圆周切入的弧线。常驻点落在右上安全带，脚底高于底部提示带。
    /// </summary>
    internal static class CircleEntrancePath
    {
        private const float PeekRadius = 1.015f;

        public static CirclePathPose Sample(EntrancePose pose, ModConfig cfg, float heightRadius, float aspect = 0.7f)
        {
            float restX, restY;
            CircleSafeLayout.RestFoot(cfg, heightRadius, aspect, out restX, out restY);
            restX = CircleSafeLayout.FitRestX(restX, restY, heightRadius, aspect);
            float restHeadY = restY + heightRadius * (1f - cfg.HeadFraction * 0.5f);
            float finalRadius = (float)Math.Sqrt(restX * restX + restHeadY * restHeadY);
            float finalAngle = (float)Math.Atan2(restHeadY, restX) * 180f / (float)Math.PI;

            float radius, angle;
            if (pose.Reveal <= 1f)
            {
                // 起始点连同整张图都在圆外，Reveal 只负责把头送到边缘。
                float hiddenRadius = PeekRadius + 0.55f + heightRadius * 0.9f;
                radius = Lerp(hiddenRadius, PeekRadius, pose.Reveal);
                angle = cfg.CirclePeekAngle;
            }
            else
            {
                float t = Clamp01(pose.Reveal - 1f);
                float delta = finalAngle - cfg.CirclePeekAngle;
                if (delta > 180f) delta -= 360f;
                else if (delta < -180f) delta += 360f;
                angle = cfg.CirclePeekAngle + delta * t + 4f * pose.Arc;
                // 径向只减不增，切向弧度由角度承担；这样既绕圆周进入，
                // 又不会在末尾出现任何把角色往圆外带的小回退。
                radius = Lerp(PeekRadius, finalRadius, t);
            }

            float radians = angle * (float)Math.PI / 180f;
            return new CirclePathPose
            {
                HeadX = (float)Math.Cos(radians) * radius,
                HeadY = (float)Math.Sin(radians) * radius,
                Radius = radius,
                AngleDegrees = angle
            };
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }

    /// <summary>
    /// SDEZ 标题/招揽页的右侧空白带。以整张人物图（含动作中的手、头发）计算，
    /// 不只检查脚底；右边和顶部保留圆周余量，中央和底部提示带始终让空。
    /// </summary>
    internal static class CircleSafeLayout
    {
        public const float Left = 0.48f;
        private const float RestLeft = Left + 0.025f; // 整体倾斜/落定时的横向余量
        public const float Bottom = -0.28f;
        private const float RestBottom = Bottom + 0.06f; // 为落定下沉预留空间（小窗口同样适用）
        private const float Top = 0.80f;
        private const float RimMargin = 0.025f;

        public static float FitHeight(float requested, float aspect)
        {
            aspect = Math.Max(0.1f, aspect);
            float low = 0f, high = Math.Max(0f, Math.Min(requested, Top - RestBottom));
            // 单调求解：整个矩形必须装得进右侧安全带与圆的交集。
            for (int i = 0; i < 24; i++)
            {
                float h = (low + high) * 0.5f;
                float edgeY = Math.Max(Math.Abs(RestBottom), Math.Abs(RestBottom + h));
                float right = (float)Math.Sqrt(Math.Max(0f, 1f - edgeY * edgeY)) - RimMargin;
                if (h * aspect <= right - RestLeft) low = h;
                else high = h;
            }
            return low;
        }

        public static void RestFoot(ModConfig cfg, float height, float aspect, out float x, out float y)
        {
            float angle = cfg.CircleRestAngle * (float)Math.PI / 180f;
            x = (float)Math.Cos(angle) * cfg.CircleRestRadius;
            float right = RestLeft + height * aspect + RimMargin;
            float edge = (float)Math.Sqrt(Math.Max(0f, 1f - right * right));
            y = Math.Max(Math.Max(RestBottom, -edge), Math.Min(Math.Min(Top, edge) - height,
                (float)Math.Sin(angle) * cfg.CircleRestRadius));
        }

        public static float FitRestX(float x, float y, float height, float aspect)
        {
            float halfWidth = height * aspect * 0.5f;
            float edgeY = Math.Max(Math.Abs(y), Math.Abs(y + height));
            float right = (float)Math.Sqrt(Math.Max(0f, 1f - edgeY * edgeY)) - RimMargin;
            return Math.Max(RestLeft + halfWidth, Math.Min(right - halfWidth, x));
        }
    }
}
