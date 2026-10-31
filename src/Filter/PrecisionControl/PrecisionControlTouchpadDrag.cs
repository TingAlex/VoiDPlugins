using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Plugin;

namespace VoiDPlugins.Filter
{
    internal enum TouchpadDragPhase
    {
        Idle,
        Tracking,
        Dragging
    }

    internal enum TouchpadDragDirection
    {
        None,
        Horizontal,
        Vertical
    }

    // Three/four-finger touchpad drag that moves the real screen mapping rectangle.
    // The raw-input capture lives in TouchpadRawInputListener; this file only turns
    // contact frames into mapping translations, so it stays hardware-free and testable.
    public partial class PrecisionControl
    {
        private const long TouchpadReleaseGapMilliseconds = 40;
        private const float TouchpadStartTravelRatio = 0.01f;

        internal TouchpadDragPhase TouchpadPhase => _touchpadDragPhase;

        internal void HandleTouchpadFrame(TouchpadFrame frame)
        {
            if (frame is null)
                throw new ArgumentNullException(nameof(frame));

            lock (_stateLock)
                ApplyTouchpadFrame(frame);
        }

        private void ApplyTouchpadFrame(TouchpadFrame frame)
        {
            if (!EnableTouchpadDrag || !_isActive || _isWriting ||
                _penDragPhase != PenDragPhase.Idle)
            {
                EndTouchpadDrag();
                return;
            }

            var fingerCount = Math.Clamp(TouchpadDragFingers, 2, 5);
            var elapsed = _touchpadLastFrameTimestamp == long.MinValue
                ? long.MaxValue
                : frame.TimestampMilliseconds - _touchpadLastFrameTimestamp;
            _touchpadLastFrameTimestamp = frame.TimestampMilliseconds;

            var contacts = frame.Contacts;
            if (contacts.Count != fingerCount)
            {
                EndTouchpadDrag();
                _touchpadLastIds = Array.Empty<int>();
                return;
            }

            var ids = contacts.Select(contact => contact.ContactId).OrderBy(id => id).ToArray();
            var sameContacts = ids.Length == _touchpadLastIds.Length &&
                ids.SequenceEqual(_touchpadLastIds);
            _touchpadLastIds = ids;

            // A new finger set or a pause between frames starts a fresh gesture: the
            // existing rectangle stays where it is and the movement baseline is rebuilt.
            if (!sameContacts || elapsed > TouchpadReleaseGapMilliseconds)
            {
                EndTouchpadDrag();
                _touchpadDragPhase = TouchpadDragPhase.Tracking;
                _touchpadTrackingCentroid = Centroid(contacts);
                _touchpadTravel = 0;
                return;
            }

            var centroid = Centroid(contacts);
            if (_touchpadDragPhase == TouchpadDragPhase.Idle)
            {
                _touchpadDragPhase = TouchpadDragPhase.Tracking;
                _touchpadTrackingCentroid = centroid;
                _touchpadTravel = 0;
                return;
            }

            if (_touchpadDragPhase == TouchpadDragPhase.Tracking)
            {
                _touchpadTravel += Vector2.Distance(centroid, _touchpadTrackingCentroid);
                _touchpadTrackingCentroid = centroid;

                var threshold = Math.Max(
                    1f,
                    Math.Min(frame.HorizontalRange, frame.VerticalRange) * TouchpadStartTravelRatio);
                if (_touchpadTravel < threshold)
                    return;

                _touchpadDragPhase = TouchpadDragPhase.Dragging;
                _touchpadStartCentroid = centroid;
                _touchpadStartBounds = _precisionBounds;
                _touchpadTravel = 0;
                _touchpadAxis = TouchpadDragDirection.None;
                Log.Debug(
                    nameof(PrecisionControl),
                    $"Touchpad drag started with {fingerCount} contacts.");
                return;
            }

            var sensitivity = TouchpadDragSensitivity;
            if (!float.IsFinite(sensitivity) || sensitivity < 0.02f || sensitivity > 5f)
            {
                EndTouchpadDrag();
                return;
            }

            if (_outputArea.Width <= 0 || _outputArea.Height <= 0 ||
                frame.HorizontalRange <= 0 || frame.VerticalRange <= 0)
            {
                EndTouchpadDrag();
                return;
            }

            var gainX = (double)_outputArea.Width * sensitivity / frame.HorizontalRange;
            var gainY = (double)_outputArea.Height * sensitivity / frame.VerticalRange;

            // Axis policy works in raw device units, so a non-square display gain cannot
            // bias the decision towards the wider screen axis.
            var rawX = (double)centroid.X - _touchpadStartCentroid.X;
            var rawY = (double)centroid.Y - _touchpadStartCentroid.Y;
            var (allowX, allowY) = ResolveTouchpadDragAxis(rawX, rawY, centroid);
            var offsetX = allowX ? Math.Round(rawX * gainX) : 0d;
            var offsetY = allowY ? Math.Round(rawY * gainY) : 0d;

            // Optional detents: the rectangle moves in whole steps instead of following
            // the fingers continuously. 10% extra travel is required before a step flips,
            // so a finger resting on a step boundary cannot rattle between two cells.
            var step = float.IsFinite(TouchpadDragStepPixels)
                ? Math.Clamp((double)TouchpadDragStepPixels, 0d, 500d)
                : 0d;
            if (step > 0)
            {
                offsetX = QuantizeStep(ref _touchpadStepX, offsetX, step);
                offsetY = QuantizeStep(ref _touchpadStepY, offsetY, step);
            }

            var targetLeft = _touchpadStartBounds.Left + offsetX;
            var targetTop = _touchpadStartBounds.Top + offsetY;
            var deltaX = targetLeft - _precisionBounds.Left;
            var deltaY = targetTop - _precisionBounds.Top;

            if (!IsSafeTranslation(deltaX, deltaY) ||
                targetLeft < int.MinValue || targetLeft > int.MaxValue - (double)_precisionBounds.Width ||
                targetTop < int.MinValue || targetTop > int.MaxValue - (double)_precisionBounds.Height)
            {
                EndTouchpadDrag();
                return;
            }

            if (deltaX != 0 || deltaY != 0)
                TranslateArea((int)deltaX, (int)deltaY);
        }

