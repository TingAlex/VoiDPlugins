using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiDPlugins.Filter
{
    // One assembler per device. Contact Count includes release records (Tip=0),
    // and zero-count reports may be continuations of a hybrid HID frame.
    internal sealed class TouchpadFrameAssembler
    {
        internal TouchpadFrame? AddReport(
            long timestampMilliseconds,
            IReadOnlyList<TouchpadContact> contacts,
            int contactCount,
            int horizontalRange,
            int verticalRange,
            uint? scanTime = null)
        {
            if (contactCount < 0 || contactCount > 5)
            {
                Reset();
                return Frame(Array.Empty<TouchpadContact>());
            }

            if (contactCount > 0)
            {
                Reset();
                _expectedCount = contactCount;
                _scanTime = scanTime;
                _startedAt = timestampMilliseconds;
            }
            else if (contacts.Count == 0)
            {
                Reset();
                return Frame(Array.Empty<TouchpadContact>());
            }
            else if (_expectedCount == 0)
            {
                // Ignore an orphan continuation: never borrow contacts from another scan.
                return Frame(Array.Empty<TouchpadContact>());
            }

            if (_scanTime != scanTime || timestampMilliseconds - _startedAt > 40)
            {
                Reset();
                return Frame(Array.Empty<TouchpadContact>());
            }

            // Fixed-size reports may contain unused trailing slots. Only consume the
            // declared records, including Tip=0, before selecting touching fingers.
            foreach (var contact in contacts.Take(_expectedCount - _pending.Count))
            {
                if (_pending.Any(existing => existing.ContactId == contact.ContactId))
                {
                    Reset();
                    return Frame(Array.Empty<TouchpadContact>());
                }
                _pending.Add(contact);
            }

            if (_pending.Count < _expectedCount)
                return null;

            var completed = _pending.ToArray();
            Reset();
            return Frame(completed);

            TouchpadFrame Frame(IReadOnlyList<TouchpadContact> completedContacts) =>
                new(timestampMilliseconds, completedContacts, horizontalRange, verticalRange);
        }

        internal void Reset()
        {
            _pending.Clear();
            _expectedCount = 0;
            _scanTime = null;
            _startedAt = 0;
        }

        private readonly List<TouchpadContact> _pending = new();
        private int _expectedCount;
        private uint? _scanTime;
        private long _startedAt;
    }
}
