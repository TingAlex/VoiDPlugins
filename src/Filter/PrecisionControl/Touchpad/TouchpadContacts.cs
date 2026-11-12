using System;
using System.Collections.Generic;

namespace VoiDPlugins.Filter
{
    // Adapted from emoacht/RawInput.Touchpad (MIT, Copyright (c) 2021 emoacht)
    // and ClementGre/ThreeFingerDragOnWindows (MIT, Copyright (c) 2022 Clément Grennerat).
    // The gesture state machine and the raw-input window are local changes for Precision Control;
    // the contact model and the precision-touchpad report parsing follow the upstream sources.
    internal readonly struct TouchpadContact : IEquatable<TouchpadContact>
    {
        public TouchpadContact(int contactId, int x, int y)
        {
            ContactId = contactId;
            X = x;
            Y = y;
        }

        public int ContactId { get; }
        public int X { get; }
        public int Y { get; }

        public bool Equals(TouchpadContact other) =>
            ContactId == other.ContactId && X == other.X && Y == other.Y;

        public override bool Equals(object? obj) => obj is TouchpadContact other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ContactId, X, Y);

        public override string ToString() => $"ID {ContactId} ({X},{Y})";
    }

    internal sealed class TouchpadFrame
    {
        public TouchpadFrame(
            long timestampMilliseconds,
            IReadOnlyList<TouchpadContact> contacts,
            int horizontalRange,
            int verticalRange)
        {
            TimestampMilliseconds = timestampMilliseconds;
            Contacts = contacts ?? Array.Empty<TouchpadContact>();
            HorizontalRange = Math.Max(1, horizontalRange);
            VerticalRange = Math.Max(1, verticalRange);
        }

        public long TimestampMilliseconds { get; }
        public IReadOnlyList<TouchpadContact> Contacts { get; }

        // Logical coordinate span reported by the touchpad HID descriptor. Used only
        // to turn raw contact movement into a stable screen-pixel gain.
        public int HorizontalRange { get; }
        public int VerticalRange { get; }
    }
}
