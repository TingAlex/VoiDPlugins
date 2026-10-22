using System;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;

namespace VoiDPlugins.Filter
{
    public partial class PrecisionControl
    {

        private enum PenDragPhase { Idle, Armed, Dragging, WaitForLift, WaitForRelease }

        private PenDragPhase _penDragPhase;
        // Require a released-button report on startup/re-entry. Never resume a held gesture.
        private bool _penDragButtonWasDown = true;
        private bool _penDragMissingButtonWarning;
        private Vector2 _penDragCursorPosition;
        private Vector2 _penDragStartPosition;
        private OverlayBounds _penDragStartBounds;
        private float _penDragSensitivity;

        private bool PenDragBlocksMovement => _penDragPhase != PenDragPhase.Idle ||
            _penNavigationPhase != PenNavigationPhase.Idle || PenContactNavigationCaptured;

        private Vector2 MapPenPosition(Vector2 position)
        {
            if (!_isActive)
                return position;
            return _pointerRelativeActive
                ? PrecisionPositionCalculator.MapOutputAreaToBounds(position, _outputArea, _precisionBounds)
                : _activationAnchor + ((position - _startingPoint) * Scale);
        }

        // Called under _stateLock, before downstream bindings and Windows Ink output.
        // _isWriting retains PHYSICAL pressure so existing movement guards stay intact.
        private bool HandlePenDrag(ITabletReport report, Vector2 position, bool wasWriting)
        {
            var buttons = report.PenButtons;
            var hasButton = buttons != null && buttons.Length >= 3;
            var buttonDown = hasButton && buttons![2];
            var pressed = buttonDown && !_penDragButtonWasDown;
            _penDragButtonWasDown = !hasButton || buttonDown;

            if ((EnablePenDrag || PenDragBlocksMovement) && hasButton)
            {
                // Don't modify arrays shared with the parser or another consumer.
                var forwardedButtons = (bool[])buttons!.Clone();
                forwardedButtons[2] = false;
                report.PenButtons = forwardedButtons;
            }
            else if (EnablePenDrag && _isActive && !_penDragMissingButtonWarning)
            {
                _penDragMissingButtonWarning = true;
                Log.Write(nameof(PrecisionControl),
                    "Pen drag unavailable: the current report has no third pen button. Check the pen/report type; no other button will be substituted.",
                    LogLevel.Warning);
            }

            var validPosition = float.IsFinite(position.X) && float.IsFinite(position.Y);
            var validSensitivity = float.IsFinite(PenDragSensitivity) &&
                PenDragSensitivity >= 0.1f && PenDragSensitivity <= 5f;
            var canDrag = EnablePenDrag && _isActive && hasButton &&
                validPosition && validSensitivity;
            if (!canDrag)
                CancelPenDrag();

            if (_penDragPhase == PenDragPhase.WaitForRelease)
            {
                report.Pressure = 0;
                if (!buttonDown && !_isWriting)
                    _penDragPhase = PenDragPhase.Idle;
                return true;
            }

            if (_penDragPhase == PenDragPhase.WaitForLift)
            {
                report.Pressure = 0;
                if (!_isWriting)
                    _penDragPhase = PenDragPhase.Idle;
                return true;
            }

            if (_penDragPhase == PenDragPhase.Idle)
            {
                // A held button on re-entry is neither a new drag nor permission to ink.
                // Finish an already-running stroke, then require button release and lift.
                if (canDrag && buttonDown && !pressed && !wasWriting)
                {
                    _penDragCursorPosition = _hasLastPenCursorPosition
                        ? _lastPenCursorPosition : MapPenPosition(position);
                    _penDragPhase = PenDragPhase.WaitForRelease;
                    report.Pressure = 0;
                    return true;
                }

                // Don't steal an existing stroke or a button already held when enabled.
                if (!canDrag || !pressed || wasWriting)
                    return false;

                _penDragPhase = PenDragPhase.Armed;
                _penDragCursorPosition = _hasLastPenCursorPosition
                    ? _lastPenCursorPosition : MapPenPosition(position);
                _penDragSensitivity = PenDragSensitivity;
            }

            // Even the first contact/release frame must reach bindings as tip-up.
            report.Pressure = 0;
            if (!buttonDown)
            {
                _penDragPhase = _isWriting ? PenDragPhase.WaitForLift : PenDragPhase.Idle;
                return true;
            }

            if (!_isWriting)
            {
                _penDragPhase = PenDragPhase.Armed;
                return true;
            }

            if (_penDragPhase == PenDragPhase.Armed)
            {
                _penDragStartPosition = position;
                _penDragStartBounds = _precisionBounds;
                _penDragPhase = PenDragPhase.Dragging;
                return true;
            }

            if (!MovePenDrag(position))
                CancelPenDrag();
            return true;
        }

        private bool MovePenDrag(Vector2 position)
        {
            // Round the total displacement from this contact's origin, not each frame.
            // This preserves subpixel movements and makes frame rate irrelevant.
            var targetX = _penDragStartBounds.Left +
                Math.Round(((double)position.X - _penDragStartPosition.X) * _penDragSensitivity);
            var targetY = _penDragStartBounds.Top +
                Math.Round(((double)position.Y - _penDragStartPosition.Y) * _penDragSensitivity);
            var deltaX = targetX - _precisionBounds.Left;
            var deltaY = targetY - _precisionBounds.Top;
            if (targetX < int.MinValue || targetX > int.MaxValue - (double)_precisionBounds.Width ||
                targetY < int.MinValue || targetY > int.MaxValue - (double)_precisionBounds.Height ||
                deltaX < int.MinValue || deltaX > int.MaxValue ||
                deltaY < int.MinValue || deltaY > int.MaxValue)
                return false;

            if (deltaX != 0 || deltaY != 0)
                TranslateArea((int)deltaX, (int)deltaY);
            return true;
        }

        private void CancelPenDrag()
        {
            CancelPenContactNavigation("mapping-action-or-cancel");
            EndDirectPan();
            if (_penDragPhase is PenDragPhase.Armed or PenDragPhase.Dragging)
                _penDragPhase = PenDragPhase.WaitForLift;
            if (_penNavigationPhase is PenNavigationPhase.Pending or
                PenNavigationPhase.ContactDragging or PenNavigationPhase.HoverScrolling)
                _penNavigationPhase = PenNavigationPhase.WaitForRelease;
        }

        private void ResetPenDrag(bool preservePageMode = false)
        {
            ResetPenContactNavigation(preservePageMode);
            if (!_penContactPageMode) EndDirectPan();
            if (_penNavigationPhase != PenNavigationPhase.Idle)
                LogPenNavigationResult(PenNavigationNow, false, "out-of-range-or-reset");
            _penDragPhase = PenDragPhase.Idle;
            _penDragButtonWasDown = true;
            _penNavigationPhase = PenNavigationPhase.Idle;
            _penNavigationButtonWasDown = true;
            _penScrollRemainderX = _penScrollRemainderY = 0;
        }
    }
}
