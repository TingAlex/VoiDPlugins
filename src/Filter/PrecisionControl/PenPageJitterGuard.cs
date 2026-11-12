using System;

namespace VoiDPlugins.Filter
{
    // Independent axis hysteresis in output touch pixels. It has no clock,
    // velocity estimate or catch-up animation: unchanged input cannot advance it.
    internal sealed class PenPageJitterGuard
    {
        private double _radius, _x, _y;
        internal bool Changed { get; private set; }

        internal void Reset(double coefficient)
        {
            _radius = coefficient;
            _x = _y = 0;
            Changed = false;
        }

        internal bool TryFilter(double x, double y, out (double X, double Y) result)
        {
            result = (_x, _y);
            Changed = false;
            if (!double.IsFinite(_radius) || _radius < 0 || _radius > 10 ||
                !double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 10000 || Math.Abs(y) > 10000)
                return false;
            var nextX = Math.Clamp(_x, x - _radius, x + _radius);
            var nextY = Math.Clamp(_y, y - _radius, y + _radius);
            Changed = nextX != _x || nextY != _y;
            _x = nextX;
            _y = nextY;
            result = (_x, _y);
            return true;
        }
    }
}
