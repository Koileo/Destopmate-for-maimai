using System;
using System.IO;
using DesktopMateOnMaimai.Config;
using DesktopMateOnMaimai.Core;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(DesktopMateOnMaimai.ModMain), "DesktopMate for maimai", "0.2.0", "Koileo")]
[assembly: MelonGame("sega-interactive", "Sinmai")]

namespace DesktopMateOnMaimai
{
    public class ModMain : MelonMod
    {
        internal static ModConfig Cfg;
        private CharacterPresenter _presenter;

        private HarmonyLib.Harmony _harmony;
        private OverlayCanvas _overlay;
        private CharacterStage _stage;

        private bool _enabled;
        private bool _buildTried;
        private int _lastScreenW, _lastScreenH;
        private float _startupDelay = 3f;   // 等游戏场景稳定后再建 UI
        private float _captureTime;
        private int _captureIndex;

        public override void OnInitializeMelon()
        {
            string modsDir = SafeModsDir();
            string root = Path.Combine(modsDir, "DesktopMate");
            try { Directory.CreateDirectory(root); } catch { }

            Cfg = ModConfig.Load(root);
            _enabled = Cfg.Enabled;

            _harmony = new HarmonyLib.Harmony("DesktopMateOnMaimai");
            CabinetWatcher.Install(_harmony);

            LoggerInstance.Msg("DesktopMate for maimai 已载入；角色目录: " + Path.Combine(root, "Characters"));
        }

        private static string SafeModsDir()
        {
            try
            {
                var d = MelonEnvironment.ModsDirectory;
                if (!string.IsNullOrEmpty(d)) return d;
            }
            catch { }
            return Path.Combine(Directory.GetCurrentDirectory(), "Mods");
        }

        public override void OnUpdate()
        {
            HandleHotkey();
            if (!_enabled) return;

            float dt = Time.unscaledDeltaTime;
            if (CabinetState.Phase != CabinetPhase.Unknown) CabinetState.PhaseSeconds += dt;

            if (!_buildTried)
            {
                _startupDelay -= dt;
                if (_startupDelay <= 0f) Build();
                return;
            }
            if (_overlay == null) return;

            if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
                Relayout();

            _presenter?.Tick(dt);
            if (_captureIndex < 3 && Cfg.CaptureOnShow && _presenter != null && _presenter.IsShowing)
            {
                _captureTime += dt;
                float threshold = _captureIndex == 0 ? Cfg.PeekInSeconds + 0.2f :
                    (_captureIndex == 1 ? Cfg.PeekInSeconds + Cfg.PeekHoldSeconds + Cfg.EnterSeconds * 0.5f : EntranceMotion.Duration(Cfg) + 0.4f);
                if (_captureTime >= threshold)
                {
                    string file = _captureIndex == 0 ? "head-peek.png" : (_captureIndex == 1 ? "body-enter.png" : "display-check.png");
                    _captureIndex++;
                    string path = Path.Combine(SafeModsDir(), "DesktopMate", file);
                    Canvas.ForceUpdateCanvases();
                    LoggerInstance.Msg("显示诊断: screen=" + Screen.width + "x" + Screen.height +
                        " canvas=" + _overlay.Canvas.pixelRect + " rect=" + _overlay.CharacterRect.rect +
                        " pos=" + _overlay.CharacterRect.position + " scale=" + _overlay.CharacterRect.lossyScale +
                        " alpha=" + _overlay.Group.alpha + " order=" + _overlay.Canvas.sortingOrder);
                    ScreenCapture.CaptureScreenshot(path);
                    if (_captureIndex == 3) _stage?.CaptureTexture(Path.Combine(SafeModsDir(), "DesktopMate", "character-texture.png"));
                    LoggerInstance.Msg("显示截图: " + path);
                }
            }
        }

        private void HandleHotkey()
        {
            if (string.IsNullOrEmpty(Cfg?.HotkeyToggle)) return;
            try
            {
                if (!Enum.TryParse(Cfg.HotkeyToggle, true, out KeyCode key)) return;
                if (Input.GetKeyDown(key))
                {
                    _enabled = !_enabled;
                    if (!_enabled && _overlay != null) _overlay.SetVisible(false);
                    LoggerInstance.Msg("展示已" + (_enabled ? "开启" : "关闭"));
                }
            }
            catch { }
        }

        private void Build()
        {
            _buildTried = true;
            try
            {
                string root = Path.Combine(SafeModsDir(), "DesktopMate");
                string charDir = Path.Combine(Path.Combine(root, "Characters"), Cfg.CharacterId);

                _overlay = OverlayCanvas.Create(Cfg);
                _stage = CharacterStage.Create(_overlay, Cfg, charDir, Cfg.CharacterId);
                _presenter = new CharacterPresenter(Cfg, _overlay, _stage);

                _lastScreenW = Screen.width;
                _lastScreenH = Screen.height;

                if (!_stage.Ready)
                    LoggerInstance.Warning("未找到可用角色资源（" + charDir + "），请放入 character.ab 或 sprites/*.png");
                else
                    LoggerInstance.Msg("角色已就绪: " + Cfg.CharacterId + " / " + (_stage.Is3D ? "3D" : "2D"));
            }
            catch (Exception e)
            {
                LoggerInstance.Error("初始化显示层失败: " + e);
            }
        }

        private void Relayout()
        {
            try
            {
                _lastScreenW = Screen.width;
                _lastScreenH = Screen.height;
                _overlay?.ApplyLayout(Cfg);
                LoggerInstance.Msg("分辨率变化 -> " + Screen.width + "x" + Screen.height);
            }
            catch (Exception e) { LoggerInstance.Warning("重排布局失败: " + e.Message); }
        }

        public override void OnApplicationQuit()
        {
            try
            {
                _presenter = null;
                _stage?.Dispose();
                _overlay?.Dispose();
                CharacterLoader.UnloadAll();
                _harmony?.UnpatchSelf();
            }
            catch (Exception e) { LoggerInstance.Warning("退出清理异常: " + e.Message); }
        }
    }
}
