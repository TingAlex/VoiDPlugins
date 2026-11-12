using System;
using System.Numerics;
using System.Threading;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace VoiDPlugins.Filter
{
    public partial class PrecisionControl
    {
        // Retained experimental page-pan implementation, hidden from OTD's
        // property loader/UI. EnablePenPageNavigation is also required in code.
        public bool EnablePenDoubleTapPagePan { get; set; }

        public float PenDoubleTapIntervalMilliseconds { get; set; } = 300f;

        public float PenPageJitterCoefficient { get; set; } = 2f;

        private enum PenContactNavigationPhase { Idle, ButtonHeld, WaitForRelease }
        private PenContactNavigationPhase _penContactPhase;
        private bool _penContactPageMode, _penContactTapPending, _penContactSecondPress;
        private bool _penContactButtonWasDown = true, _penContactUsed, _penContactMappingDragging;
        private long _penContactPressedAt, _penContactTapDeadline, _penContactLastTime;
        private float _penContactHoldTime;
        private IntPtr _penContactWindow, _penContactTapWindow, _penContactPageWindow;
        private Vector2 _penContactPanOrigin;
        private double _penContactPanGain;
        private readonly PenPageJitterGuard _penPageJitterGuard = new();
        private bool _penContactStrokeActive, _penContactContextPaused;
        private IDisposable? _penContactSettleTimer;
        private long _penContactSettleId, _penContactSettleDeadline;
        internal Func<Action, IDisposable>? PenPageSettleScheduler { get; set; }

        internal bool IsPenPageDragMode => _penContactPageMode;
        private bool PenContactNavigationCaptured => _penContactPageMode || _penContactTapPending ||
            _penContactPhase != PenContactNavigationPhase.Idle;

        // Called under _stateLock. Physical pressure remains in _isWriting;
        // only the forwarded report is zeroed, before any downstream tip binding.
        private bool HandlePenContactNavigation(ITabletReport report, Vector2 position, bool wasWriting)
        {
            if (_penDragPhase != PenDragPhase.Idle || _penNavigationPhase != PenNavigationPhase.Idle)
            {
                EndDirectPan();
                _penDragPhase = PenDragPhase.Idle;
                _penNavigationPhase = PenNavigationPhase.Idle;
                _penContactPhase = PenContactNavigationPhase.WaitForRelease;
            }

            var hasButton = report.PenButtons != null && report.PenButtons.Length >= 3;
            var down = hasButton && report.PenButtons![2];
            var pressed = down && !_penContactButtonWasDown;
            _penContactButtonWasDown = !hasButton || down;
            if (hasButton)
            {
                var forwarded = (bool[])report.PenButtons!.Clone();
                forwarded[2] = false;
                report.PenButtons = forwarded;
            }

            var now = PenNavigationNow;
            if (_penContactSettleTimer != null && now >= _penContactSettleDeadline)
                FinishPenPageSettle();
            var window = PenNavigationCurrentContext;
            var enabled = EnablePenNavigation && EnablePenPageNavigation && EnablePenDoubleTapPagePan;
            var valid = float.IsFinite(position.X) && float.IsFinite(position.Y) &&
                float.IsFinite(PenScrollSensitivity) && PenScrollSensitivity >= 0.1f && PenScrollSensitivity <= 5 &&
                float.IsFinite(PenNavigationHoldMilliseconds) && PenNavigationHoldMilliseconds >= 150 && PenNavigationHoldMilliseconds <= 800 &&
                float.IsFinite(PenDoubleTapIntervalMilliseconds) && PenDoubleTapIntervalMilliseconds >= 150 && PenDoubleTapIntervalMilliseconds <= 600 &&
                float.IsFinite(PenPageJitterCoefficient) && PenPageJitterCoefficient >= 0 && PenPageJitterCoefficient <= 10;
            var contextChanged = (_penContactTapPending && window != _penContactTapWindow) ||
                (_penContactPhase == PenContactNavigationPhase.ButtonHeld && window != _penContactWindow);
            if (PenContactNavigationCaptured && (!enabled || !valid || !hasButton ||
                window == IntPtr.Zero || contextChanged || now < _penContactLastTime))
            {
                var reason = !enabled ? "feature-disabled" : !valid ? "invalid-settings-or-position" :
                    !hasButton ? "missing-button-report" : contextChanged ? "foreground-changed" : "invalid-context-or-clock";
                if (enabled && _penContactPageMode) PausePenContactStroke(reason);
                else CancelPenContactNavigation(reason);
            }
            _penContactLastTime = now;

            if (_penContactPhase == PenContactNavigationPhase.WaitForRelease)
            {
                report.Pressure = 0;
                if (!down && !_isWriting && hasButton && _penContactSettleTimer == null)
                    _penContactPhase = PenContactNavigationPhase.Idle;
                return true;
            }
            if (!enabled || !valid || !hasButton)
                return false;

            // A pending single may resolve on a hover report, or immediately BEFORE
            // the first tip-down report is forwarded. Never apply it during a stroke.
            if (_penContactTapPending && (now >= _penContactTapDeadline || _isWriting))
            {
                if (wasWriting || (down && _isWriting))
                    _penContactTapPending = false;
                else
                    CompletePenContactSingle(position);
            }

            if (_penContactPhase == PenContactNavigationPhase.Idle && down)
            {
                if (wasWriting)
                {
                    _penContactTapPending = false;
                    if (!_penContactPageMode)
                        return false; // keep an already-started ordinary ink stroke
                    EndDirectPan();
                    _penContactPhase = PenContactNavigationPhase.WaitForRelease;
                    report.Pressure = 0;
                    return true;
                }
                PinPenContactCursor(position);
                if (!pressed || window == IntPtr.Zero)
                {
                    _penContactTapPending = false;
                    _penContactPhase = PenContactNavigationPhase.WaitForRelease;
                    report.Pressure = 0;
                    return true;
                }
                if (!_penContactPageMode) EndDirectPan();
                _penContactSecondPress = _penContactTapPending && now < _penContactTapDeadline;
                _penContactTapPending = false;
                _penContactPhase = PenContactNavigationPhase.ButtonHeld;
                _penContactPressedAt = now;
                _penContactHoldTime = PenNavigationHoldMilliseconds;
                _penContactWindow = window;
                _penContactUsed = false;
                _penContactMappingDragging = false;
            }

            if (_penContactPhase == PenContactNavigationPhase.ButtonHeld)
            {
                report.Pressure = 0;
                if (!down)
                {
                    var tap = !_penContactUsed && !_isWriting && now - _penContactPressedAt < _penContactHoldTime;
                    _penContactPhase = _isWriting ? PenContactNavigationPhase.WaitForRelease : PenContactNavigationPhase.Idle;
                    if (tap && _penContactSecondPress)
                    {
                        _penContactSecondPress = false;
                        _penContactPageMode = !_penContactPageMode;
                        _penContactPageWindow = _penContactPageMode ? window : IntPtr.Zero;
                        if (!_penContactPageMode && _penContactSettleTimer != null)
                            _penContactPhase = PenContactNavigationPhase.WaitForRelease;
                        else
                            EndDirectPan();
                        Log.Write(nameof(PrecisionControl), $"Pen page mode: {(_penContactPageMode ? "enabled" : "disabled")}; reason=double-tap; mapping unchanged.", LogLevel.Info);
                    }
                    else if (tap)
                    {
                        _penContactTapPending = true;
                        _penContactTapDeadline = now + (long)PenDoubleTapIntervalMilliseconds;
                        _penContactTapWindow = window;
                    }
                    else
                        _penContactSecondPress = false;
                    _penContactMappingDragging = false;
                    return true;
                }

                if (_isWriting)
                {
                    _penContactUsed = true;
                    // Page mode requires released buttons; normal mode retains the
                    // existing hold-button + contact mapping drag, including clutching.
                    if (!_penContactPageMode && EnablePenDrag && _isActive &&
                        float.IsFinite(PenDragSensitivity) && PenDragSensitivity >= 0.1f && PenDragSensitivity <= 5)
                    {
                        if (!_penContactMappingDragging)
                        {
                            _penDragStartPosition = position;
                            _penDragStartBounds = _precisionBounds;
                            _penDragSensitivity = PenDragSensitivity;
                            _penContactMappingDragging = true;
                        }
                        else if (!MovePenDrag(position))
                            CancelPenContactNavigation("invalid-mapping-displacement");
                    }
                }
                else
                    _penContactMappingDragging = false;
                // Long hover holds deliberately do nothing in this interaction.
                return true;
            }

            if (!_penContactPageMode)
                return false;
            report.Pressure = 0;
            if (window != _penContactPageWindow)
            {
                if (!_penContactContextPaused)
                {
                    PausePenContactStroke("foreground-changed");
                    _penContactContextPaused = true;
                }
                if (_isWriting) PinPenContactCursor(position);
                return _isWriting;
            }
            _penContactContextPaused = false;
            if (!_isWriting)
            {
                if (_penContactStrokeActive)
                {
                    _penContactStrokeActive = false;
                    StartPenPageSettle();
                }
                return false;   // hovering may position the cursor for the next contact
            }

            _penContactTapPending = false;
            if (!_penContactStrokeActive)
            {
                CancelPenPageSettle();
                PinPenContactCursor(position);
                _penContactPanOrigin = position;
                _penContactPanGain = PenScrollSensitivity * (PenScrollNaturalDirection ? 1d : -1d);
                _penPageJitterGuard.Reset(PenPageJitterCoefficient);
                if (_penDirectPan != null)
                {
                    _penDirectPan.Rebase();
                    _penContactStrokeActive = true;
                }
                else
                {
                    _penDirectPan = PenDirectPanFactory?.Invoke() ?? new PenDirectPan();
                    if (!_penDirectPan.Begin(_penContactPageWindow, MapPenPosition(position)))
                    {
                        Log.Write(nameof(PrecisionControl), $"Contact page pan could not start: {_penDirectPan.Error}. Mode retained; lift to retry. No wheel fallback.", LogLevel.Warning);
                        PausePenContactStroke("direct-pan-start-failed");
                    }
                    else
                        _penContactStrokeActive = true;
                }
            }
            else
            {
                var filtered = _penPageJitterGuard.TryFilter(
                    ((double)position.X - _penContactPanOrigin.X) * _penContactPanGain,
                    ((double)position.Y - _penContactPanOrigin.Y) * _penContactPanGain, out var displacement);
                if (_penDirectPan == null || !filtered ||
                    (_penPageJitterGuard.Changed && !_penDirectPan.Move(displacement.X, displacement.Y)))
                {
                    Log.Write(nameof(PrecisionControl), $"Contact page pan stopped: {_penDirectPan?.Error ?? "invalid-filtered-displacement"}. Mode retained; lift to retry. No wheel fallback.", LogLevel.Warning);
                    PausePenContactStroke("direct-pan-update-failed");
                }
            }
            return true;
        }

        private void PinPenContactCursor(Vector2 position) =>
            _penDragCursorPosition = _hasLastPenCursorPosition ? _lastPenCursorPosition : MapPenPosition(position);

        private void CompletePenContactSingle(Vector2 position)
        {
            _penContactTapPending = false;
            if (!_penContactPageMode)
            {
                ApplyAction(PrecisionControlAction.Toggle, position);
                Log.Write(nameof(PrecisionControl), $"Pen single tap: mapping={(_isActive ? "enabled" : "disabled")}; double-tap interval elapsed or tip-down.", LogLevel.Info);
            }
        }

        private void CancelPenContactNavigation(string reason)
        {
            if (!PenContactNavigationCaptured)
                return;
            EndDirectPan();
            if (_penContactPageMode)
                Log.Write(nameof(PrecisionControl), $"Pen page mode: disabled; reason={reason}; mapping unchanged.", LogLevel.Info);
            _penContactPageMode = _penContactTapPending = _penContactSecondPress = _penContactMappingDragging = false;
            _penContactPhase = PenContactNavigationPhase.WaitForRelease;
        }

        private void PausePenContactStroke(string reason)
        {
            EndDirectPan();
            _penContactTapPending = _penContactSecondPress = _penContactMappingDragging = false;
            _penContactPhase = PenContactNavigationPhase.WaitForRelease;
            Log.Write(nameof(PrecisionControl), $"Pen page mode paused; reason={reason}; mode retained, ink remains suppressed. Lift to retry or double-tap to exit.", LogLevel.Warning);
        }

        private void CancelPenPageSettle()
        {
            _penContactSettleId++;
            var timer = _penContactSettleTimer;
            _penContactSettleTimer = null;
            timer?.Dispose();
        }

        private void StartPenPageSettle()
        {
            if (_penDirectPan == null || _penContactSettleTimer != null) return;
            if (!_penDirectPan.Hold()) { PausePenContactStroke("direct-pan-hold-failed"); return; }
            var pan = _penDirectPan;
            var generation = ++_penContactSettleId;
            _penContactSettleDeadline = PenNavigationNow + 120;
            var ticks = 0;
            Action tick = () =>
            {
                lock (_stateLock)
                {
                    if (generation != _penContactSettleId || !ReferenceEquals(pan, _penDirectPan)) return;
                    try
                    {
                        // Do not inject an UPDATE and UP in the same callback:
                        // Windows may reject frames that are too closely spaced.
                        if (++ticks >= 3) EndDirectPan();
                        else if (!pan.Hold()) PausePenContactStroke("direct-pan-hold-failed");
                    }
                    catch (Exception ex)
                    {
                        PausePenContactStroke("direct-pan-settle-exception");
                        Log.Write(nameof(PrecisionControl), ex.Message, LogLevel.Warning);
                    }
                }
            };
            // Stationary frames at 0/40/80 ms, release at 120; no queued motion.
            _penContactSettleTimer = PenPageSettleScheduler?.Invoke(tick) ??
                new Timer(_ => tick(), null, 40, 40);
        }

        private void FinishPenPageSettle()
        {
            EndDirectPan();
        }

        private void ResetPenContactNavigation(bool preservePageMode)
        {
            // Leaving pen sensing range is also a way to clutch. End the current
            // contacts and tap candidate, but do not silently invert the user's
            // next explicit double-tap from "exit" into "enter".
            if (preservePageMode && _penContactPageMode && EnablePenNavigation && EnablePenPageNavigation && EnablePenDoubleTapPagePan)
            {
                _penContactStrokeActive = false;
                if (PenNavigationCurrentContext == _penContactPageWindow) StartPenPageSettle();
                else EndDirectPan();
                _penContactTapPending = _penContactSecondPress = _penContactMappingDragging = false;
            }
            else
                CancelPenContactNavigation("out-of-range-or-reset");
            _penContactPhase = PenContactNavigationPhase.Idle;
            _penContactButtonWasDown = true;
            _penContactLastTime = 0;
        }
    }
}
