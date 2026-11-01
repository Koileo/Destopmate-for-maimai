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
        var cfg = new ModConfig { LogStateChanges = false };
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

        var hiddenPose = EntranceMotion.Sample(0f, cfg);
        var peekPose = EntranceMotion.Sample(cfg.PeekInSeconds + 0.1f, cfg);
        var holdPose = EntranceMotion.Sample(cfg.PeekInSeconds + cfg.PeekHoldSeconds - 0.01f, cfg);
        var insidePose = EntranceMotion.Sample(EntranceMotion.Duration(cfg) + 1f, cfg);
        Check(hiddenPose.Reveal == 0f && peekPose.Reveal == 1f && holdPose.Reveal == 1f &&
            insidePose.Reveal == 2f && insidePose.Tilt == 0f, "Entrance reveals the head, pauses, then enters the circle");

        CabinetState.OnProcessStarted("AdvertiseCommercialProcess");
        Check(!CabinetState.ShouldShow(cfg), "Commercial toggle works with attract enabled");
        cfg.ShowInCommercial = true;
        Check(CabinetState.ShouldShow(cfg), "Commercial can be enabled independently");

        CabinetState.OnProcessStarted("EntryProcess");
        Check(!CabinetState.ShouldShow(cfg), "Unknown entry screen stays hidden");
        CabinetState.OnEntryScreen("ConfirmEntry");
        Check(CabinetState.ShouldShow(cfg), "Waiting for a card permits display");
        CabinetState.PhaseSeconds = 16f;
        presenter.Tick(0.6f);
        CabinetState.OnEntryScreen("DisplayPleaseWait");
        presenter.Tick(0.1f);
        Check(!presenter.IsShowing && canvas.Alpha < 1f,
            "PleaseWait starts fading without the two-second buffer");
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
            presenter.Tick(0.6f);
            int stageTicks = stage.Ticks;
            int brainTicks = IdleBrain.Ticks;
            CabinetState.OnProcessStarted(process);
            presenter.Tick(0.01f);
            Check(!canvas.Visible && canvas.Alpha == 0f && !presenter.IsShowing &&
                stageTicks == stage.Ticks && brainTicks == IdleBrain.Ticks &&
                !CabinetState.ShouldShow(cfg),
                process + " hides immediately and stops animation, including ForceShow");
        }

        cfg.ForceShow = false;
        CabinetState.OnProcessStarted("AdvertiseProcess");
        CabinetState.PhaseSeconds = 16f;
        presenter.Tick(0.6f);
        CabinetState.OnProcessStarted("MusicSelectProcess");
        presenter.Tick(1f);
        Check(presenter.IsShowing, "Ordinary transition retains the hide buffer");
        presenter.Tick(1.1f);
        Check(!presenter.IsShowing && !canvas.Visible, "Ordinary transition eventually hides");

        CabinetState.OnProcessStarted("EntryProcess");
        CabinetState.OnEntryScreen("ConfirmEntry");
        Check(CabinetState.ShouldShow(cfg), "New entry session resets completed login");

        CabinetState.OnProcessStarted("AdvertiseProcess");
        CabinetState.PhaseSeconds = 30f;
        stage.Ready = false;
        presenter.Tick(10f);
        Check(!canvas.Visible && !presenter.IsShowing, "Failed character load cannot display an empty white surface");
        stage.Ready = true;

        string directory = Path.Combine(Path.GetTempPath(), "DesktopMateChecks-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var loaded = ModConfig.Load(directory);
            Check(loaded.Renderer == "2D" && ModConfig.Load(directory).Renderer == "2D",
                "Generated config and reloaded config default to 2D");
        }
        finally
        {
            File.Delete(Path.Combine(directory, ModConfig.FileName));
            Directory.Delete(directory);
        }
        Console.WriteLine(_passed + " behavior checks passed (headless; Unity rendering is not tested).");
    }
}
