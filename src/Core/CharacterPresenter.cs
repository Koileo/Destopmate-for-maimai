using System;
using DesktopMateOnMaimai.Config;
using MelonLoader;

namespace DesktopMateOnMaimai.Core
{
    /// <summary>
    /// 表现层总控：把「机台是否空闲」翻译成「淡入 / 待机 / 淡出」。
    /// 所有状态切换都带滞回，避免刷卡瞬间人物闪烁。
    /// </summary>
    internal sealed class CharacterPresenter
    {
        private readonly ModConfig _cfg;
        private readonly OverlayCanvas _overlay;
        private readonly CharacterStage _stage;
        private readonly IdleBrain _brain;

        private float _alpha;           // 当前实际透明度(0..1)，与 cfg.Opacity 相乘
        private float _wantTimer;       // 条件满足/失效的持续计时
        private bool _showing;
        private float _motionTime;

        public CharacterPresenter(ModConfig cfg, OverlayCanvas overlay, CharacterStage stage)
        {
            _cfg = cfg;
            _overlay = overlay;
            _stage = stage;
            _brain = stage != null && stage.Driver != null ? new IdleBrain(cfg, stage.Driver) : null;
        }

        public bool IsShowing => _showing;

        public void Tick(float dt)
        {
            if (_cfg == null || _overlay == null) return;

            if (_cfg.HideDuringGame && CabinetState.IsGameOrResult)
            {
                _showing = false;
                _wantTimer = 0f;
                _alpha = 0f;
                _motionTime = 0f;
                _overlay.SetAlpha(0f);
                _overlay.SetVisible(false);
                return;
            }

            bool cond = _stage != null && _stage.Ready && CabinetState.ShouldShow(_cfg) &&
                (_cfg.ForceShow || CabinetState.PhaseSeconds >= _cfg.ShowDelaySeconds);

            if (cond != _showing)
            {
                _wantTimer += dt;
                float need = cond ? 0.05f :
                    (CabinetState.EntryMustHide ? 0f : _cfg.HideDelaySeconds);
                if (_wantTimer >= need)
                {
                    _showing = cond;
                    _wantTimer = 0f;
                    if (_showing)
                    {
                        _motionTime = 0f;
                        _brain?.Reset();
                        if (_cfg.LogStateChanges)
                            MelonLogger.Msg("[DesktopMate] 进入展示 (" + CabinetState.Phase + ")");
                    }
                    else if (_cfg.LogStateChanges)
                    {
                        MelonLogger.Msg("[DesktopMate] 退出展示 (" + CabinetState.Phase + ")");
                    }
                }
            }
            else
            {
                _wantTimer = 0f;
            }

            float target = _showing ? 1f : 0f;
            float speed = _showing
                ? (_cfg.FadeInSeconds <= 0.01f ? 100f : 1f / _cfg.FadeInSeconds)
                : (_cfg.FadeOutSeconds <= 0.01f ? 100f : 1f / _cfg.FadeOutSeconds);

            _alpha = MoveTowards(_alpha, target, speed * dt);
            // 进场时间线用钳制过的 dt：卡顿一帧不该让角色瞬移半程
            float motionDt = dt > 0.1f ? 0.1f : dt;
            _motionTime = _showing
                ? Math.Min(EntranceMotion.Duration(_cfg), _motionTime + motionDt)
                : Math.Max(0f, _motionTime - motionDt * EntranceMotion.Duration(_cfg) / Math.Max(0.05f, _cfg.FadeOutSeconds));
            _overlay.SetEntrance(_motionTime, _cfg);

            _overlay.SetAlpha(_alpha * _cfg.Opacity);
            _overlay.SetVisible(_alpha > 0.002f);

            if (_alpha > 0.002f)
            {
                _brain?.Tick(dt);
                _stage?.Tick(dt);
            }
        }

        private static float MoveTowards(float cur, float target, float maxDelta)
        {
            if (Math.Abs(target - cur) <= maxDelta) return target;
            return cur + Math.Sign(target - cur) * maxDelta;
        }
    }
}
