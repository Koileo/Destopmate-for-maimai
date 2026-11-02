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
        public string HotkeySeat = "F8";

        // ---- Display ----
        public string Renderer = "3D";              // "3D" | "2D"
        public int ReferenceWidth = 1920;
        public int ReferenceHeight = 1080;
        public int SortingOrder = 20000;
        public string Anchor = "BottomRight";       // BottomLeft|BottomCenter|BottomRight|Center|Custom
        public float OffsetX = -140f;
        public float OffsetY = 0f;
        public float Scale = 1.0f;
        public string PlayerSlot = "Auto";          // Auto|1P|2P|Both
        public int RenderTextureWidth = 1620;
        public int RenderTextureHeight = 1620;
        public int RenderTextureAntiAliasing = 4;
        public int CharacterLayer = 30;
        public bool CircleEntrance = true;
        public bool AnimatedEntrance = false; // 全身在固定站位淡入；旧探头/绕圆仅显式开启
        public float CircleCenterX = 0.5f;
        public float CircleCenterY = -1f; // auto: center of the lower square main monitor
        public float CircleRadius = 0.5f; // fraction of the game monitor's fitted width
        public float HeadFraction = 0.20f;
        public float CirclePeekAngle = 18f;       // 右侧探头的圆周角度
        public float CircleRestAngle = -30f;      // 常驻脚底相对圆心的角度，避开底部提示
        public float CircleRestRadius = 0.76f;    // 常驻脚底到圆心的距离 / 半径
        public float CircleHeightRadius = 1.65f;  // 全身接近圆屏高度的四分之三
        public float CircleResidentX = 0.46f; // 相对圆心的横向偏移 / 半径
        public float CircleResidentY = -1.12f; // 脚放到圆边下方，腿由圆屏自然遮挡
        public bool AllowUpperSeat = true;
        public bool UpperSeatOnLogin = false;
        public float UpperSeatX = 0.78f; // 上屏靠右
        public float UpperSeatY = 0.48f; // 上屏内部，自下往上
        public float UpperSeatHeight = 0.52f; // 相对主屏宽度
        public float PeekInSeconds = 0.72f;
        public float PeekHoldSeconds = 2.55f;
        public float EnterSeconds = 1.55f;
        public float PeekTiltDegrees = 14f;

        // ---- Trigger ----
        public bool ShowInAttract = true;           // 系统闲置：广告/演示循环
        public bool ShowInEntryWaiting = false;     // 兼容旧配置；刷卡流程始终隐藏
        public bool ShowInCommercial = false;       // 广告视频播放期间是否显示
        public float ShowDelaySeconds = 15f;
        public float HideDelaySeconds = 2f;
        public bool HideDuringGame = true;

        // ---- Character ----
        public string CharacterId = "miku-local";
        public float Opacity = 1.0f;
        public float FadeInSeconds = 0.6f;
        public float FadeOutSeconds = 0.35f;
        public bool EnableBlink = true;
        public bool EnableLookAt = false; // 兼容旧配置；待机视线始终朝前
        public float CharacterHeightPixels = 860f;  // 只影响 2D 渲染与 3D 相机取景
        public float YawDegrees = 180f;             // 默认 Miku 面向玩家

        // ---- Animation / 待机 ----
        public float IdleMinSeconds = 6f;
        public float IdleMaxSeconds = 14f;
        public string[] IdleVariants = { "Idle", "Think", "HandsOnHips", "Shift", "LookAround", "Nod", "Wave" };
        public float[] IdleWeights = { 7f, 2f, 1f, 1f, 1f, 1f, 1f };
        public float StandingIdleMotion = 0f; // 休息时固定站姿，表情与主动动作独立保留
        public float TimeJitter = 0.35f;            // 对应 DesktopMate 的 AddRandomFloat 语义
        public float GazeRadius = 0f; // 兼容旧配置；不再随机改变视线

        // ---- Animation / 动作细节（v0.3）----
        public float MotionSpeed = 1.0f;            // 屏内待机速度；进场共用空间时间线
        public float GestureIntensity = 1.0f;       // 手势幅度倍率（1.0 = 设计值）
        public float BreathSeconds = 3.9f;          // 一次完整呼吸周期
        public float DoubleBlinkChance = 0.18f;     // 二连眨概率
        public bool EnableLegMotion = true;         // 腿部微动（圆裁切遮住下半身时可关）
        public bool EnableActionCombos = true;
        public bool ReducedMotion = false;
        public float EntranceSettleSeconds = 0.35f; // 进入圆内后的落定回弹时长

        // ---- Interaction ----
        public bool TouchEnabled = true;
        public float TouchCooldownSeconds = 8f;

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
                cfg.HotkeySeat = t.GetString("General", "HotkeySeat", cfg.HotkeySeat);

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
                int samples = t.GetInt("Display", "RenderTextureAntiAliasing", cfg.RenderTextureAntiAliasing);
                cfg.RenderTextureAntiAliasing = samples <= 1 ? 1 : samples <= 2 ? 2 : samples <= 4 ? 4 : 8;
                cfg.CharacterLayer = t.GetInt("Display", "CharacterLayer", cfg.CharacterLayer);
                cfg.CircleEntrance = t.GetBool("Display", "CircleEntrance", cfg.CircleEntrance);
                cfg.AnimatedEntrance = t.GetBool("Display", "AnimatedEntrance", cfg.AnimatedEntrance);
                cfg.CircleCenterX = Clamp(t.GetFloat("Display", "CircleCenterX", cfg.CircleCenterX), 0f, 1f);
                cfg.CircleCenterY = t.GetFloat("Display", "CircleCenterY", cfg.CircleCenterY);
                cfg.CircleRadius = Clamp(t.GetFloat("Display", "CircleRadius", cfg.CircleRadius), 0.1f, 0.5f);
                cfg.HeadFraction = Clamp(t.GetFloat("Display", "HeadFraction", cfg.HeadFraction), 0.1f, 0.5f);
                cfg.CirclePeekAngle = Clamp(t.GetFloat("Display", "CirclePeekAngle", cfg.CirclePeekAngle), -180f, 180f);
                cfg.CircleRestAngle = Clamp(t.GetFloat("Display", "CircleRestAngle", cfg.CircleRestAngle), -180f, 180f);
                cfg.CircleRestRadius = Clamp(t.GetFloat("Display", "CircleRestRadius", cfg.CircleRestRadius), 0.45f, 0.92f);
                cfg.CircleHeightRadius = Clamp(t.GetFloat("Display", "CircleHeightRadius", cfg.CircleHeightRadius), 0.55f, 1.8f);
                cfg.CircleResidentX = Clamp(t.GetFloat("Display", "CircleResidentX", cfg.CircleResidentX), -1f, 1f);
                cfg.CircleResidentY = Clamp(t.GetFloat("Display", "CircleResidentY", cfg.CircleResidentY), -1.8f, 0f);
                cfg.AllowUpperSeat = t.GetBool("Display", "AllowUpperSeat", cfg.AllowUpperSeat);
                cfg.UpperSeatOnLogin = t.GetBool("Display", "UpperSeatOnLogin", cfg.UpperSeatOnLogin);
                cfg.UpperSeatX = Clamp(t.GetFloat("Display", "UpperSeatX", cfg.UpperSeatX), 0.1f, 0.9f);
                cfg.UpperSeatY = Clamp(t.GetFloat("Display", "UpperSeatY", cfg.UpperSeatY), 0.1f, 0.8f);
                cfg.UpperSeatHeight = Clamp(t.GetFloat("Display", "UpperSeatHeight", cfg.UpperSeatHeight), 0.15f, 0.6f);
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
                cfg.StandingIdleMotion = Clamp(t.GetFloat("Animation", "StandingIdleMotion", cfg.StandingIdleMotion), 0f, 1f);
                cfg.TimeJitter = t.GetFloat("Animation", "TimeJitter", cfg.TimeJitter);
                cfg.GazeRadius = t.GetFloat("Animation", "GazeRadius", cfg.GazeRadius);
                cfg.MotionSpeed = Clamp(t.GetFloat("Animation", "MotionSpeed", cfg.MotionSpeed), 0.2f, 2.5f);
                cfg.GestureIntensity = Clamp(t.GetFloat("Animation", "GestureIntensity", cfg.GestureIntensity), 0f, 1.5f);
                cfg.BreathSeconds = t.GetFloat("Animation", "BreathSeconds", cfg.BreathSeconds);
                if (cfg.BreathSeconds < 1.2f) cfg.BreathSeconds = 1.2f;
                cfg.DoubleBlinkChance = Clamp(t.GetFloat("Animation", "DoubleBlinkChance", cfg.DoubleBlinkChance), 0f, 1f);
                cfg.EnableLegMotion = t.GetBool("Animation", "EnableLegMotion", cfg.EnableLegMotion);
                cfg.EnableActionCombos = t.GetBool("Animation", "EnableActionCombos", cfg.EnableActionCombos);
                cfg.ReducedMotion = t.GetBool("Animation", "ReducedMotion", cfg.ReducedMotion);
                cfg.EntranceSettleSeconds = Clamp(t.GetFloat("Animation", "EntranceSettleSeconds", cfg.EntranceSettleSeconds), 0f, 2f);

                cfg.TouchEnabled = t.GetBool("Interaction", "TouchEnabled", cfg.TouchEnabled);
                cfg.TouchCooldownSeconds = Clamp(t.GetFloat("Interaction", "TouchCooldownSeconds", cfg.TouchCooldownSeconds), 2f, 60f);

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
HotkeySeat = ""F8""
[Display]
Renderer = ""3D""
ReferenceWidth = 1920
ReferenceHeight = 1080
SortingOrder = 20000
Anchor = ""BottomRight""
OffsetX = -140
OffsetY = 0
Scale = 1.0
PlayerSlot = ""Auto""
RenderTextureWidth = 1620
RenderTextureHeight = 1620
RenderTextureAntiAliasing = 4
CharacterLayer = 30
CircleEntrance = true
AnimatedEntrance = false
CircleCenterX = 0.5
CircleCenterY = -1
CircleRadius = 0.5
HeadFraction = 0.20
CirclePeekAngle = 18
CircleRestAngle = -30
CircleRestRadius = 0.76
CircleHeightRadius = 1.65
CircleResidentX = 0.46
CircleResidentY = -1.12
AllowUpperSeat = true
UpperSeatOnLogin = false
UpperSeatX = 0.78
UpperSeatY = 0.48
UpperSeatHeight = 0.52
[Trigger]
ShowInAttract = true
ShowInEntryWaiting = false
ShowInCommercial = false
ShowDelaySeconds = 15
HideDelaySeconds = 2
HideDuringGame = true
[Character]
Id = ""miku-local""
Opacity = 1.0
FadeInSeconds = 0.6
FadeOutSeconds = 0.35
EnableBlink = true
EnableLookAt = false
HeightPixels = 860
YawDegrees = 180
[Animation]
IdleMinSeconds = 6
IdleMaxSeconds = 14
IdleVariants = [""Idle"", ""Think"", ""HandsOnHips"", ""Shift"", ""LookAround"", ""Nod"", ""Wave""]
IdleWeights = [7, 2, 1, 1, 1, 1, 1]
StandingIdleMotion = 0
TimeJitter = 0.35
GazeRadius = 0
MotionSpeed = 1.0
GestureIntensity = 1.0
BreathSeconds = 3.9
DoubleBlinkChance = 0.18
EnableLegMotion = true
EnableActionCombos = true
ReducedMotion = false
EntranceSettleSeconds = 0.35
PeekInSeconds = 0.72
PeekHoldSeconds = 2.55
EnterSeconds = 1.55
PeekTiltDegrees = 14
[Interaction]
TouchEnabled = true
TouchCooldownSeconds = 8
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
