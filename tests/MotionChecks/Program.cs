using System;
using System.Linq;
using DesktopMateOnMaimai.Config;
using DesktopMateOnMaimai.Core;
using DesktopMateOnMaimai.Core.Motion;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        _passed++;
    }

    private static void Main()
    {
        var cfg = new ModConfig { IdleVariants = new[] { "Idle" }, IdleWeights = new[] { 1f },
            IdleMinSeconds = 0.8f, IdleMaxSeconds = 0.8f, TimeJitter = 0f };
        var driver = new RecordingDriver();
        var brain = new IdleBrain(cfg, driver);
        brain.Reset();
        for (int i = 0; i < 6000; i++) brain.Tick(0.1f);
        Check(driver.Triggers.Count == 0 && brain.CurrentMood == IdleBrain.Mood.Idle,
            "Idle-only remains idle through repeated scheduling for ten minutes");
        Check(driver.GazeUpdates == 6000, "Idle-only retains gaze updates");
        brain.Reset();
        for (int i = 0; i < 100; i++) brain.Tick(0.1f);
        Check(driver.Triggers.Count == 0, "Redisplay does not insert a Shift into Idle-only");

        cfg.IdleVariants = new[] { "Wave" };
        brain.Reset();
        for (int i = 0; i < 600; i++) brain.Tick(0.1f);
        Check(driver.Triggers.Count > 5 && driver.Triggers.All(t => t == "Wave"),
            "A single explicit gesture repeats without unconfigured fallback gestures");

        float boundary = cfg.PeekInSeconds + cfg.PeekHoldSeconds + cfg.EnterSeconds;
        foreach (float duration in new[] { 0f, 0.1f, 0.35f, 2f })
        {
            cfg.EntranceSettleSeconds = duration;
            var before = EntranceMotion.Sample(boundary - 0.00001f, cfg);
            var after = EntranceMotion.Sample(boundary + 0.00001f, cfg);
            Check(Math.Abs(after.Dip - before.Dip) < 0.001f,
                "No position jump at settle entry, duration=" + duration);
            var end = EntranceMotion.Sample(EntranceMotion.Duration(cfg) + 0.001f, cfg);
            Check(end.Dip == 0f && end.Tilt == 0f && end.Reveal == 2f,
                "Settle ends at rest, duration=" + duration);
        }
        cfg.EntranceSettleSeconds = 0.35f;
        float maximum = 0f, minimum = 0f, largestStep = 0f, previous = 0f;
        for (int frame = 0; frame <= 22; frame++)
        {
            float dip = EntranceMotion.Sample(boundary + frame / 60f, cfg).Dip;
            maximum = Math.Max(maximum, dip);
            minimum = Math.Min(minimum, dip);
            largestStep = Math.Max(largestStep, Math.Abs(dip - previous));
            previous = dip;
        }
        Check(maximum > 5f && maximum < 8f && minimum < 0f && largestStep < 3f,
            "60fps settle sinks, rebounds and stays within a 3px per-frame step");
        Console.WriteLine($"Settle: max={maximum:F3}px min={minimum:F3}px step={largestStep:F3}px");

        foreach (string name in ClipLibrary.Names())
        {
            var motion = new MotionRig(3.9f, 1f, 0.18f, true, true);
            Check(motion.Play(name), name + " starts");
            bool finite = true;
            for (int i = 0; i < 300; i++)
            {
                motion.Tick(1f / 60f);
                finite &= motion.Output.V.All(float.IsFinite);
            }
            Check(finite && !motion.Busy, name + " completes with finite pose channels");
        }
        Console.WriteLine(_passed + " motion checks passed (headless; bone binding and rendering require the game).");
    }
}
