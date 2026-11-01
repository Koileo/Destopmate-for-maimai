using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace DesktopMateOnMaimai.Core
{
    public enum CabinetPhase
    {
        Unknown = 0,
        Boot,        // 开机 / 启动画面 / PleaseWait
        Attract,     // 系统闲置：标题 Logo、广告循环
        Commercial,  // 闲置中的广告视频段
        Demo,        // 闲置中的自动演示游玩
        Entry,       // 已投币/待刷卡 → 登录流程
        Game,        // 谱面游玩
        Result,      // 结算
        Other
    }

    /// <summary>
    /// 机台状态。全部通过「进程名」字符串判断，不依赖 Assembly-CSharp 的编译期类型，
    /// 以便在 SDEZ 各版本（1.5x ~ 1.7x）之间保持可用。
    /// </summary>
    internal static class CabinetState
    {
        public static CabinetPhase Phase = CabinetPhase.Unknown;
        public static string ProcessName = "";
        public static float PhaseSeconds;

        /// <summary>Entry 流程当前界面（Monitor.Entry.Parts.ScreenType 的名称）。</summary>
        public static string EntryScreen = "";
        public static bool UserLoggedIn;

        /// <summary>Entry 流程中「还没刷卡 / 还没进游戏」的界面集合（白名单式收敛）。</summary>
        private static readonly HashSet<string> EntryWaitingScreens = new HashSet<string>(StringComparer.Ordinal)
        {
            "ConfirmEntry", "ConfirmFelica", "ConfirmAccessCode", "DisplayAccessCodeQR",
            "DisplayFelicaQR", "DispFelicaSiteAndQR", "ConfirmFelicaSite", "NoticeFelicaRegistration",
            "ConfirmExistingAime", "ConfirmExistingAimeContinue", "ConfirmInheritAime", "WaitPartner",
            "NoticeDataMigration", "ConfirmGuest"
        };

        internal static void Reset()
        {
            Phase = CabinetPhase.Unknown;
            ProcessName = "";
            PhaseSeconds = 0f;
            EntryScreen = "";
            UserLoggedIn = false;
        }

        internal static void OnProcessStarted(string typeName)
        {
            // Fade / common processes coexist with the actual game process.
            if (IsAuxiliary(typeName)) return;
            ProcessName = typeName ?? "";
            PhaseSeconds = 0f;
            UserLoggedIn = false;
            EntryScreen = "";
            Phase = Map(typeName);

            if (ModMain.Cfg != null && ModMain.Cfg.LogStateChanges)
                MelonLogger.Msg("[DesktopMate] 进程切换 -> " + ProcessName + " (" + Phase + ")");
        }

        internal static void OnProcessReleased(string typeName)
        {
            // 只有被释放的正是当前进程时才需要回退；回退到 Unknown，等下一次 AddProcess 修正。
            if (string.Equals(ProcessName, typeName, StringComparison.Ordinal))
            {
                ProcessName = "";
                PhaseSeconds = 0f;
                Phase = CabinetPhase.Unknown;
            }
        }

        internal static void OnEntryScreen(string screenName)
        {
            EntryScreen = screenName ?? "";
            if (EntryScreen == "DoneEntry") UserLoggedIn = true;
        }

        private static CabinetPhase Map(string n)
        {
            if (string.IsNullOrEmpty(n)) return CabinetPhase.Unknown;

            if (n.IndexOf("AdvertiseCommercial", StringComparison.OrdinalIgnoreCase) >= 0) return CabinetPhase.Commercial;
            if (n.IndexOf("AdvDemo", StringComparison.OrdinalIgnoreCase) >= 0) return CabinetPhase.Demo;
            if (n.IndexOf("Advertise", StringComparison.OrdinalIgnoreCase) >= 0) return CabinetPhase.Attract;

            if (n.IndexOf("Entry", StringComparison.OrdinalIgnoreCase) >= 0) return CabinetPhase.Entry;
            if (n.IndexOf("GameProcess", StringComparison.OrdinalIgnoreCase) >= 0) return CabinetPhase.Game;
            if (n.IndexOf("Result", StringComparison.OrdinalIgnoreCase) >= 0) return CabinetPhase.Result;
            if (n.IndexOf("PowerOn", StringComparison.OrdinalIgnoreCase) >= 0) return CabinetPhase.Boot;
            if (n.IndexOf("Startup", StringComparison.OrdinalIgnoreCase) >= 0) return CabinetPhase.Boot;
            if (n.IndexOf("PleaseWait", StringComparison.OrdinalIgnoreCase) >= 0) return CabinetPhase.Boot;
            if (n.IndexOf("TestMode", StringComparison.OrdinalIgnoreCase) >= 0) return CabinetPhase.Boot;

            return CabinetPhase.Other;
        }

        private static bool IsAuxiliary(string name)
        {
            return name == "FadeProcess" || name == "BlackoutFadeProcess" ||
                name == "CommonProcess" || name == "GenericProcess";
        }

        /// <summary>是否处于「系统闲置或用户未登录」的展示窗口。</summary>
        public static bool ShouldShow(Config.ModConfig cfg)
        {
            if (cfg == null) return false;
            if (cfg.HideDuringGame && IsGameOrResult) return false;
            if (cfg.ForceShow) return true;

            switch (Phase)
            {
                case CabinetPhase.Attract:
                    return cfg.ShowInAttract;
                case CabinetPhase.Commercial:
                    return cfg.ShowInCommercial;
                case CabinetPhase.Demo:
                    return cfg.ShowInAttract;
                case CabinetPhase.Entry:
                    if (!cfg.ShowInEntryWaiting) return false;
                    if (UserLoggedIn) return false;
                    return EntryWaitingScreens.Contains(EntryScreen);
                case CabinetPhase.Game:
                case CabinetPhase.Result:
                case CabinetPhase.Boot:
                case CabinetPhase.Other:
                case CabinetPhase.Unknown:
                default:
                    return false;
            }
        }

        public static bool IsGameOrResult => Phase == CabinetPhase.Game || Phase == CabinetPhase.Result;

        public static bool EntryMustHide => Phase == CabinetPhase.Entry &&
            (UserLoggedIn || EntryScreen == "DoneEntry" || EntryScreen == "DisplayPleaseWait");
    }

    /// <summary>
    /// 通过 Harmony 反射挂接机台状态机；任何一步失败都只降级为「不显示」，不影响游戏。
    /// </summary>
    internal static class CabinetWatcher
    {
        public static bool Installed { get; private set; }

        public static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type pm = FindType("Manager.ProcessManager");
                if (pm == null)
                {
                    MelonLogger.Warning("[DesktopMate] 未找到 Manager.ProcessManager，待机检测不可用");
                    return;
                }

                Patch(harmony, pm, "AddProcess", postfix: nameof(OnAddProcess));
                Patch(harmony, pm, "ReleaseProcess", postfix: nameof(OnReleaseProcess));

                Type screenManager = FindType("Monitor.Entry.Parts.ScreenManager");
                if (screenManager != null)
                {
                    Patch(harmony, screenManager, "OpenScreen", postfix: nameof(OnOpenScreen));
                    Patch(harmony, screenManager, "CloseScreen", postfix: nameof(OnCloseScreen));
                }

                Installed = true;
                MelonLogger.Msg("[DesktopMate] 机台状态监听已安装 (ProcessManager" +
                                (screenManager != null ? " + ScreenManager" : "") + ")");
            }
            catch (Exception e)
            {
                MelonLogger.Error("[DesktopMate] 安装状态监听失败: " + e);
            }
        }

        private static Type FindType(string fullName)
        {
            var t = AccessTools.TypeByName(fullName);
            if (t != null) return t;

            // 兜底：按短名在所有已加载程序集里找
            string shortName = fullName.Substring(fullName.LastIndexOf('.') + 1);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch { continue; }

                foreach (var x in types)
                    if (x != null && x.Name == shortName) return x;
            }
            return null;
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string methodName, string postfix)
        {
            var m = FindMethod(type, methodName);
            if (m == null)
            {
                MelonLogger.Warning("[DesktopMate] 未找到方法 " + type.Name + "." + methodName);
                return;
            }

            var post = new HarmonyMethod(typeof(CabinetWatcher).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(m, postfix: post);
        }

        private static MethodInfo FindMethod(Type type, string name)
        {
            try
            {
                var m = AccessTools.Method(type, name);
                if (m != null) return m;
            }
            catch { }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var x in type.GetMethods(flags))
                if (x.Name == name) return x;

            return null;
        }

        // ---- Harmony 回调：用 object[] __args 规避签名差异 ----

        private static void OnAddProcess(object[] __args)
        {
            if (__args == null || __args.Length == 0 || __args[0] == null) return;
            CabinetState.OnProcessStarted(__args[0].GetType().Name);
        }

        private static void OnReleaseProcess(object[] __args)
        {
            if (__args == null || __args.Length == 0 || __args[0] == null) return;
            CabinetState.OnProcessReleased(__args[0].GetType().Name);
        }

        private static void OnOpenScreen(object[] __args)
        {
            if (__args == null || __args.Length == 0 || __args[0] == null) return;
            CabinetState.OnEntryScreen(__args[0].ToString());
        }

        private static void OnCloseScreen(object[] __args)
        {
            CabinetState.OnEntryScreen("");
        }
    }
}
