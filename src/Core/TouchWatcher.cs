using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace DesktopMateOnMaimai.Core
{
    // Read completed game input frames. No prefix, input writes or event consumption.
    internal static class TouchWatcher
    {
        private static Func<int, bool> _anyDown;
        private static bool _firstPlayer, _secondPlayer;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type input = AccessTools.TypeByName("Manager.InputManager");
                MethodInfo read = input == null ? null : AccessTools.Method(input,
                    "GetTouchPanelAreaAnyDown", new[] { typeof(int) });
                MethodInfo update = input == null ? null : AccessTools.Method(input,
                    "UpdateTouchPanel", Type.EmptyTypes);
                if (read == null || update == null || !read.IsStatic || read.ReturnType != typeof(bool))
                    throw new MissingMethodException("InputManager touch API");
                _anyDown = (Func<int, bool>)Delegate.CreateDelegate(typeof(Func<int, bool>), read);
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(TouchWatcher), nameof(AfterTouchUpdate)));
                MelonLogger.Msg("[DesktopMate] 触摸互动已接入（只读，区分 1P/2P）");
            }
            catch (Exception e)
            {
                _anyDown = null;
                MelonLogger.Warning("[DesktopMate] 触摸互动不可用，保留普通陪伴: " + e.Message);
            }
        }

        private static void AfterTouchUpdate()
        {
            if (_anyDown == null) return;
            try { Record(_anyDown(0), _anyDown(1)); }
            catch (Exception e)
            {
                _anyDown = null;
                _firstPlayer = _secondPlayer = false;
                MelonLogger.Warning("[DesktopMate] 已停用触摸读取: " + e.Message);
            }
        }

        internal static void Record(bool firstPlayer, bool secondPlayer)
        {
            _firstPlayer |= firstPlayer;
            _secondPlayer |= secondPlayer;
        }

        public static bool Consume(string playerSlot)
        {
            bool touched = string.Equals(playerSlot, "2P", StringComparison.OrdinalIgnoreCase)
                ? _secondPlayer : _firstPlayer;
            // Auto/Both follow the single overlay's 1P slot. Never replay hidden touches.
            _firstPlayer = _secondPlayer = false;
            return touched;
        }
    }
}
