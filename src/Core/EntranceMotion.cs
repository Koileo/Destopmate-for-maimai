using System;
using DesktopMateOnMaimai.Config;

namespace DesktopMateOnMaimai.Core
{
    internal struct EntrancePose
    {
        public float Reveal; // 0: outside window, 1: head peeking, 2: standing inside circle
        public float Tilt;
    }

    internal static class EntranceMotion
    {
        public static float Duration(ModConfig cfg) => cfg.PeekInSeconds + cfg.PeekHoldSeconds + cfg.EnterSeconds;

        public static EntrancePose Sample(float time, ModConfig cfg)
        {
            if (time < cfg.PeekInSeconds)
            {
                float t = Smooth(time / cfg.PeekInSeconds);
                return new EntrancePose { Reveal = t, Tilt = cfg.PeekTiltDegrees * t };
            }
            time -= cfg.PeekInSeconds;
            if (time < cfg.PeekHoldSeconds)
                return new EntrancePose { Reveal = 1f, Tilt = cfg.PeekTiltDegrees };
            float enter = Smooth((time - cfg.PeekHoldSeconds) / cfg.EnterSeconds);
            return new EntrancePose { Reveal = 1f + enter, Tilt = cfg.PeekTiltDegrees * (1f - enter) };
        }

        private static float Smooth(float value)
        {
            float t = Math.Max(0f, Math.Min(1f, value));
            return t * t * (3f - 2f * t);
        }
    }
}