        private (bool AllowX, bool AllowY) ResolveTouchpadDragAxis(
            double rawX,
            double rawY,
            Vector2 centroid)
        {
            var mode = TouchpadDragAxis;
            if (string.Equals(mode, TouchpadAxisHorizontal, StringComparison.Ordinal))
                return (true, false);

            if (string.Equals(mode, TouchpadAxisVertical, StringComparison.Ordinal))
                return (false, true);

            if (!string.Equals(mode, TouchpadAxisLock, StringComparison.Ordinal))
                return (true, true); // free diagonal (or an unknown value from an old profile)

            var lockRatio = float.IsFinite(TouchpadDragAxisLockRatio)
                ? Math.Clamp((double)TouchpadDragAxisLockRatio, 1d, 5d)
                : 1.5d;
            var releaseRatio = float.IsFinite(TouchpadDragAxisReleaseRatio)
                ? Math.Clamp((double)TouchpadDragAxisReleaseRatio, 1.2d, 6d)
                : 2.5d;

            var horizontal = Math.Abs(rawX);
            var vertical = Math.Abs(rawY);

            switch (_touchpadAxis)
            {
                case TouchpadDragDirection.None:
                    if (Math.Max(horizontal, vertical) < 1d)
                        return (false, false);
                    if (Math.Max(horizontal, vertical) >= Math.Min(horizontal, vertical) * lockRatio)
                    {
                        _touchpadAxis = horizontal >= vertical
                            ? TouchpadDragDirection.Horizontal
                            : TouchpadDragDirection.Vertical;
                        // Keep the dominant-axis displacement that proved the direction;
                        // drop only the minor-axis part so the axis starts clean.
                        return _touchpadAxis == TouchpadDragDirection.Horizontal
                            ? (true, false)
                            : (false, true);
                    }

                    return (true, true);

                case TouchpadDragDirection.Horizontal:
                    if (vertical > horizontal * releaseRatio)
                    {
                        _touchpadAxis = TouchpadDragDirection.Vertical;
                        RebaselineTouchpadDrag(centroid);
                        return (false, false);
                    }

                    return (true, false);

                case TouchpadDragDirection.Vertical:
                    if (horizontal > vertical * releaseRatio)
                    {
                        _touchpadAxis = TouchpadDragDirection.Horizontal;
                        RebaselineTouchpadDrag(centroid);
                        return (false, false);
                    }

                    return (false, true);

                default:
                    return (true, true);
            }
        }

        private void RebaselineTouchpadDrag(Vector2 centroid)
        {
            _touchpadStartCentroid = centroid;
            _touchpadStartBounds = _precisionBounds;
            _touchpadStepX = 0;
            _touchpadStepY = 0;
        }

