using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;

namespace VoiDPlugins.Filter
{
    public partial class PrecisionControl
    {
        // Source-only experimental gate. Neither stale JSON nor the generated
        // settings page can enable page injection in the normal distribution.
        internal bool EnablePenPageNavigation { get; set; }

        // Retained page-navigation parameters; intentionally not OTD properties.
        public float PenScrollSensitivity { get; set; } = 1f;

        public bool PenScrollNaturalDirection { get; set; } = true;

        public const string WheelScrollMode = "Wheel (Legacy)";
        public const string DirectTouchPanMode = "Direct Touch Pan";
        public static string[] ValidPenScrollModes => new[] { WheelScrollMode, DirectTouchPanMode };

        public string PenScrollMode { get; set; } = WheelScrollMode;

        private enum PenNavigationPhase { Idle, Pending, ContactDragging, HoverScrolling, WaitForRelease }
        private PenNavigationPhase _penNavigationPhase;
        private bool _penNavigationButtonWasDown = true;
        private bool _penNavigationUsed;
        private long _penNavigationPressedAt;
        private float _penNavigationHoldTime;
        private Vector2 _penNavigationPressPosition;
        private float _penNavigationMaxDistanceSquared;
        private bool _penNavigationHadContact;
        private string? _penNavigationCancelReason;
        private Vector2 _penScrollLastPosition;
        private double _penScrollRemainderX, _penScrollRemainderY;
        private IntPtr _penNavigationWindow;
        private Vector2 _penDirectPanOrigin;
        private IPenDirectPan? _penDirectPan;
        internal Func<IPenDirectPan>? PenDirectPanFactory { get; set; }
        internal Func<long>? PenNavigationClock { get; set; }
        internal Func<IntPtr>? PenNavigationContext { get; set; }
        internal Func<int, int, IntPtr, bool>? PenScrollSender { get; set; }

        private long PenNavigationNow => PenNavigationClock?.Invoke() ??
            (long)(Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency));
        private IntPtr PenNavigationCurrentContext => PenNavigationContext?.Invoke() ??
            PenPageScroller.CurrentWindow;

