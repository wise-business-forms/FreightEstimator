using System;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// Holds one value for a short time (#49): the Poller Monitor re-reads every 2 s per open browser, and several open browsers
    /// must not multiply the load on the database. Readers that arrive while a read is under way wait for it instead of starting
    /// their own. A failed read is not kept, so the next reader tries again.
    /// </summary>
    internal class TimedCache<T>
    {
        private readonly Func<T> _read;
        private readonly TimeSpan _keepFor;
        private readonly Func<DateTime> _utcNow;
        private readonly object _gate = new object();
        private T _value;
        private DateTime _readAt;
        private bool _hasValue;

        public TimedCache(Func<T> read, TimeSpan keepFor, Func<DateTime> utcNow = null)
        {
            _read = read;
            _keepFor = keepFor;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public T Get()
        {
            lock (_gate)
            {
                DateTime now = _utcNow();
                if (!_hasValue || now - _readAt >= _keepFor)
                {
                    _value = _read();   // throws leave the old value stale and the next reader tries again
                    _readAt = now;
                    _hasValue = true;
                }
                return _value;
            }
        }
    }
}
