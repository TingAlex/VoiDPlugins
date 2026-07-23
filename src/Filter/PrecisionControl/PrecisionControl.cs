using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace VoiDPlugins.Filter
{
    [PluginName("Precision Control")]
    public class PrecisionControlBinding : IStateBinding
    {
        public static string[] ValidModes => new[] { "Toggle", "Hold" };

        [Property("Mode"), PropertyValidated(nameof(ValidModes))]
        public string? Mode { set; get; }

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (Mode == "Toggle")
                PrecisionControlCoordinator.Request(tablet, PrecisionControlAction.Toggle);
            else if (Mode == "Hold")
                PrecisionControlCoordinator.Request(tablet, PrecisionControlAction.Activate);
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            if (Mode == "Hold")
                PrecisionControlCoordinator.Request(tablet, PrecisionControlAction.Deactivate);
        }
    }

    [PluginName("Precision Control")]
    public class PrecisionControl : IPositionedPipelineElement<IDeviceReport>, IDisposable
    {
        public event Action<IDeviceReport>? Emit;

        [SliderProperty("Precision Multiplier", 0.0f, 10f, 0.3f), DefaultPropertyValue(0.3f)]
        public float Scale { get; set; }

        [TabletReference]
        public TabletReference? Tablet { get; set; }

        public PipelinePosition Position => PipelinePosition.PostTransform;

        internal bool PenInRange
        {
            get
            {
                lock (_rangeLock)
                    return _penInRange;
            }
        }

        internal long LastInRangeTimestamp => Interlocked.Read(ref _lastInRangeTimestamp);

        [OnDependencyLoad]
        public void Initialize()
        {
            PrecisionControlCoordinator.Register(this);
        }

        public void Consume(IDeviceReport value)
        {
            if (value is OutOfRangeReport)
            {
                lock (_rangeLock)
                {
                    _penInRange = false;
                    _pendingGlobalToggleCount = 0;
                }
            }

            if (value is ITabletReport report)
            {
                int pendingGlobalToggleCount;
                lock (_rangeLock)
                {
                    _penInRange = true;
                    pendingGlobalToggleCount = _pendingGlobalToggleCount;
                    _pendingGlobalToggleCount = 0;
                }
                Interlocked.Exchange(ref _lastInRangeTimestamp, Stopwatch.GetTimestamp());

                while (_pendingActions.TryDequeue(out var action))
                    ApplyAction(action, report.Position);

                if ((pendingGlobalToggleCount & 1) != 0)
                    ApplyAction(PrecisionControlAction.Toggle, report.Position);

                if (_isActive)
                {
                    var delta = (report.Position - _startingPoint) * Scale;
                    report.Position = _startingPoint + delta;
                }
                value = report;
            }

            Emit?.Invoke(value);
        }

        internal void Queue(PrecisionControlAction action)
        {
            _pendingActions.Enqueue(action);
        }

        internal bool QueueGlobalToggle()
        {
            lock (_rangeLock)
            {
                if (!_penInRange)
                    return false;

                _pendingGlobalToggleCount++;
                return true;
            }
        }

        private void ApplyAction(PrecisionControlAction action, Vector2 currentPosition)
        {
            switch (action)
            {
                case PrecisionControlAction.Toggle:
                    _isActive = !_isActive;
                    if (_isActive)
                        _startingPoint = currentPosition;
                    break;
                case PrecisionControlAction.Activate:
                    _isActive = true;
                    _startingPoint = currentPosition;
                    break;
                case PrecisionControlAction.Deactivate:
                    _isActive = false;
                    break;
            }
        }

        public void Dispose()
        {
            PrecisionControlCoordinator.Unregister(this);
        }

        private readonly ConcurrentQueue<PrecisionControlAction> _pendingActions = new();
        private Vector2 _startingPoint;
        private bool _isActive;
        private bool _penInRange;
        private long _lastInRangeTimestamp;
        private int _pendingGlobalToggleCount;
        private readonly object _rangeLock = new();
    }

    internal enum PrecisionControlAction
    {
        Toggle,
        Activate,
        Deactivate
    }

    internal static class PrecisionControlCoordinator
    {
        public static void Register(PrecisionControl filter)
        {
            lock (_syncRoot)
            {
                if (!_filters.Contains(filter))
                    _filters.Add(filter);
            }
        }

        public static void Unregister(PrecisionControl filter)
        {
            lock (_syncRoot)
                _filters.Remove(filter);
        }

        public static void Request(TabletReference tablet, PrecisionControlAction action)
        {
            lock (_syncRoot)
            {
                var filter = _filters.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate.Tablet, tablet));
                filter?.Queue(action);
            }
        }

        public static bool TryQueueGlobalToggle()
        {
            lock (_syncRoot)
            {
                var filter = _filters
                    .Where(candidate => candidate.PenInRange)
                    .OrderByDescending(candidate => candidate.LastInRangeTimestamp)
                    .FirstOrDefault();

                return filter?.QueueGlobalToggle() ?? false;
            }
        }

        private static readonly object _syncRoot = new();
        private static readonly List<PrecisionControl> _filters = new();
    }
}
