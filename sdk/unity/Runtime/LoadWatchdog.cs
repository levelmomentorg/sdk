// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — pre-`ready` load watchdog.
//
// Mirrors the load-timeout watchdog in sdk/web (LevelMomentWebAd): if the
// hosted page never posts `ready` (it crashed, navigated away, or the network
// dropped it), the fullscreen WebView would otherwise cover the game forever.
// After `ready` there is intentionally NO timeout — a student thinking through
// a quiz must never be force-closed.
//
// Modelled as a tick-driven state machine with an injectable clock so the
// EditMode tests can drive it deterministically without Unity time.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    internal class LoadWatchdog
    {
        private readonly double _timeoutSeconds;
        private readonly Func<double> _now;

        private double _startedAt;
        private bool _running;
        private bool _fired;

        /// <param name="timeoutSeconds">Deadline; 0 or negative disables the watchdog.</param>
        /// <param name="now">Injectable clock returning monotonically increasing seconds.</param>
        public LoadWatchdog(double timeoutSeconds, Func<double> now)
        {
            _timeoutSeconds = timeoutSeconds;
            _now = now ?? throw new ArgumentNullException(nameof(now));
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        public bool HasFired
        {
            get { return _fired; }
        }

        /// <summary>Begin watching. No-op when the timeout is disabled (&lt;= 0).</summary>
        public void Start()
        {
            if (_timeoutSeconds <= 0)
                return;
            _startedAt = _now();
            _running = true;
        }

        /// <summary>The page posted `ready` — stop watching; no post-ready timeout.</summary>
        public void NotifyReady()
        {
            _running = false;
        }

        /// <summary>Stop without firing (terminal reached, or disposed).</summary>
        public void Cancel()
        {
            _running = false;
        }

        /// <summary>
        /// Advance the clock check. Returns true exactly once — on the first tick
        /// at or past the deadline while still running. Subsequent ticks return
        /// false.
        /// </summary>
        public bool Tick()
        {
            if (!_running || _fired)
                return false;
            if (_now() - _startedAt < _timeoutSeconds)
                return false;
            _running = false;
            _fired = true;
            return true;
        }
    }
}
