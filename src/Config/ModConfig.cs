using System;
using System.IO;
using System.Text;
using MelonLoader;

namespace DesktopMateOnMaimai.Config
{
    /// <summary>
    /// 全部可配置项。字段语义与 assets/DesktopMate.toml 一一对应。
    /// </summary>
    internal sealed class ModConfig
    {
        public const string FileName = "DesktopMate.toml";

        // ---- General ----
        public bool Enabled = true;
        public string HotkeyToggle = "F9";

        // ---- Display ----
        public string Renderer = "2D";              // "3D" | "2D"
        public int ReferenceWidth = 1920;
        public int ReferenceHeight = 1080;
        public int SortingOrder = 20000;
        public string Anchor = "BottomRight";       // BottomLeft|BottomCenter|BottomRight|Center|Custom
        public float OffsetX = -140f;
        public float OffsetY = 0f;
        public float Scale = 1.0f;
        public string PlayerSlot = "Auto";          // Auto|1P|2P|Both
        public int RenderTextureWidth = 900;
        public int RenderTextureHeight = 1080;
        public int CharacterLayer = 30;
        public bool CircleEntrance = true;
        public float CircleCenterX = 0.5f;
        public float CircleCenterY = -1f; // auto: center of the lower square main monitor
        public float CircleRadius = 0.5f; // fraction of the game monitor's fitted width
        public float HeadFraction = 0.20f;
        public float PeekInSeconds = 0.65f;
        public float PeekHoldSeconds = 1.3f;
        public float EnterSeconds = 1.2f;
        public float PeekTiltDegrees = 16f;

        // ---- Trigger ----
        public bool ShowInAttract = true;           // 系统闲置：广告/演示循环
        public bool ShowInEntryWaiting = true;      // 未登录：投币后等待刷卡
        public bool ShowInCommercial = false;       // 广告视频播放期间是否显示
        public float ShowDelaySeconds = 15f;
        public float HideDelaySeconds = 2f;
        public bool HideDuringGame = true;

        // ---- Character ----
        public string CharacterId = "placeholder";
        public float Opacity = 1.0f;
        public float FadeInSeconds = 0.6f;
        public float FadeOutSeconds = 0.35f;
        public bool EnableBlink = true;
        public bool EnableLookAt = true;
        public float CharacterHeightPixels = 860f;  // 只影响 2D 渲染与 3D 相机取景
        public float YawDegrees = 0f;               // 朝向微调（面向玩家）

        // ---- Animation / 待机 ----
        public float IdleMinSeconds = 6f;
        public float IdleMaxSeconds = 14f;
        public string[] IdleVariants = { "Idle", "Peek", "Stretch", "Wave" };
        public float[] IdleWeights = { 6f, 2f, 1f, 1f };
        public float TimeJitter = 0.35f;            // 对应 DesktopMate 的 AddRandomFloat 语义
        public float GazeRadius = 0.35f;

        // ---- Debug ----
        public bool LogStateChanges = true;
        public bool ForceShow = false;
        public bool CaptureOnShow = false;