        private bool HandlePenNavigation(ITabletReport report, Vector2 position, bool wasWriting)
        {
            if ((EnablePenNavigation && EnablePenPageNavigation && EnablePenDoubleTapPagePan) || PenContactNavigationCaptured)
                return HandlePenContactNavigation(report, position, wasWriting);
            if (_penDragPhase != PenDragPhase.Idle)
            {
                // Switching modes during a captured legacy drag must not strand its lock.
                _penDragPhase = PenDragPhase.Idle;
                _penDragButtonWasDown = true;
                _penNavigationPhase = PenNavigationPhase.WaitForRelease;
            }
            var hasButton = report.PenButtons != null && report.PenButtons.Length >= 3;
            var down = hasButton && report.PenButtons![2];
            var pressed = down && !_penNavigationButtonWasDown;
            _penNavigationButtonWasDown = !hasButton || down;
            if (hasButton)
            {
                var forwarded = (bool[])report.PenButtons!.Clone();
                forwarded[2] = false;
                report.PenButtons = forwarded;
            }
            else if (!_penDragMissingButtonWarning)
            {
                _penDragMissingButtonWarning = true;
                Log.Write(nameof(PrecisionControl), "Pen navigation unavailable: report has no third pen button.", LogLevel.Warning);
            }

            var now = PenNavigationNow;
            var finite = float.IsFinite(position.X) && float.IsFinite(position.Y);
            var validSettings = float.IsFinite(PenNavigationHoldMilliseconds) &&
                PenNavigationHoldMilliseconds >= 150 && PenNavigationHoldMilliseconds <= 800 &&
                float.IsFinite(PenScrollSensitivity) && PenScrollSensitivity >= 0.1f && PenScrollSensitivity <= 5;

            if (_penNavigationPhase is not (PenNavigationPhase.Idle or PenNavigationPhase.WaitForRelease) &&
                (!EnablePenNavigation || !hasButton || !finite || !validSettings ||
                 now < _penNavigationPressedAt || PenNavigationCurrentContext != _penNavigationWindow))
            {
                _penNavigationCancelReason = !EnablePenNavigation ? "feature-disabled" :
                    !hasButton ? "missing-button-report" : !finite ? "invalid-position" :
                    !validSettings ? "invalid-settings" : now < _penNavigationPressedAt ?
                    "clock-reset" : "foreground-changed";
                _penNavigationPhase = PenNavigationPhase.WaitForRelease;
            }

            if (_penNavigationPhase == PenNavigationPhase.WaitForRelease)
            {
                EndDirectPan();
                report.Pressure = 0;
                _penScrollRemainderX = _penScrollRemainderY = 0;
                if (!down && !_isWriting)
                {
                    LogPenNavigationResult(now, false, _penNavigationCancelReason ?? "cancelled");
                    _penNavigationPhase = PenNavigationPhase.Idle;
                }
                return true;
            }

            if (_penNavigationPhase == PenNavigationPhase.Idle)
            {
                if (!EnablePenNavigation || !hasButton || !finite || !validSettings || !down || wasWriting)
                    return false;
                _penDragCursorPosition = _hasLastPenCursorPosition
                    ? _lastPenCursorPosition : MapPenPosition(position);
                _penNavigationWindow = PenNavigationCurrentContext;
                if (!pressed || _penNavigationWindow == IntPtr.Zero)
                {
                    _penNavigationPressedAt = now;
                    _penNavigationCancelReason = !pressed ? "button-already-held" : "no-foreground-window";
                    _penNavigationPhase = PenNavigationPhase.WaitForRelease;
                    report.Pressure = 0;
                    return true;
                }
                _penNavigationPhase = PenNavigationPhase.Pending;
                _penNavigationPressedAt = now;
                _penNavigationHoldTime = PenNavigationHoldMilliseconds;
                _penNavigationPressPosition = position;
                _penNavigationUsed = false;
                _penNavigationHadContact = false;
                _penNavigationMaxDistanceSquared = 0;
                _penNavigationCancelReason = null;
                _penScrollRemainderX = _penScrollRemainderY = 0;
            }

            report.Pressure = 0;
            var elapsed = now - _penNavigationPressedAt;
            // Hover jitter is not a drag. At Scale=.25, 12 original output pixels
            // are only 3 mapped pixels; using that as a veto made closing unreliable.
            // Time separates a hover tap from scrolling; contact separates tip dragging.
            _penNavigationMaxDistanceSquared = Math.Max(_penNavigationMaxDistanceSquared,
                Vector2.DistanceSquared(position, _penNavigationPressPosition));

            if (!down)
            {
                EndDirectPan();
                var tap = !_penNavigationUsed && !_isWriting && elapsed < _penNavigationHoldTime;
                _penNavigationPhase = _isWriting ? PenNavigationPhase.WaitForRelease : PenNavigationPhase.Idle;
                _penScrollRemainderX = _penScrollRemainderY = 0;
                if (tap)
                    ApplyAction(PrecisionControlAction.Toggle, position);
                LogPenNavigationResult(now, tap, tap ? "short-tap" :
                    _isWriting || _penNavigationHadContact ? "tip-contact" : "long-press");
                return true;
            }

            if (_isWriting)
            {
                EndDirectPan();
                _penNavigationUsed = true;
                _penNavigationHadContact = true;
                _penScrollRemainderX = _penScrollRemainderY = 0;
                if (!EnablePenDrag || !_isActive || !float.IsFinite(PenDragSensitivity) ||
                    PenDragSensitivity < 0.1f || PenDragSensitivity > 5)
                {
                    _penNavigationPhase = PenNavigationPhase.Pending;
                    return true;
                }
                if (_penNavigationPhase != PenNavigationPhase.ContactDragging)
                {
                    _penDragStartPosition = position;
                    _penDragStartBounds = _precisionBounds;
                    _penDragSensitivity = PenDragSensitivity;
                    _penNavigationPhase = PenNavigationPhase.ContactDragging;
                }
                else if (!MovePenDrag(position))
                {
                    _penNavigationCancelReason = "invalid-contact-displacement";
                    _penNavigationPhase = PenNavigationPhase.WaitForRelease;
                }
                return true;
            }

            if (_penNavigationPhase == PenNavigationPhase.ContactDragging)
                _penNavigationPhase = PenNavigationPhase.Pending;
            if (elapsed < _penNavigationHoldTime)
                return true;

            _penNavigationUsed = true;
            if (!EnablePenPageNavigation)
            {
                EndDirectPan();
                return true; // a long hover hold never injects page input
            }
            if (_penNavigationPhase != PenNavigationPhase.HoverScrolling)
            {
                // Do not replay movement made before long-press or while touching.
                _penNavigationPhase = PenNavigationPhase.HoverScrolling;
                _penScrollLastPosition = position;
                _penDirectPanOrigin = position;
                _penScrollRemainderX = _penScrollRemainderY = 0;
                if (PenScrollMode == DirectTouchPanMode)
                {
                    _penDirectPan = PenDirectPanFactory?.Invoke() ?? new PenDirectPan();
                    if (!_penDirectPan.Begin(_penNavigationWindow, _penDragCursorPosition))
                    {
                        Log.Write(nameof(PrecisionControl), $"Direct page pan could not start: {_penDirectPan.Error}. No wheel fallback.", LogLevel.Warning);
                        _penNavigationCancelReason = "direct-pan-start-failed";
                        _penNavigationPhase = PenNavigationPhase.WaitForRelease;
                        EndDirectPan();
                    }
                }
                return true;
            }

            if (PenScrollMode == DirectTouchPanMode)
            {
                var sign = PenScrollNaturalDirection ? 1d : -1d;
                var x = ((double)position.X - _penDirectPanOrigin.X) * PenScrollSensitivity * sign;
                var y = ((double)position.Y - _penDirectPanOrigin.Y) * PenScrollSensitivity * sign;
                if (_penDirectPan == null || !_penDirectPan.Move(x, y))
                {
                    Log.Write(nameof(PrecisionControl), $"Direct page pan stopped: {_penDirectPan?.Error ?? "session-missing"}. Release button 3 to retry; no wheel fallback.", LogLevel.Warning);
                    _penNavigationCancelReason = "direct-pan-update-failed";
                    _penNavigationPhase = PenNavigationPhase.WaitForRelease;
                    EndDirectPan();
                }
                return true;
            }
            EndDirectPan();

            var direction = PenScrollNaturalDirection ? 1d : -1d;
            var dx = ((double)position.X - _penScrollLastPosition.X) * PenScrollSensitivity * 3d;
            var dy = ((double)position.Y - _penScrollLastPosition.Y) * PenScrollSensitivity * 3d;
            _penScrollLastPosition = position;
            // Bound one report; never turn invalid/outlier input into a scroll backlog.
            if (Math.Abs(dx) > 1200 || Math.Abs(dy) > 1200)
            {
                _penNavigationCancelReason = "hover-outlier";
                _penNavigationPhase = PenNavigationPhase.WaitForRelease;
                return true;
            }
            _penScrollRemainderX += -dx * direction;
            _penScrollRemainderY += dy * direction;
            var horizontal = (int)Math.Truncate(_penScrollRemainderX);
            var vertical = (int)Math.Truncate(_penScrollRemainderY);
            if (horizontal == 0 && vertical == 0)
                return true;
            _penScrollRemainderX -= horizontal;
            _penScrollRemainderY -= vertical;
            var sent = PenScrollSender?.Invoke(horizontal, vertical, _penNavigationWindow) ??
                PenPageScroller.TryScroll(horizontal, vertical, _penNavigationWindow);
            if (!sent)
            {
                Log.Write(nameof(PrecisionControl), "Page scrolling stopped: input was blocked, modified, or the target window changed. Release button 3 to retry.", LogLevel.Warning);
                _penNavigationCancelReason = "scroll-send-blocked";
                _penNavigationPhase = PenNavigationPhase.WaitForRelease;
                _penScrollRemainderX = _penScrollRemainderY = 0;
            }
            return true;
        }

        private void LogPenNavigationResult(long now, bool toggled, string reason)
        {
            Log.Write(nameof(PrecisionControl),
                $"Pen button gesture: toggle={toggled}, mapping={(_isActive ? "enabled" : "disabled")}, reason={reason}, held={Math.Max(0, now - _penNavigationPressedAt)}ms, hover-drift={Math.Sqrt(_penNavigationMaxDistanceSquared):F1}px.",
                LogLevel.Info);
        }

        private void EndDirectPan()
        {
            CancelPenPageSettle();
            _penContactStrokeActive = false;
            var pan = _penDirectPan;
            _penDirectPan = null;
            pan?.End();
            if (pan?.Error?.StartsWith("touch-cancel", StringComparison.Ordinal) == true)
                Log.Write(nameof(PrecisionControl), $"Direct page contact cancellation failed: {pan.Error}. Device cleanup was attempted; page-mode ink interlock is unchanged.", LogLevel.Warning);
        }
    }
}
