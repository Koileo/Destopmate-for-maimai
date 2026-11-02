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
        var muscleClip = new MuscleClip { Length = 2f, Rate = 4f, Frames = 9, Values = new float[9 * 95] };
        for (int frame = 0; frame < 9; frame++)
            for (int channel = 0; channel < 95; channel++)
                muscleClip.Values[frame * 95 + channel] = channel == 1 ? 0.4f : (float)Math.Sin(frame * 0.3f);
        var sampled = new float[95];
        var incoming = new float[95];
        bool bounded = true, exactKeys = true;
        for (int frame = 0; frame < 8; frame++)
        {
            muscleClip.Sample(frame / 4f, sampled);
            exactKeys &= Math.Abs(sampled[0] - muscleClip.Values[frame * 95]) < 0.00001f;
            for (int step = 0; step <= 100; step++)
            {
                muscleClip.Sample((frame + step / 100f) / 4f, sampled);
                float a = muscleClip.Values[frame * 95], b = muscleClip.Values[(frame + 1) * 95];
                bounded &= sampled[0] >= Math.Min(a, b) - 0.00001f && sampled[0] <= Math.Max(a, b) + 0.00001f;
            }
        }
        Check(exactKeys && bounded, "Continuous muscle sampling preserves keys without joint overshoot");
        Func<float, float> loopSample = time => { muscleClip.SampleLoop(time, sampled, incoming); return sampled[0]; };
        float seam = muscleClip.Length, epsilon = 0.001f;
        float atSeam = loopSample(seam);
        float beforeVelocity = (atSeam - loopSample(seam - epsilon)) / epsilon;
        float afterVelocity = (loopSample(seam + epsilon) - atSeam) / epsilon;
        Check(Math.Abs(beforeVelocity - afterVelocity) < 0.02f &&
            Math.Abs(atSeam - loopSample(muscleClip.LoopOverlap)) < 0.00001f,
            "Loop seam continues the incoming pose and velocity instead of restarting at frame zero");
        muscleClip.SampleLoop(4000f, sampled, incoming);
        Check(Math.Abs(sampled[1] - 0.4f) < 0.00001f && sampled.All(float.IsFinite),
            "Long-running muscle loops keep constant channels stable and every channel finite");

        var layoutCfg = new ModConfig();
        foreach (float aspect in new[] { 0.5f, 0.7f, 1f, 1.5f })
        {
            foreach (float requested in new[] { 0.55f, 0.9f, 1.3f, 1.8f })
            {
                float h = CircleSafeLayout.FitHeight(requested, aspect);
                CircleSafeLayout.RestFoot(layoutCfg, h, aspect, out float x, out float y);
                x = CircleSafeLayout.FitRestX(x, y, h, aspect);
                float left = x - h * aspect * 0.5f, right = x + h * aspect * 0.5f;
                float edgeY = Math.Max(Math.Abs(y), Math.Abs(y + h));
                Check(h > 0f && left >= CircleSafeLayout.Left && y >= CircleSafeLayout.Bottom &&
                    right * right + edgeY * edgeY < 1f,
                    "Legacy animated entrance respects its narrow safe lane, aspect=" + aspect + ", height=" + requested);
            }
        }
        var touchCfg = new ModConfig { IdleVariants = new[] { "Idle" }, IdleWeights = new[] { 1f },
            TouchCooldownSeconds = 8f };
        var touchDriver = new RecordingDriver();
        var touchBrain = new IdleBrain(touchCfg, touchDriver);
        touchBrain.Reset();
        Check(touchBrain.ReactToTouch() && touchDriver.Triggers.SequenceEqual(new[] { "Nod" }),
            "Touch immediately acknowledges with an available short gesture");
        bool spamSuppressed = true;
        bool touchSmileReleased = false;
        for (int i = 0; i < 79; i++)
        {
            touchBrain.Tick(0.1f);
            if (i == 14) touchSmileReleased = touchDriver.LastFace == "Default";
            spamSuppressed &= !touchBrain.ReactToTouch();
        }
        Check(spamSuppressed, "Touch spam stays suppressed throughout cooldown");
        touchBrain.Tick(0.1f);
        touchBrain.Tick(0.1f);
        Check(touchSmileReleased && touchBrain.ReactToTouch(),
            "Touch smile returns to normal and reaction becomes available after cooldown");
        touchBrain.Reset();
        Check(!touchBrain.ReactToTouch(), "Redisplay cannot bypass the touch cooldown");
        for (int i = 0; i < 90; i++) touchBrain.Tick(0.1f);
        touchDriver.IsBusy = true;
        Check(!touchBrain.ReactToTouch(), "Touch cannot interrupt an active clip");
        touchDriver.IsBusy = false;
        touchDriver.Allowed = new[] { "Shift", "Think" };
        Check(touchBrain.ReactToTouch() && touchDriver.Triggers.Last() == "Shift",
            "Native-motion characters use a supported Shift rather than an absent Nod or Wave");
        var seatedDriver = new RecordingDriver();
        var seatedBrain = new IdleBrain(touchCfg, seatedDriver);
        seatedBrain.SetSeated(true);
        Check(seatedBrain.ReactToTouch() && seatedDriver.Triggers.Count == 0 && seatedDriver.LastFace == "Smile",
            "Seated touch smiles without starting standing gestures");
        for (int i = 0; i < 20; i++) seatedBrain.Tick(0.1f);
        Check(seatedDriver.LastFace == "Default", "Seated smile ends without waiting for another gesture");
        touchCfg.ReducedMotion = true;
        var reducedDriver = new RecordingDriver();
        var reducedBrain = new IdleBrain(touchCfg, reducedDriver);
        Check(reducedBrain.ReactToTouch() && reducedDriver.Triggers.Count == 0 && reducedDriver.LastFace == "Smile",
            "Reduced motion acknowledges touch with only a facial expression");
        touchCfg.TouchEnabled = false;
        var disabledBrain = new IdleBrain(touchCfg, new RecordingDriver());
        Check(!disabledBrain.ReactToTouch(), "Touch interaction can be disabled independently");
        var cfg = new ModConfig { IdleVariants = new[] { "Idle" }, IdleWeights = new[] { 1f },
            IdleMinSeconds = 0.8f, IdleMaxSeconds = 0.8f, TimeJitter = 0f };
        var driver = new RecordingDriver();
        var brain = new IdleBrain(cfg, driver);
        brain.Reset();
        for (int i = 0; i < 6000; i++) brain.Tick(0.1f);
        Check(driver.Triggers.Count == 0 && brain.CurrentMood == IdleBrain.Mood.Idle,
            "Idle-only remains idle through repeated scheduling for ten minutes");
        Check(driver.GazeUpdates == 6000 && driver.GazeStayedForward, "Idle-only maintains a fixed forward gaze for ten minutes");
        brain.Reset();
        for (int i = 0; i < 100; i++) brain.Tick(0.1f);
        Check(driver.Triggers.Count == 0, "Redisplay does not insert a Shift into Idle-only");

        cfg.IdleVariants = new[] { "Wave" };
        brain.Reset();
        for (int i = 0; i < 600; i++) brain.Tick(0.1f);
        Check(driver.Triggers.Count > 5 && driver.Triggers.All(t => t == "Wave"),
            "A single explicit gesture repeats without unconfigured fallback gestures");

        float boundary = cfg.PeekInSeconds + cfg.PeekHoldSeconds + cfg.EnterSeconds;
        var comboCfg = new ModConfig { IdleVariants = new[] { "Shift", "Think" }, IdleWeights = new[] { 1f, 1f },
            IdleMinSeconds = 1f, IdleMaxSeconds = 1f };
        var comboDriver = new RecordingDriver { Allowed = new[] { "Shift", "Think" } };
        var comboBrain = new IdleBrain(comboCfg, comboDriver);
        comboBrain.Reset();
        comboDriver.IsBusy = true;
        for (int i = 0; i < 100; i++) comboBrain.Tick(0.1f);
        Check(comboDriver.Triggers.Count == 0, "Combos wait for the current clip to finish");
        comboDriver.IsBusy = false;
        for (int i = 0; i < 1200; i++) comboBrain.Tick(0.1f);
        Check(comboDriver.Triggers.Count > 4 && comboDriver.Triggers.Select((name, i) => name == (i % 2 == 0 ? "Think" : "Shift")).All(v => v),
            "Supported configured gestures form complete Think-to-Shift phrases");
        int beforeSeated = comboDriver.Triggers.Count;
        comboBrain.SetSeated(true);
        for (int i = 0; i < 1000; i++) comboBrain.Tick(0.1f);
        Check(comboDriver.Triggers.Count == beforeSeated, "Seated idle never starts standing gestures");
        comboBrain.Reset();
        comboCfg.ReducedMotion = true;
        for (int i = 0; i < 1000; i++) comboBrain.Tick(0.1f);
        Check(comboDriver.Triggers.Count == beforeSeated, "Reduced motion keeps the companion free of extra gesture phrases");
        var availableCfg = new ModConfig { IdleVariants = new[] { "Wave", "Nod", "Think" },
            IdleWeights = new[] { 1000000f, 1000000f, 1f }, IdleMinSeconds = 1f, IdleMaxSeconds = 1f };
        var availableDriver = new RecordingDriver { Allowed = new[] { "Think" } };
        var availableBrain = new IdleBrain(availableCfg, availableDriver);
        for (int i = 0; i < 500; i++) availableBrain.Tick(0.1f);
        Check(availableDriver.Triggers.Count > 5 && availableDriver.Triggers.All(t => t == "Think"),
            "Unsupported high-weight gestures cannot starve the character's available expressive action");
        var facialDriver = new RecordingDriver();
        var facialBrain = new IdleBrain(new ModConfig { ReducedMotion = true }, facialDriver);
        facialBrain.SetSeated(true);
        for (int i = 0; i < 500; i++) facialBrain.Tick(0.1f);
        Check(facialDriver.Faces.Count(f => f == "Smile") >= 3 && facialDriver.Faces.Count(f => f == "Default") >= 3 &&
            facialDriver.Triggers.Count == 0,
            "Seated reduced-motion idle smiles and relaxes repeatedly without body gestures or touch");
        var legacyGazeCfg = new ModConfig { EnableLookAt = true, GazeRadius = 10f,
            IdleMinSeconds = 0.1f, IdleMaxSeconds = 0.1f };
        var fixedGazeDriver = new RecordingDriver();
        var fixedGazeBrain = new IdleBrain(legacyGazeCfg, fixedGazeDriver);
        for (int i = 0; i < 6000; i++)
        {
            if (i % 100 == 0) fixedGazeBrain.ReactToTouch();
            if (i % 500 == 0) fixedGazeBrain.Reset();
            if (i == 3000) fixedGazeBrain.SetSeated(true);
            fixedGazeBrain.Tick(0.1f);
        }
        Check(fixedGazeDriver.GazeStayedForward && fixedGazeDriver.GazeUpdates == 6000,
            "Legacy random-look settings, gestures, touch and seat changes cannot move the gaze off center");
        var quietMotion = new MotionRig(3.9f, 1f, 0f, true, true, 0f);
        var stillPose = new Pose();
        quietMotion.Tick(0.1f);
        stillPose.CopyFrom(quietMotion.Output);
        bool stableSkeleton = true, quietBlink = false;
        for (int i = 0; i < 6000; i++)
        {
            quietMotion.Tick(0.1f);
            for (int channel = 0; channel <= Pose.AnkleR; channel++)
                stableSkeleton &= quietMotion.Output[channel] == stillPose[channel];
            quietBlink |= quietMotion.Output[Pose.Blink] > 0.5f;
        }
        quietMotion.PlayFace("Smile");
        for (int i = 0; i < 10; i++) quietMotion.Tick(0.1f);
        Check(stableSkeleton && quietBlink && quietMotion.Output[Pose.Smile] > 0.9f,
            "Zero idle amplitude stops procedural breath, weight shift and noise for ten minutes while face animation remains active");
        quietMotion.Play("Wave");
        bool quietGesture = false;
        for (int i = 0; i < 40; i++)
        {
            quietMotion.Tick(0.1f);
            quietGesture |= Math.Abs(quietMotion.Output[Pose.ArmDownR] - stillPose[Pose.ArmDownR]) > 20f;
        }
        for (int i = 0; i < 100; i++) quietMotion.Tick(0.1f);
        Check(quietGesture && !quietMotion.Busy && Math.Abs(quietMotion.Output[Pose.HipsShiftX]) < 0.000001f,
            "Quiet standing preserves deliberate gestures and settles back after the gesture finishes");
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

        float fittedHeight = CircleSafeLayout.FitHeight(0.90f, 0.7f);
        var peekPath = CircleEntrancePath.Sample(EntranceMotion.Sample(cfg.PeekInSeconds, cfg), cfg, fittedHeight);
        var restPath = CircleEntrancePath.Sample(EntranceMotion.Sample(EntranceMotion.Duration(cfg) + 10f, cfg), cfg, fittedHeight);
        float restFootY = restPath.HeadY - fittedHeight * (1f - cfg.HeadFraction * 0.5f);
        Check(peekPath.Radius > 1f && restPath.Radius < 1f,
            "Circular path starts outside the rim and finishes permanently inside it");
        Check(restPath.HeadX > 0.55f && restFootY > -0.45f,
            "Default rest pose uses the right safe lane and stays above the bottom prompt band");
        bool inward = true, smooth = true;
        var previousPath = peekPath;
        for (int frame = 1; frame <= 120; frame++)
        {
            float t = cfg.PeekInSeconds + cfg.PeekHoldSeconds + cfg.EnterSeconds * frame / 120f;
            var path = CircleEntrancePath.Sample(EntranceMotion.Sample(t, cfg), cfg, fittedHeight);
            inward &= path.Radius <= previousPath.Radius + 0.0001f;
            float step = (float)Math.Sqrt(Math.Pow(path.HeadX - previousPath.HeadX, 2) +
                Math.Pow(path.HeadY - previousPath.HeadY, 2));
            smooth &= step < 0.04f;
            previousPath = path;
        }
        Check(inward && smooth, "Circular entrance moves inward monotonically without frame jumps");
        Check(!new ModConfig().IdleVariants.Contains("Peek"), "Inside idle schedule does not repeat the entrance peek");
        Check(EntranceMotion.Sample(cfg.PeekInSeconds + 0.5f, cfg).BodyReveal >= 0.4f,
            "Calling exposes the supporting and waving arms before the body enters");

        foreach (float aspect in new[] { 0.5f, 0.7f, 900f / 1080f, 1f, 1.5f })
        {
            float h = CircleSafeLayout.FitHeight(1.15f, aspect);
            bool safe = true, inside = true;
            double minX = 9, minY = 9, maxRadius = 0;
            for (int frame = 0; frame <= 600; frame++)
            {
                float t = EntranceMotion.Duration(cfg) * frame / 600f;
                var pose = EntranceMotion.Sample(t, cfg);
                var path = CircleEntrancePath.Sample(pose, cfg, h, aspect);
                double angle = pose.Tilt * Math.PI / 180;
                for (int corner = 0; corner < 4; corner++)
                {
                    float localX = (corner % 2 == 0 ? -0.5f : 0.5f) * h * aspect;
                    float visibleBottom = h * (1f - cfg.HeadFraction) * (1f - pose.BodyReveal);
                    float localY = (corner < 2 ? visibleBottom : h) - h * (1f - cfg.HeadFraction * 0.5f);
                    double x = path.HeadX + localX * Math.Cos(angle) - localY * Math.Sin(angle);
                    // 160px radius is deliberately smaller than the normal 450+px game circle.
                    double y = path.HeadY + localX * Math.Sin(angle) + localY * Math.Cos(angle) - pose.Dip / 160f;
                    safe &= x >= CircleSafeLayout.Left - 0.0001 && y >= CircleSafeLayout.Bottom - 0.0001;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    if (frame == 600) { inside &= x * x + y * y <= 1f; maxRadius = Math.Max(maxRadius, Math.Sqrt(x*x+y*y)); }
                }
            }
            Check(safe && inside, "Visible animated quad avoids information bands and rests within circle, aspect=" + aspect +
                $" (minX={minX:F4}, minY={minY:F4}, restRadius={maxRadius:F4})");
        }

        foreach (float speed in new[] { 0.2f, 1f, 2.5f })
        {
            var motion = new MotionRig(3.9f, 1f, 0f, false, true);
            motion.SetSpeed(speed);
            for (int i = 0; i < 10; i++)
            {
                motion.SampleEntrance("Call", 1.08f / 2.35f);
                motion.Tick(0.1f);
            }
            Check(motion.CurrentGesture == "Call" && motion.Output[Pose.ElbowR] > 40f,
                "Entrance pose remains synchronized to path at motion speed=" + speed);
            motion.SampleEntrance(null, 0f);
            motion.Tick(0.1f);
            motion.Tick(0.1f);
            Check(!motion.Busy && Math.Abs(motion.Output[Pose.ElbowR] - 9f) < 2f,
                "Entrance releases smoothly into resident breathing at motion speed=" + speed);
        }

        var interrupted = new MotionRig(3.9f, 1f, 0f, false, true);
        interrupted.SampleEntrance("Call", 1.08f / 2.35f);
        for (int i = 0; i < 30; i++) interrupted.Tick(1f / 60f);
        float raisedArm = interrupted.Output[Pose.ArmDownR];
        interrupted.SampleEntrance("Enter", 0f);
        interrupted.Tick(1f / 60f);
        Check(Math.Abs(interrupted.Output[Pose.ArmDownR] - raisedArm) < 4f,
            "Switching entrance stages blends the raised arm instead of snapping to neutral");

        var slowFrames = new MotionRig(3.9f, 1f, 0f, false, true);
        slowFrames.Play("Enter");
        for (int i = 0; i < 16; i++) slowFrames.Tick(0.1f);
        Check(!slowFrames.Busy, "10fps animation consumes the full shared delta and completes a 1.55s step on time");

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