        private static double QuantizeStep(ref int appliedSteps, double offset, double step)
        {
            var desired = (int)Math.Round(offset / step, MidpointRounding.AwayFromZero);
            if (desired > appliedSteps)
            {
                if (offset >= (appliedSteps * step) + (step * 0.6))
                    appliedSteps = desired;
            }
            else if (desired < appliedSteps)
            {
                if (offset <= (appliedSteps * step) - (step * 0.6))
                    appliedSteps = desired;
            }

            return appliedSteps * step;
        }

        private static bool IsSafeTranslation(double deltaX, double deltaY)
        {
            return double.IsFinite(deltaX) && double.IsFinite(deltaY) &&
                deltaX >= int.MinValue && deltaX <= int.MaxValue &&
                deltaY >= int.MinValue && deltaY <= int.MaxValue;
        }

        private static Vector2 Centroid(IReadOnlyList<TouchpadContact> contacts)
        {
            var sumX = 0d;
            var sumY = 0d;
            foreach (var contact in contacts)
            {
                sumX += contact.X;
                sumY += contact.Y;
            }

            return new Vector2(
                (float)(sumX / contacts.Count),
                (float)(sumY / contacts.Count));
        }

        internal void EndTouchpadDrag()
        {
            if (_touchpadDragPhase == TouchpadDragPhase.Dragging)
                Log.Debug(nameof(PrecisionControl), "Touchpad drag ended.");
            _touchpadDragPhase = TouchpadDragPhase.Idle;
            _touchpadTravel = 0;
            _touchpadAxis = TouchpadDragDirection.None;
            _touchpadStepX = 0;
            _touchpadStepY = 0;
        }

        private void ResetTouchpadDrag()
        {
            EndTouchpadDrag();
            _touchpadLastIds = Array.Empty<int>();
        }

        private TouchpadDragPhase _touchpadDragPhase;
        private long _touchpadLastFrameTimestamp = long.MinValue;
        private Vector2 _touchpadTrackingCentroid;
        private Vector2 _touchpadStartCentroid;
        private OverlayBounds _touchpadStartBounds;
        private float _touchpadTravel;
        private TouchpadDragDirection _touchpadAxis;
        private int _touchpadStepX;
        private int _touchpadStepY;
        private int[] _touchpadLastIds = Array.Empty<int>();
    }

    // One background listener per process; every active Precision Control filter that
    // enables touchpad drag receives the same contact frames.
    internal static class TouchpadFrameRouter
    {
        public static void Register(PrecisionControl filter)
        {
            if (!OperatingSystem.IsWindows())
                return;

            lock (_syncRoot)
            {
                if (!_filters.Contains(filter))
                    _filters.Add(filter);
                EnsureStarted();
            }
        }

        public static void Unregister(PrecisionControl filter)
        {
            lock (_syncRoot)
            {
                _filters.Remove(filter);
                if (_filters.Count == 0)
                    Stop();
            }
        }

        private static void EnsureStarted()
        {
            if (_listener != null)
                return;

            var listener = new TouchpadRawInputListener();
            if (!listener.IsRegistered)
            {
                Log.Write(
                    nameof(PrecisionControl),
                    $"Touchpad drag unavailable: precision touchpad raw input could not be registered (Win32 error {listener.RegistrationError}).",
                    LogLevel.Warning);
                listener.Dispose();
                return;
            }

            listener.FrameReceived += OnFrame;
            _listener = listener;
            Log.Write(
                nameof(PrecisionControl),
                "Touchpad drag listening for precision touchpad contacts in the background.",
                LogLevel.Info);
        }

        private static void Stop()
        {
            if (_listener == null)
                return;

            _listener.FrameReceived -= OnFrame;
            _listener.Dispose();
            _listener = null;
        }

        private static void OnFrame(TouchpadFrame frame)
        {
            PrecisionControl[] snapshot;
            lock (_syncRoot)
                snapshot = _filters.ToArray();

            foreach (var filter in snapshot)
            {
                try
                {
                    filter.HandleTouchpadFrame(frame);
                }
                catch (Exception exception)
                {
                    Log.Write(
                        nameof(PrecisionControl),
                        $"Touchpad drag frame failed: {exception.Message}",
                        LogLevel.Error);
                }
            }
        }

        private static readonly object _syncRoot = new();
        private static readonly List<PrecisionControl> _filters = new();
        private static TouchpadRawInputListener? _listener;
    }
}
