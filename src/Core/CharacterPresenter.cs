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
        private EntrancePhase _entrancePhase = EntrancePhase.Hidden;
        private bool _entranceStarted;
        private bool _upperSeat;
        private bool _seatRequested;
        private bool _hadLogin;

        public bool CanChooseSeat => _cfg.AllowUpperSeat && CabinetState.CanChooseSeat &&
            _stage != null && _stage.Ready && _stage.SupportsSeat && _overlay.HasUpperScreen;
        public bool SeatRequested => _seatRequested;
        public void ChooseUpperSeat(bool value)
        {
            if (CanChooseSeat)
            {
                _seatRequested = value;
                _hadLogin = true;
            }
        }

        public CharacterPresenter(ModConfig cfg, OverlayCanvas overlay, CharacterStage stage)
        {
            _cfg = cfg;
            _overlay = overlay;
            _stage = stage;
            _brain = stage != null && stage.Driver != null ? new IdleBrain(cfg, stage.Driver) : null;
        }

        public bool IsShowing => _showing;

        public bool ReactToTouch()
        {
            if (!_showing || _alpha <= 0.002f || _entrancePhase != EntrancePhase.Inside ||
                CabinetState.IsGameOrResult || !(_upperSeat ? CanChooseSeat : CabinetState.ShouldShow(_cfg)))
                return false;
            return _brain != null && _brain.ReactToTouch();
        }

        public void Tick(float dt)
        {
            if (_cfg == null || _overlay == null) return;
            if (!CabinetState.UserLoggedIn) _seatRequested = false;
            else if (!_hadLogin) _seatRequested = _cfg.UpperSeatOnLogin;
            _hadLogin = CabinetState.UserLoggedIn;

            bool wantUpper = CanChooseSeat && _seatRequested;
            bool relocating = wantUpper != _upperSeat;

            bool mustHide = CabinetState.EntryMustHide ||
                (_cfg.HideDuringGame && CabinetState.IsGameOrResult);

            bool cond = !mustHide && !relocating && _stage != null && _stage.Ready &&
                (wantUpper || (CabinetState.ShouldShow(_cfg) &&
                (_cfg.ForceShow || _entranceStarted || CabinetState.PhaseSeconds >= _cfg.ShowDelaySeconds)));

            if (cond != _showing)
            {
                _wantTimer += dt;
                float need = cond ? 0.05f :
                    (relocating || CabinetState.UserLoggedIn || mustHide ? 0f : _cfg.HideDelaySeconds);
                if (_wantTimer >= need)
                {
                    _showing = cond;
                    _wantTimer = 0f;
                    if (_showing)
                    {
                        // 广告/标题切换即使已经全透明，也属于同一轮招揽。
                        // 只在首次展示或一次玩家会话结束后重新编排进场。
                        if (!_entranceStarted)
                        {
                            _entranceStarted = true;
                            _motionTime = _cfg.AnimatedEntrance && !_upperSeat && !_cfg.ReducedMotion ? 0f : EntranceMotion.Duration(_cfg);
                            _entrancePhase = EntrancePhase.Hidden;
                            _stage?.ResetForEntrance();
                            _stage?.SetSeated(_upperSeat);
                            _brain?.Reset();
                            _brain?.SetSeated(_upperSeat);
                        }
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

            // 卡顿不能把整段淡入/淡出压成一次跳变；反向时从当前透明度继续。
            float motionDt = Math.Max(0f, Math.Min(0.1f, dt));
            _alpha = MoveTowards(_alpha, target, speed * motionDt);
            // Relocate only while transparent; rapid choices can safely retarget.
            if (relocating && _alpha <= 0.002f)
            {
                _overlay.SetAlpha(0f);
                _upperSeat = wantUpper;
                _overlay.SetUpperSeat(_upperSeat);
                _stage?.ResetForEntrance();
                _stage?.SetSeated(_upperSeat);
                _brain?.Reset();
                _brain?.SetSeated(_upperSeat);
                _entranceStarted = false;
            }
            // 进场时间线用钳制过的 dt：卡顿一帧不该让角色瞬移半程
            if (_showing)
                _motionTime = Math.Min(EntranceMotion.Duration(_cfg), _motionTime + motionDt);
            // 隐藏只冻结空间并淡出，不倒放 Reveal，不把角色拉回圆外。
            UpdateEntrancePerformance();
            if (!_upperSeat) _stage?.SampleEntrance(_motionTime, _cfg);
            _overlay.SetEntrance(_motionTime, _cfg);

            _overlay.SetAlpha(_alpha * _cfg.Opacity);
            _overlay.SetVisible(_alpha > 0.002f);

            if (_alpha > 0.002f)
            {
                // 入场期间只播编排好的 Call / Enter，随机待机不能抢动作。
                if (_showing && _entrancePhase == EntrancePhase.Inside) _brain?.Tick(motionDt);
                _stage?.Tick(motionDt);
            }
            if (!_showing && _alpha <= 0.002f && mustHide)
            {
                _entranceStarted = false;
                _entrancePhase = EntrancePhase.Hidden;
                _motionTime = 0f;
            }
        }

        private void UpdateEntrancePerformance()
        {
            if (!_showing) return;
            EntrancePhase phase = EntranceMotion.PhaseAt(_motionTime, _cfg);
            if (phase == _entrancePhase) return;
            _entrancePhase = phase;
            _stage?.PlayEntrancePhase(phase);
            if (phase == EntrancePhase.Inside) { _brain?.Reset(); _brain?.SetSeated(_upperSeat); }
        }

        private static float MoveTowards(float cur, float target, float maxDelta)
        {
            if (Math.Abs(target - cur) <= maxDelta) return target;
            return cur + Math.Sign(target - cur) * maxDelta;
        }
    }
}
