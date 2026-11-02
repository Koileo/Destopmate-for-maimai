// Only presentation and logging are replaced: state, config and timing use production sources.
namespace MelonLoader
{
    internal static class MelonLogger
    {
        public static void Msg(string message) { }
        public static void Warning(string message) { }
        public static void Error(string message) { }
    }
}

namespace DesktopMateOnMaimai
{
    internal static class ModMain
    {
        internal static Config.ModConfig Cfg;
    }
}

namespace DesktopMateOnMaimai.Core
{
    internal sealed class OverlayCanvas
    {
        public float Alpha;
        public bool Visible;
        public float MotionTime;
        public void SetAlpha(float alpha) { Alpha = alpha; }
        public void SetVisible(bool visible) { Visible = visible; }
        public void SetEntrance(float time, Config.ModConfig cfg) { MotionTime = time; }
        public bool HasUpperScreen = true;
        public bool UpperSeat;
        public float AlphaAtMove;
        public void SetUpperSeat(bool seated) { UpperSeat = seated; AlphaAtMove = Alpha; }
    }

    internal sealed class CharacterStage
    {
        public object Driver = new object();
        public bool SupportsSeat = true;
        public bool Seated;
        public bool Ready = true;
        public int Ticks;
        public int ResetCount;
        public readonly System.Collections.Generic.List<EntrancePhase> Phases = new System.Collections.Generic.List<EntrancePhase>();
        public void Tick(float dt) { Ticks++; }
        public void ResetForEntrance() { ResetCount++; }
        public void PlayEntrancePhase(EntrancePhase phase) { Phases.Add(phase); }
        public void SampleEntrance(float time, Config.ModConfig cfg) { }
        public void SetSeated(bool seated) { Seated = seated; }
    }

    internal sealed class IdleBrain
    {
        public static int Ticks;
        public static int TouchReactions;
        public bool ReactToTouch() { TouchReactions++; return true; }
        public IdleBrain(Config.ModConfig cfg, object driver) { }
        public void Reset() { }
        public void Tick(float dt) { Ticks++; }
        public void SetSeated(bool seated) { }
    }
}
