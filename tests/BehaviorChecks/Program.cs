using System;
using System.IO;
using DesktopMateOnMaimai;
using DesktopMateOnMaimai.Config;
using DesktopMateOnMaimai.Core;

internal static class Program
{
    private static int _passed;

    private static void Check(bool condition, string scenario)
    {
        if (!condition) throw new Exception("FAIL: " + scenario);
        Console.WriteLine("PASS: " + scenario);
        _passed++;
    }

    private static void Main()
    {
        TouchWatcher.Record(true, false);
        Check(TouchWatcher.Consume("Auto"), "Auto overlay receives first-player touch");
        Check(!TouchWatcher.Consume("1P"), "A touch is consumed once and never queued");
        TouchWatcher.Record(false, true);
        Check(!TouchWatcher.Consume("1P") && !TouchWatcher.Consume("2P"),
            "Other-player touches are discarded rather than replayed");
        TouchWatcher.Record(false, true);
        Check(TouchWatcher.Consume("2P"), "2P overlay reads the second player's touch");
        var residentCfg = new ModConfig { ForceShow = true, LogStateChanges = false };
        var residentCanvas = new OverlayCanvas();
        var residentStage = new CharacterStage();
        var residentPresenter = new CharacterPresenter(residentCfg, residentCanvas, residentStage);
        CabinetState.Reset();
        Check(!residentPresenter.ReactToTouch(), "Hidden companion ignores touch");
        residentPresenter.Tick(0.1f);
        Check(residentPresenter.ReactToTouch(), "Visible resident companion receives touch feedback");
        CabinetState.OnProcessStarted("GameProcess");
        Check(!residentPresenter.ReactToTouch(), "Gameplay rejects touch before the presenter updates visibility");
        CabinetState.Reset();
        float residentTime = residentCanvas.MotionTime;
        for (int i = 0; i < 100; i++) residentPresenter.Tick(0.1f);
        Check(!residentCfg.AnimatedEntrance && residentStage.Phases.Count == 1 &&
            residentStage.Phases[0] == EntrancePhase.Inside && residentCanvas.MotionTime == residentTime,
            "Default character fades in whole and stays at its resident pose without peeking or circling");
        var cfg = new ModConfig { LogStateChanges = false, AnimatedEntrance = true };
        ModMain.Cfg = cfg;
        CabinetState.Reset();
        Check(!CabinetState.ShouldShow(cfg), "Startup stays hidden");

        CabinetState.OnProcessStarted("AdvertiseProcess");
        var canvas = new OverlayCanvas();
        var stage = new CharacterStage();
        var presenter = new CharacterPresenter(cfg, canvas, stage);
        CabinetState.PhaseSeconds = 14.9f;
        presenter.Tick(0.1f);
        Check(!canvas.Visible, "Attract stays hidden before 15 seconds");
        CabinetState.PhaseSeconds = 15.1f;
        presenter.Tick(0.1f);
        Check(canvas.Visible && presenter.IsShowing, "Attract appears after 15 seconds");
        CabinetState.OnProcessStarted("FadeProcess");
        CabinetState.OnProcessStarted("CommonProcess");
        CabinetState.OnProcessReleased("FadeProcess");
        Check(CabinetState.Phase == CabinetPhase.Attract && CabinetState.PhaseSeconds == 15.1f,
            "Fade and common processes cannot replace or reset the real attract state");
        for (int i = 0; i < 10; i++) presenter.Tick(0.1f);
        int attractResets = stage.ResetCount;
        CabinetState.OnProcessStarted("AdvDemoProcess");
        for (int i = 0; i < 30; i++) presenter.Tick(0.1f);
        Check(presenter.IsShowing && canvas.Alpha == 1f && stage.ResetCount == attractResets,
            "Allowed attract pages do not fade out while the new process's show delay starts over");
        CabinetState.OnProcessReleased("AdvDemoProcess");
        presenter.Tick(0.1f);
        CabinetState.OnProcessStarted("AdvertiseProcess");
        for (int i = 0; i < 30; i++) presenter.Tick(0.1f);
        Check(presenter.IsShowing && canvas.Alpha == 1f && stage.ResetCount == attractResets,
            "Brief process gaps preserve the visible companion through the next title page");

        var hiddenPose = EntranceMotion.Sample(0f, cfg);
        var peekPose = EntranceMotion.Sample(cfg.PeekInSeconds + 0.1f, cfg);
        var holdPose = EntranceMotion.Sample(cfg.PeekInSeconds + cfg.PeekHoldSeconds - 0.01f, cfg);
        var insidePose = EntranceMotion.Sample(EntranceMotion.Duration(cfg) + 1f, cfg);
        Check(hiddenPose.Reveal == 0f && peekPose.Reveal == 1f && holdPose.Reveal == 1f &&
            insidePose.Reveal == 2f && insidePose.Tilt == 0f, "Entrance reveals the head, pauses, then enters the circle");
        Check(EntranceMotion.PhaseAt(0.1f, cfg) == EntrancePhase.Peeking &&
            EntranceMotion.PhaseAt(cfg.PeekInSeconds + 0.1f, cfg) == EntrancePhase.Calling &&
            EntranceMotion.PhaseAt(cfg.PeekInSeconds + cfg.PeekHoldSeconds + 0.1f, cfg) == EntrancePhase.Entering &&
            EntranceMotion.PhaseAt(EntranceMotion.Duration(cfg) + 1f, cfg) == EntrancePhase.Inside,
            "Entrance performance advances through peek, call, enter and permanent inside phases");

        CabinetState.OnProcessStarted("AdvertiseCommercialProcess");
        Check(!CabinetState.ShouldShow(cfg), "Commercial toggle works with attract enabled");
        cfg.ShowInCommercial = true;
        Check(CabinetState.ShouldShow(cfg), "Commercial can be enabled independently");

        CabinetState.OnProcessStarted("EntryProcess");
        Check(!CabinetState.ShouldShow(cfg), "Unknown entry screen stays hidden");
        CabinetState.OnEntryScreen("ConfirmEntry");
        Check(!CabinetState.ShouldShow(cfg), "Waiting for a card never permits the lower companion");
        cfg.ShowInEntryWaiting = true;
        cfg.ForceShow = true;
        foreach (string screen in new[] { "", "ConfirmEntry", "ConfirmFelica", "ConfirmAccessCode",
            "DisplayAccessCodeQR", "WaitPartner", "ConfirmGuest", "DisplayPleaseWait", "DoneEntry" })
        {
            CabinetState.OnEntryScreen(screen);
            Check(!CabinetState.ShouldShow(cfg) && CabinetState.EntryMustHide,
                "Entry screen stays hidden despite legacy config and ForceShow: " + screen);
        }
        cfg.ForceShow = false;
        CabinetState.OnProcessStarted("AdvertiseProcess");
        CabinetState.PhaseSeconds = 16f;
        for (int i = 0; i < 70; i++) presenter.Tick(0.1f);
        float entryMotion = canvas.MotionTime;
        int entryResets = stage.ResetCount;
        CabinetState.OnProcessStarted("EntryProcess");
        presenter.Tick(0.01f);
        Check(!presenter.IsShowing && canvas.Visible && canvas.Alpha > 0f && canvas.Alpha < 1f &&
            canvas.MotionTime == entryMotion && stage.ResetCount == entryResets,
            "Entry immediately starts fading in place before any card screen is reported");
        float entryAlpha = canvas.Alpha;
        CabinetState.OnEntryScreen("ConfirmEntry");
        presenter.Tick(0.01f);
        Check(canvas.Alpha < entryAlpha, "Opening the card prompt continues the fade without flashing back");
        CabinetState.OnEntryScreen("DisplayPleaseWait");
        presenter.Tick(0.1f);
        Check(!presenter.IsShowing && canvas.Alpha < 1f,
            "PleaseWait starts fading without the two-second buffer");
        for (int i = 0; i < 200; i++) presenter.Tick(0.1f);
        Check(!canvas.Visible && canvas.Alpha == 0f && stage.ResetCount == entryResets,
            "Remaining on the card screen never restarts the lower companion");
        CabinetState.OnEntryScreen("");
        Check(!CabinetState.ShouldShow(cfg), "Closing PleaseWait cannot reopen display");
        CabinetState.OnEntryScreen("DoneEntry");
        CabinetState.OnEntryScreen("DisplayPleaseWait");
        CabinetState.OnEntryScreen("ConfirmEntry");
        Check(!CabinetState.ShouldShow(cfg), "Completed login remains hidden through screen changes");

        cfg.ForceShow = true;
        CabinetState.Reset();
        presenter.Tick(0.6f);
        Check(canvas.Visible && presenter.IsShowing, "ForceShow works without process detection");
        foreach (string process in new[] { "GameProcess", "ResultProcess" })
        {
            CabinetState.Reset();
            for (int i = 0; i < 10; i++) presenter.Tick(0.1f);
            CabinetState.OnProcessStarted(process);
            presenter.Tick(0.01f);
            Check(canvas.Visible && canvas.Alpha > 0f && canvas.Alpha < 1f && !presenter.IsShowing &&
                !CabinetState.ShouldShow(cfg), process + " starts fading immediately, including ForceShow");
            for (int i = 0; i < 5; i++) presenter.Tick(0.1f);
            int stageTicks = stage.Ticks;
            int brainTicks = IdleBrain.Ticks;
            presenter.Tick(0.1f);
            Check(!canvas.Visible && canvas.Alpha == 0f && stageTicks == stage.Ticks && brainTicks == IdleBrain.Ticks,
                process + " stops animation once the fade completes");
        }

        cfg.ForceShow = false;
        CabinetState.OnProcessStarted("AdvertiseProcess");
        CabinetState.PhaseSeconds = 16f;
        presenter.Tick(0.6f);
        CabinetState.OnProcessStarted("MusicSelectProcess");
        presenter.Tick(1f);
        Check(presenter.IsShowing, "Ordinary transition retains the hide buffer");
        presenter.Tick(1.1f);
        for (int i = 0; i < 5; i++) presenter.Tick(0.1f);
        Check(!presenter.IsShowing && !canvas.Visible, "Ordinary transition eventually hides");

        CabinetState.OnProcessStarted("EntryProcess");
        CabinetState.OnEntryScreen("ConfirmEntry");
        Check(!CabinetState.UserLoggedIn && !CabinetState.ShouldShow(cfg),
            "New entry session resets completed login while keeping the card screen clear");

        CabinetState.OnProcessStarted("AdvertiseProcess");
        CabinetState.PhaseSeconds = 30f;
        stage.Ready = false;
        presenter.Tick(10f);
        Check(!canvas.Visible && !presenter.IsShowing, "Failed character load cannot display an empty white surface");
        stage.Ready = true;

        var oneWayCfg = new ModConfig { LogStateChanges = false, ForceShow = true, AnimatedEntrance = true,
            HideDelaySeconds = 0f, FadeInSeconds = 0.01f, FadeOutSeconds = 0.5f };
        CabinetState.Reset();
        var oneWayCanvas = new OverlayCanvas();
        var oneWayStage = new CharacterStage();
        var oneWayPresenter = new CharacterPresenter(oneWayCfg, oneWayCanvas, oneWayStage);
        int idleTicksBeforeEntrance = IdleBrain.Ticks;
        oneWayPresenter.Tick(0.1f);
        Check(IdleBrain.Ticks == idleTicksBeforeEntrance,
            "Random idle gestures cannot interrupt the choreographed entrance");
        for (int i = 1; i < 70; i++) oneWayPresenter.Tick(0.1f);
        float enteredMotion = oneWayCanvas.MotionTime;
        Check(oneWayStage.Phases.FindAll(p => p == EntrancePhase.Calling).Count == 1 &&
            oneWayStage.Phases.FindAll(p => p == EntrancePhase.Entering).Count == 1,
            "Call and enter gestures each fire exactly once per entrance");
        Check(oneWayStage.Phases.FindAll(p => p == EntrancePhase.Inside).Count == 1 &&
            IdleBrain.Ticks > idleTicksBeforeEntrance,
            "The exactly clamped entrance endpoint reaches Inside and enables resident idle");
        oneWayCfg.ForceShow = false;
        oneWayPresenter.Tick(0.1f);
        Check(!oneWayPresenter.IsShowing && oneWayCanvas.Alpha < 1f &&
            Math.Abs(oneWayCanvas.MotionTime - enteredMotion) < 0.0001f,
            "Hiding fades in place without rewinding the entered character");
        oneWayCfg.ForceShow = true;
        oneWayPresenter.Tick(0.1f);
        Check(oneWayPresenter.IsShowing && oneWayStage.ResetCount == 1 &&
            Math.Abs(oneWayCanvas.MotionTime - enteredMotion) < 0.0001f,
            "Brief visibility changes resume inside instead of shuttling through the rim");
        oneWayCfg.ForceShow = false;
        oneWayPresenter.Tick(4f);
        Check(oneWayCanvas.Alpha > 0f && oneWayCanvas.Alpha < 1f && oneWayCanvas.Visible,
            "A stalled frame cannot skip the entire fade-out");
        float interruptedAlpha = oneWayCanvas.Alpha;
        oneWayCfg.ForceShow = true;
        oneWayCfg.FadeInSeconds = 0.6f;
        oneWayPresenter.Tick(0.05f);
        Check(oneWayCanvas.Alpha > interruptedAlpha && oneWayCanvas.Alpha < 1f && oneWayStage.ResetCount == 1,
            "Interrupted fades reverse from the current opacity without resetting the pose");
        for (int i = 0; i < 10; i++) oneWayPresenter.Tick(0.1f);
        oneWayCfg.ForceShow = false;
        for (int i = 0; i < 8; i++) oneWayPresenter.Tick(0.1f);
        oneWayCfg.ForceShow = true;
        oneWayPresenter.Tick(0.1f);
        Check(oneWayStage.ResetCount == 1 && oneWayCanvas.MotionTime >= enteredMotion,
            "Even a fully hidden attract transition resumes inside without replaying the entrance");

        oneWayCfg.ForceShow = false;
        CabinetState.OnProcessStarted("AdvertiseCommercialProcess");
        for (int i = 0; i < 30; i++) oneWayPresenter.Tick(0.1f);
        CabinetState.OnProcessStarted("AdvertiseProcess");
        CabinetState.PhaseSeconds = 30f;
        oneWayPresenter.Tick(0.1f);
        Check(oneWayStage.ResetCount == 1 && oneWayCanvas.MotionTime >= enteredMotion,
            "Commercial-to-title loops preserve the resident character after a complete fade");
        CabinetState.OnProcessStarted("GameProcess");
        for (int i = 0; i < 8; i++) oneWayPresenter.Tick(0.1f);
        CabinetState.OnProcessStarted("AdvertiseProcess");
        CabinetState.PhaseSeconds = 30f;
        oneWayPresenter.Tick(0.1f);
        Check(oneWayStage.ResetCount == 2 && oneWayCanvas.MotionTime < enteredMotion,
            "A new player session allows exactly one new entrance after gameplay");

        CabinetState.Reset();
        var seatCfg = new ModConfig { LogStateChanges = false, ShowDelaySeconds = 0f };
        var seatCanvas = new OverlayCanvas();
        var seatStage = new CharacterStage();
        var seatPresenter = new CharacterPresenter(seatCfg, seatCanvas, seatStage);
        CabinetState.OnProcessStarted("AdvertiseProcess");
        for (int i = 0; i < 10; i++) seatPresenter.Tick(0.1f);
        seatPresenter.ChooseUpperSeat(true);
        Check(!seatPresenter.SeatRequested && !seatCanvas.UpperSeat && seatCanvas.Visible,
            "Logged-out character stays in lower circle and cannot select upper seat");
        CabinetState.OnProcessStarted("EntryProcess");
        CabinetState.OnEntryScreen("DoneEntry");
        CabinetState.OnProcessStarted("MusicSelectProcess");
        Check(CabinetState.UserLoggedIn && seatPresenter.CanChooseSeat && !seatPresenter.SeatRequested,
            "Login survives entry-to-selection transition and offers an opt-in seat");
        seatPresenter.ChooseUpperSeat(true);
        for (int i = 0; i < 20; i++) seatPresenter.Tick(0.1f);
        Check(seatCanvas.UpperSeat && seatStage.Seated && seatPresenter.IsShowing && seatCanvas.AlphaAtMove <= 0.002f,
            "An immediate login choice survives initialization and relocates only while transparent");
        CabinetState.OnProcessStarted("GameProcess");
        seatPresenter.Tick(0.01f);
        Check(seatCanvas.Visible && seatCanvas.Alpha > 0f && seatCanvas.Alpha < 1f && !seatPresenter.IsShowing,
            "Gameplay fades the upper companion instead of abruptly removing it");
        for (int i = 0; i < 5; i++) seatPresenter.Tick(0.1f);
        Check(!seatCanvas.Visible && !seatPresenter.CanChooseSeat && seatPresenter.SeatRequested,
            "Gameplay hides seated character after fading and preserves the session choice");
        CabinetState.OnProcessStarted("MusicSelectProcess");
        for (int i = 0; i < 20; i++) seatPresenter.Tick(0.1f);
        Check(seatPresenter.IsShowing && seatStage.Seated, "Returning to selection resumes the selected seat");
        seatPresenter.ChooseUpperSeat(false);
        seatPresenter.ChooseUpperSeat(true);
        seatPresenter.Tick(0.1f);
        Check(seatPresenter.IsShowing && seatCanvas.UpperSeat, "Rapid seat choices retarget without restarting a move");
        seatPresenter.ChooseUpperSeat(false);
        for (int i = 0; i < 20; i++) seatPresenter.Tick(0.1f);
        Check(!seatPresenter.IsShowing && !seatCanvas.Visible, "Cancelling the seat hides the logged-in companion");
        seatStage.SupportsSeat = false;
        Check(!seatPresenter.CanChooseSeat, "Assets without a real sitting pose do not offer a fake seat");
        seatStage.SupportsSeat = true;
        seatCanvas.HasUpperScreen = false;
        Check(!seatPresenter.CanChooseSeat, "Landscape output without an upper monitor does not offer a seat");
        seatCanvas.HasUpperScreen = true;
        CabinetState.OnProcessStarted("AdvertiseProcess");
        for (int i = 0; i < 20; i++) seatPresenter.Tick(0.1f);
        Check(!CabinetState.UserLoggedIn && !seatCanvas.UpperSeat && !seatStage.Seated && seatPresenter.IsShowing,
            "A new attract session returns to the lower circle in standing pose");

        string directory = Path.Combine(Path.GetTempPath(), "DesktopMateChecks-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var loaded = ModConfig.Load(directory);
            var reloaded = ModConfig.Load(directory);
            Check(loaded.Renderer == "3D" && reloaded.Renderer == "3D" &&
                loaded.CharacterId == "miku-local" && reloaded.CharacterId == "miku-local" &&
                reloaded.YawDegrees == 180f && !reloaded.ShowInEntryWaiting &&
                Math.Abs(loaded.CircleRestRadius - 0.76f) < 0.0001f &&
                Math.Abs(reloaded.CircleHeightRadius - 1.65f) < 0.0001f &&
                reloaded.CircleResidentX == 0.46f && reloaded.CircleResidentY == -1.12f &&
                reloaded.RenderTextureWidth == 1620 && reloaded.RenderTextureHeight == 1620 &&
                reloaded.RenderTextureAntiAliasing == 4 && !reloaded.AnimatedEntrance &&
                reloaded.HotkeySeat == "F8" && reloaded.AllowUpperSeat && !reloaded.UpperSeatOnLogin &&
                reloaded.EnableActionCombos && !reloaded.ReducedMotion &&
                reloaded.StandingIdleMotion == 0f && !reloaded.EnableLookAt && reloaded.GazeRadius == 0f &&
                reloaded.TouchEnabled && reloaded.TouchCooldownSeconds == 8f &&
                reloaded.UpperSeatX == 0.78f && reloaded.UpperSeatHeight == 0.52f,
                "Generated config reloads large companion placement and touch defaults");
            File.WriteAllText(Path.Combine(directory, ModConfig.FileName), "[Interaction]\nTouchEnabled = false\nTouchCooldownSeconds = -20\n");
            loaded = ModConfig.Load(directory);
            Check(!loaded.TouchEnabled && loaded.TouchCooldownSeconds == 2f, "Touch configuration parses enable flag and clamps cooldown");
            File.WriteAllText(Path.Combine(directory, ModConfig.FileName), "[Display]\nCircleResidentY = -1.4\n");
            Check(ModConfig.Load(directory).CircleResidentY == -1.4f,
                "Standing placement can put the feet below the circular mask");
            File.WriteAllText(Path.Combine(directory, ModConfig.FileName), "[Display]\nCircleResidentY = -20\n");
            Check(ModConfig.Load(directory).CircleResidentY == -1.8f,
                "Below-circle standing placement remains bounded");
        }
        finally
        {
            File.Delete(Path.Combine(directory, ModConfig.FileName));
            Directory.Delete(directory);
        }
        Console.WriteLine(_passed + " behavior checks passed (headless; Unity rendering is not tested).");
    }
}