        public static ModConfig Load(string dir)
        {
            string path = Path.Combine(dir, FileName);
            var cfg = new ModConfig();
            if (!File.Exists(path))
            {
                TryWriteTemplate(path);
                return cfg;
            }

            try
            {
                var t = TinyToml.Load(path);
                cfg.Enabled = t.GetBool("General", "Enabled", cfg.Enabled);
                cfg.HotkeyToggle = t.GetString("General", "HotkeyToggle", cfg.HotkeyToggle);

                cfg.Renderer = t.GetString("Display", "Renderer", cfg.Renderer).ToUpperInvariant();
                cfg.ReferenceWidth = t.GetInt("Display", "ReferenceWidth", cfg.ReferenceWidth);
                cfg.ReferenceHeight = t.GetInt("Display", "ReferenceHeight", cfg.ReferenceHeight);
                cfg.SortingOrder = t.GetInt("Display", "SortingOrder", cfg.SortingOrder);
                cfg.Anchor = t.GetString("Display", "Anchor", cfg.Anchor);
                cfg.OffsetX = t.GetFloat("Display", "OffsetX", cfg.OffsetX);
                cfg.OffsetY = t.GetFloat("Display", "OffsetY", cfg.OffsetY);
                cfg.Scale = t.GetFloat("Display", "Scale", cfg.Scale);
                cfg.PlayerSlot = t.GetString("Display", "PlayerSlot", cfg.PlayerSlot);
                cfg.RenderTextureWidth = t.GetInt("Display", "RenderTextureWidth", cfg.RenderTextureWidth);
                cfg.RenderTextureHeight = t.GetInt("Display", "RenderTextureHeight", cfg.RenderTextureHeight);
                cfg.CharacterLayer = t.GetInt("Display", "CharacterLayer", cfg.CharacterLayer);
                cfg.CircleEntrance = t.GetBool("Display", "CircleEntrance", cfg.CircleEntrance);
                cfg.CircleCenterX = Clamp(t.GetFloat("Display", "CircleCenterX", cfg.CircleCenterX), 0f, 1f);
                cfg.CircleCenterY = t.GetFloat("Display", "CircleCenterY", cfg.CircleCenterY);
                cfg.CircleRadius = Clamp(t.GetFloat("Display", "CircleRadius", cfg.CircleRadius), 0.1f, 0.5f);
                cfg.HeadFraction = Clamp(t.GetFloat("Display", "HeadFraction", cfg.HeadFraction), 0.1f, 0.5f);
                cfg.PeekInSeconds = Math.Max(0.05f, t.GetFloat("Animation", "PeekInSeconds", cfg.PeekInSeconds));
                cfg.PeekHoldSeconds = Math.Max(0f, t.GetFloat("Animation", "PeekHoldSeconds", cfg.PeekHoldSeconds));
                cfg.EnterSeconds = Math.Max(0.05f, t.GetFloat("Animation", "EnterSeconds", cfg.EnterSeconds));
                cfg.PeekTiltDegrees = t.GetFloat("Animation", "PeekTiltDegrees", cfg.PeekTiltDegrees);

                cfg.ShowInAttract = t.GetBool("Trigger", "ShowInAttract", cfg.ShowInAttract);
                cfg.ShowInEntryWaiting = t.GetBool("Trigger", "ShowInEntryWaiting", cfg.ShowInEntryWaiting);
                cfg.ShowInCommercial = t.GetBool("Trigger", "ShowInCommercial", cfg.ShowInCommercial);
                cfg.ShowDelaySeconds = t.GetFloat("Trigger", "ShowDelaySeconds", cfg.ShowDelaySeconds);
                cfg.HideDelaySeconds = t.GetFloat("Trigger", "HideDelaySeconds", cfg.HideDelaySeconds);
                cfg.HideDuringGame = t.GetBool("Trigger", "HideDuringGame", cfg.HideDuringGame);

                cfg.CharacterId = t.GetString("Character", "Id", cfg.CharacterId);
                cfg.Opacity = Clamp(t.GetFloat("Character", "Opacity", cfg.Opacity), 0f, 1f);
                cfg.FadeInSeconds = t.GetFloat("Character", "FadeInSeconds", cfg.FadeInSeconds);
                cfg.FadeOutSeconds = t.GetFloat("Character", "FadeOutSeconds", cfg.FadeOutSeconds);
                cfg.EnableBlink = t.GetBool("Character", "EnableBlink", cfg.EnableBlink);
                cfg.EnableLookAt = t.GetBool("Character", "EnableLookAt", cfg.EnableLookAt);
                cfg.CharacterHeightPixels = t.GetFloat("Character", "HeightPixels", cfg.CharacterHeightPixels);
                cfg.YawDegrees = t.GetFloat("Character", "YawDegrees", cfg.YawDegrees);

                cfg.IdleMinSeconds = t.GetFloat("Animation", "IdleMinSeconds", cfg.IdleMinSeconds);
                cfg.IdleMaxSeconds = t.GetFloat("Animation", "IdleMaxSeconds", cfg.IdleMaxSeconds);
                cfg.IdleVariants = t.GetStringArray("Animation", "IdleVariants", cfg.IdleVariants);
                cfg.IdleWeights = t.GetFloatArray("Animation", "IdleWeights", cfg.IdleWeights);
                cfg.TimeJitter = t.GetFloat("Animation", "TimeJitter", cfg.TimeJitter);
                cfg.GazeRadius = t.GetFloat("Animation", "GazeRadius", cfg.GazeRadius);

                cfg.LogStateChanges = t.GetBool("Debug", "LogStateChanges", cfg.LogStateChanges);
                cfg.ForceShow = t.GetBool("Debug", "ForceShow", cfg.ForceShow);
                cfg.CaptureOnShow = t.GetBool("Debug", "CaptureOnShow", cfg.CaptureOnShow);
            }
            catch (Exception e)
            {
                MelonLogger.Error("[DesktopMate] 配置解析失败，使用默认值: " + e.Message);
            }
            return cfg;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        private static void TryWriteTemplate(string path)
        {
            const string template = @"[General]
Enabled = true
HotkeyToggle = ""F9""
[Display]
Renderer = ""2D""
ReferenceWidth = 1920
ReferenceHeight = 1080
SortingOrder = 20000
Anchor = ""BottomRight""
OffsetX = -140
OffsetY = 0
Scale = 1.0
PlayerSlot = ""Auto""
RenderTextureWidth = 900
RenderTextureHeight = 1080
CharacterLayer = 30
CircleEntrance = true
CircleCenterX = 0.5
CircleCenterY = -1
CircleRadius = 0.5
HeadFraction = 0.20
[Trigger]
ShowInAttract = true
ShowInEntryWaiting = true
ShowInCommercial = false
ShowDelaySeconds = 15
HideDelaySeconds = 2
HideDuringGame = true
[Character]
Id = ""placeholder""
Opacity = 1.0
FadeInSeconds = 0.6
FadeOutSeconds = 0.35
EnableBlink = true
EnableLookAt = true
HeightPixels = 860
YawDegrees = 0
[Animation]
IdleMinSeconds = 6
IdleMaxSeconds = 14
IdleVariants = [""Idle"", ""Peek"", ""Stretch"", ""Wave""]
IdleWeights = [6, 2, 1, 1]
TimeJitter = 0.35
GazeRadius = 0.35
PeekInSeconds = 0.65
PeekHoldSeconds = 1.3
EnterSeconds = 1.2
PeekTiltDegrees = 16
[Debug]
LogStateChanges = true
ForceShow = false
CaptureOnShow = false
";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, template, new UTF8Encoding(false));
                MelonLogger.Msg("[DesktopMate] 已生成默认配置: " + path);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DesktopMate] 写入默认配置失败: " + e.Message);
            }
        }
    }
}
