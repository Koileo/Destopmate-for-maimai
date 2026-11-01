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
        public void SetAlpha(float alpha) { Alpha = alpha; }
        public void SetVisible(bool visible) { Visible = visible; }
        public void SetEntrance(float time, Config.ModConfig cfg) { }
    }

    internal sealed class CharacterStage
    {
        public object Driver = new object();
        public bool Ready = true;
        public int Ticks;
        public void Tick(float dt) { Ticks++; }
    }

    internal sealed class IdleBrain
    {
        public static int Ticks;
        public IdleBrain(Config.ModConfig cfg, object driver) { }
        public void Reset() { }
        public void Tick(float dt) { Ticks++; }
    }
}
