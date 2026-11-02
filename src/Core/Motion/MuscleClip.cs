using System;

namespace DesktopMateOnMaimai.Core.Motion
{
    // Continuous interpolation of the exported 30 Hz samples. A loop overlaps
    // moving poses, then continues at that same incoming time after wrapping.
    internal sealed class MuscleClip
    {
        public string Name;
        public float Length, Rate;
        public int Frames;
        public float[] Values;
        public float LoopOverlap => Math.Min(0.5f, Length * 0.25f);

        private static float Tangent(float a, float b) => a * b <= 0f ? 0f : 2f * a * b / (a + b);

        public void Sample(float time, float[] dst)
        {
            float frame = Math.Max(0f, Math.Min(Frames - 1f, time * Rate));
            int lo = (int)frame, hi = Math.Min(lo + 1, Frames - 1);
            float t = frame - lo, t2 = t * t, t3 = t2 * t;
            for (int i = 0; i < 95; i++)
            {
                float a = Values[lo * 95 + i], b = Values[hi * 95 + i], d = b - a;
                float before = Values[Math.Max(0, lo - 1) * 95 + i];
                float after = Values[Math.Min(Frames - 1, hi + 1) * 95 + i];
                float m0 = lo == 0 ? d : Tangent(a - before, d);
                float m1 = hi == Frames - 1 ? d : Tangent(d, after - b);
                dst[i] = (2f * t3 - 3f * t2 + 1f) * a + (t3 - 2f * t2 + t) * m0 +
                    (-2f * t3 + 3f * t2) * b + (t3 - t2) * m1;
            }
        }

        public float Wrap(float time) => time < Length ? time :
            LoopOverlap + (time - Length) % (Length - LoopOverlap);

        public void SampleLoop(float time, float[] dst, float[] incoming)
        {
            time = Wrap(time);
            Sample(time, dst);
            float start = Length - LoopOverlap;
            if (time <= start) return;
            Sample(time - start, incoming);
            float t = (time - start) / LoopOverlap;
            float blend = t * t * (3f - 2f * t);
            for (int i = 0; i < 95; i++) dst[i] += (incoming[i] - dst[i]) * blend;
        }
    }
}
