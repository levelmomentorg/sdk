// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — the device's connection state, and require-internet
// mode.
//
// "Offline" means Unity reports the device as unreachable:
// Application.internetReachability == NetworkReachability.NotReachable, which
// is Wi-Fi and mobile data both off, or airplane mode. It never means Level
// Moment's servers failed. An outage on our side ends the break and the game
// keeps playing, as it always has: our outage must never lock a game.
//
// Unity raises no event when reachability changes, so the watcher reads it on
// the main thread every PollIntervalSeconds (2 seconds of real time, unaffected
// by Time.timeScale) and again whenever the application regains focus or
// resumes from pause. Reading internetReachability is a cached OS flag, not a
// network request.
//
// Mirrors sdk/web/src/connection.ts and sdk/flutter/lib/src/connection.dart.
// ---------------------------------------------------------------------------

using System;
using UnityEngine;

namespace LevelMoment
{
    /// <summary>
    /// Reads the device's connection state for every hosted surface, and owns
    /// the require-internet watcher when <see cref="LevelMomentConfig.RequireInternet"/>
    /// is set.
    /// </summary>
    internal static class LevelMomentConnection
    {
        /// <summary>How often the watcher reads reachability, in seconds.</summary>
        internal const double PollIntervalSeconds = 2.0;

        /// <summary>
        /// True while the device reports no connection. Tests replace it; see
        /// <see cref="LevelMomentAds.ResetForTests"/>.
        /// </summary>
        internal static Func<bool> ReportsOffline = DefaultReportsOffline;

        /// <summary>The require-internet watcher, or null when the mode is off.</summary>
        internal static ConnectionWatcher Watcher { get; private set; }

        /// <summary>
        /// Whether the device reports a connection right now, read fresh for a
        /// hosted surface about to open. Never calls the game: a watcher that
        /// disagrees catches up on its next tick, so the game hears the
        /// surface's own failure (and resumes) before it hears
        /// <c>OnConnectionChange(false)</c> (and pauses), never the reverse.
        /// </summary>
        internal static bool CheckOnline()
        {
            var online = !SafeReportsOffline();
            if (Watcher != null && online != Watcher.Online)
                Watcher.RequestRecheck();
            return online;
        }

        /// <summary>The last state the watcher saw, or a fresh read without one.</summary>
        internal static bool IsOnline
        {
            get { return Watcher != null ? Watcher.Online : !SafeReportsOffline(); }
        }

        internal static void StartWatching(string gameName, Action<bool> onConnectionChange)
        {
            // A game already told `false` by the watcher being replaced is not
            // told again: it hears `true` when the connection returns.
            var alreadyToldOffline = Watcher != null && Watcher.ToldOffline;
            StopWatching();
            Watcher = new ConnectionWatcher(
                gameName, onConnectionChange, SafeReportsOffline,
                delegate { return LevelMomentAds.ClockSeconds(); }, PollIntervalSeconds);
            Watcher.Start(alreadyToldOffline);
        }

        internal static void StopWatching()
        {
            if (Watcher == null)
                return;
            Watcher.Dispose();
            Watcher = null;
        }

        /// <summary>Forget the watcher and restore the device probe. Tests and domain reloads.</summary>
        internal static void ResetStatic()
        {
            StopWatching();
            ReportsOffline = DefaultReportsOffline;
        }

