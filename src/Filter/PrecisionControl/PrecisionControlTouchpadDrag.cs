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
                Log.Debug(
                    nameof(PrecisionControl),
                    $"Touchpad drag started with {fingerCount} contacts.");
                return;
            }

            var sensitivity = TouchpadDragSensitivity;
            if (!float.IsFinite(sensitivity) || sensitivity < 0.05f || sensitivity > 3f)
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
            var offsetX = Math.Round((centroid.X - _touchpadStartCentroid.X) * gainX);
            var offsetY = Math.Round((centroid.Y - _touchpadStartCentroid.Y) * gainY);

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