        // A probe that throws says nothing about the connection, so it reads
        // as online: a failure here must never lock a game.
        private static bool SafeReportsOffline()
        {
            try
            {
                return ReportsOffline();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool DefaultReportsOffline()
        {
            return Application.internetReachability == NetworkReachability.NotReachable;
        }
    }

    /// <summary>
    /// Require-internet mode: shows the full-screen "No internet connection"
    /// message while the device reports no connection, removes it when the
    /// connection returns, and tells the game through its callback.
    /// </summary>
    internal sealed class ConnectionWatcher : ITickable
    {
        private readonly string _gameName;
        private readonly Action<bool> _onChange;
        private readonly Func<bool> _reportsOffline;
        private readonly Func<double> _now;
        private readonly double _interval;

        private bool _online = true;
        private bool _disposed;
        private double _lastPoll;
        // Offline at Start: the game hears `false` on the first tick, after
        // Initialize() has returned and the game has finished its own setup.
        private bool _pendingInitialOffline;
        private bool _pendingInitialOnline;
        private bool _recheckRequested;
        private LevelMomentChrome.Layer _message;

        public ConnectionWatcher(
            string gameName,
            Action<bool> onChange,
            Func<bool> reportsOffline,
            Func<double> now,
            double intervalSeconds)
        {
            _gameName = gameName;
            _onChange = onChange;
            _reportsOffline = reportsOffline;
            _now = now;
            _interval = intervalSeconds;
        }

        /// <summary>False exactly while the message is showing.</summary>
        public bool Online
        {
            get { return _online; }
        }

        /// <summary>
        /// True while the game's last word from this watcher, or the one it
        /// replaced, was <c>false</c>, including while a <c>true</c> is queued.
        /// </summary>
        public bool ToldOffline
        {
            get { return (!_online && !_pendingInitialOffline) || _pendingInitialOnline; }
        }

        public void Start(bool alreadyToldOffline = false)
        {
            _lastPoll = _now();
            LevelMomentRuntime.Track(this);
            LevelMomentRuntime.ApplicationResumed += Recheck;
            if (_reportsOffline())
            {
                _online = false;
                _pendingInitialOffline = !alreadyToldOffline;
                Show();
            }
            else if (alreadyToldOffline)
            {
                // Back online between the two watchers: say so on the next tick.
                _pendingInitialOnline = true;
            }
        }

        /// <summary>Read the device again on the next tick instead of waiting for the poll.</summary>
        public void RequestRecheck()
        {
            _recheckRequested = true;
        }

        /// <summary>Read the device's state again, as Try again does.</summary>
        public void Recheck()
        {
            if (_disposed)
                return;
            _lastPoll = _now();
            Set(!_reportsOffline());
        }

        void ITickable.Tick()
        {
            Tick();
        }

        internal void Tick()
        {
            if (_disposed)
                return;
            if (_pendingInitialOffline)
            {
                _pendingInitialOffline = false;
                Notify(false);
                if (_disposed)
                    return;
            }
            if (_pendingInitialOnline)
            {
                _pendingInitialOnline = false;
                if (_online)
                {
                    Notify(true);
                    if (_disposed)
                        return;
                }
            }
            if (_recheckRequested || _now() - _lastPoll >= _interval)
            {
                _recheckRequested = false;
                Recheck();
            }
        }

        /// <summary>Remove the message and stop watching. Idempotent.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            LevelMomentRuntime.Untrack(this);
            LevelMomentRuntime.ApplicationResumed -= Recheck;
            Hide();
        }

        private void Set(bool online)
        {
            if (online == _online)
                return;
            _online = online;
            if (online)
            {
                Hide();
                // The game never heard it go: say nothing about it coming back.
                if (_pendingInitialOffline)
                {
                    _pendingInitialOffline = false;
                    return;
                }
            }
            else
            {
                Show();
                // The game still believes it is offline: the carried-over
                // `true` is cancelled and nothing new needs saying.
                if (_pendingInitialOnline)
                {
                    _pendingInitialOnline = false;
                    return;
                }
            }
            Notify(online);
        }

        private void Notify(bool online)
        {
            if (_onChange != null)
                _onChange(online);
        }

        private void Show()
        {
            if (_message == null)
                _message = LevelMomentChrome.ShowNoConnection(_gameName, Recheck);
        }

        private void Hide()
        {
            if (_message == null)
                return;
            LevelMomentChrome.Remove(_message);
            _message = null;
        }
    }
}
