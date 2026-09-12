// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — shared WebView-break state machine.
//
// RewardedAd and InterstitialAd both show the hosted /break page in a WebView
// and bridge its events back to C#; they differ only in whether an
// `earnedReward` message means anything (InterstitialAd has none to report)
// and in the `kind` they announce on the URL. Everything else — the
// synchronous Load()/MarkLoaded(), Show()'s WebView-open + watchdog + ticker
// wiring, the terminal-once dismissed/error/native-close collapse, and
// Teardown — lives here once, so the two placements cannot drift out of step
// with each other or with the terminal-once discipline sdk/flutter and
// sdk/react-native mirror.
//
// Internal, not a public base class: RewardedAd and InterstitialAd compose an
// instance of this rather than extend it. A public RewardedAd/InterstitialAd
// cannot name an internal base class in its base list (the same C# rule
// RewardedAd's own Ticker/ITickable adapter works around), and this state
// machine is plumbing a game developer never needs to see.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    /// <summary>
    /// Load()-time guards shared by every hosted-/break placement. A separate,
    /// non-generic type from <see cref="BreakSurfaceCore{TShowCallbacks}"/> (C#
    /// tells the two apart by arity, the way <c>Tuple</c>/<c>Tuple&lt;T&gt;</c>
    /// coexist) — these guards run before a placement instance exists, so they
    /// have no <c>TShowCallbacks</c> to be generic over.
    /// </summary>
    internal static class BreakSurfaceCore
    {
        /// <summary>
        /// Runs RewardedAd.Load's and InterstitialAd.Load's three guards: the
        /// SDK is initialized, <paramref name="placementId"/> is non-empty, and
        /// <paramref name="studentToken"/> resolves through
        /// <see cref="LevelMomentAds.ResolveStudentToken"/> (which throws for a
        /// non-sandbox token). Returns false with <paramref name="error"/> set
        /// on the first guard that fails; true with
        /// <paramref name="resolvedToken"/> set otherwise.
        /// </summary>
        internal static bool TryResolveLoad(
            string placementId,
            string studentToken,
            string notInitializedMessage,
            out string resolvedToken,
            out LevelMomentAdError error)
        {
            resolvedToken = null;

            if (!LevelMomentAds.IsInitialized)
            {
                error = new LevelMomentAdError("not_initialized", notInitializedMessage);
                return false;
            }
            if (string.IsNullOrEmpty(placementId))
            {
                error = new LevelMomentAdError(
                    "invalid_request", "placementId is required to load a break.");
                return false;
            }

            try
            {
                resolvedToken = LevelMomentAds.ResolveStudentToken(studentToken);
            }
            catch (Exception err)
            {
                error = new LevelMomentAdError("invalid_request", err.Message);
                return false;
            }

            error = null;
            return true;
        }
    }

    internal sealed class BreakSurfaceCore<TShowCallbacks> where TShowCallbacks : BreakShowCallbacksBase
    {
        private sealed class Ticker : ITickable
        {
            private readonly BreakSurfaceCore<TShowCallbacks> _core;

            public Ticker(BreakSurfaceCore<TShowCallbacks> core)
            {
                _core = core;
            }

            public void Tick()
            {
                _core.Tick();
            }
        }

        private readonly string _placementId;
        private readonly string _format;
        private readonly string _kind;
        // What the game declared for this slot, or null when it declared
        // nothing. Rides on the hosted /break URL; see AdSlot.cs.
        private readonly LevelMomentAdSlot _slot;
        private readonly string _studentToken;

        // Placement-specific handling for the one bridge message that varies
        // by kind. Null for a placement (InterstitialAd) that never reports one.
        private readonly Action<HostMessage> _handleEarnedReward;

        private bool _loaded;
        private bool _shown;
        private bool _terminal;
        private bool _disposed;

        // Set while a publisher callback is on the stack, and left set if that
        // callback throws. See the catch in Show() and RewardedAd.HandleEarnedReward.
        private bool _inPublisherCallback;

        private TShowCallbacks _callbacks;
        private ILevelMomentWebView _webView;
        // The hosted URL this break opened. An `openExternal` message is judged
        // against its origin, so a WebView that wandered off cannot send a
        // parent to a page of its own choosing.
        private string _hostedUrl;
        private LoadWatchdog _watchdog;
        private Ticker _ticker;

        public BreakSurfaceCore(
            string placementId,
            string format,
            string kind,
            string studentToken,
            Action<HostMessage> handleEarnedReward,
            LevelMomentAdSlot slot = null)
        {
            _placementId = placementId;
            _format = string.IsNullOrEmpty(format) ? "quick_question" : format;
            _kind = kind;
            _studentToken = studentToken;
            _handleEarnedReward = handleEarnedReward;
            _slot = slot;
        }

        /// <summary>What the game declared for this slot, or null.</summary>
        public LevelMomentAdSlot Slot
        {
            get { return _slot; }
        }

        /// <summary>The break format: <c>quick_question</c>, <c>practice_set</c>, <c>mastery_round</c>, or <c>intro_lesson</c>.</summary>
        public string Format
        {
            get { return _format; }
        }

        /// <summary>True once loaded and not yet shown/disposed.</summary>
        public bool IsLoaded
        {
            get { return _loaded && !_shown && !_disposed; }
        }

        /// <summary>True once a terminal outcome (dismiss/error/close/watchdog) has fired.</summary>
        public bool IsTerminal
        {
            get { return _terminal; }
        }

        /// <summary>The callbacks passed to the current/most recent Show(), for
        /// a placement's own message handling (e.g. RewardedAd's reward
        /// dispatch) to read.</summary>
        public TShowCallbacks Callbacks
        {
            get { return _callbacks; }
        }

        public void MarkLoaded()
        {
            _loaded = true;
        }

        public void Show(TShowCallbacks callbacks)
        {
            if (_disposed)
                return;
            if (_shown)
            {
                // Already shown (any outcome) or already open: a fresh handle
                // comes from Load() for each break, exactly like AdMob. Report
                // it rather than silently doing nothing, so a game that calls
                // Show() twice on the same handle hears about the mistake
                // instead of nothing happening.
                if (callbacks != null && callbacks.OnAdFailedToShow != null)
                    callbacks.OnAdFailedToShow(new LevelMomentAdError(
                        "not_loaded", "This ad was already shown. Load() a new ad for each break."));
                return;
            }
            if (!_loaded)
            {
                if (callbacks != null && callbacks.OnAdFailedToShow != null)
                    callbacks.OnAdFailedToShow(new LevelMomentAdError(
                        "not_loaded", "Show() called before the ad loaded. Call Load() first."));
                return;
            }

            _shown = true;
            _terminal = false;
            _inPublisherCallback = false;
            _callbacks = callbacks;

            var url = BreakUrl.Build(LevelMomentAds.Config, _placementId, _format, _kind, _slot);
            _hostedUrl = url;

            try
            {
                _webView = LevelMomentWebViewRegistry.Create();
                _webView.OnMessage += HandleRawMessage;
                _webView.OnClosed += HandleClosed;

                _watchdog = new LoadWatchdog(
                    LevelMomentAds.LoadTimeoutSeconds, LevelMomentAds.ClockSeconds);
                _watchdog.Start();

                _ticker = new Ticker(this);
                LevelMomentRuntime.Track(_ticker);

                // Open last: the NoWebViewFallback posts its error synchronously
                // here, and the handlers/watchdog are already wired to receive it.
                _webView.Open(url);
            }
            catch (Exception err)
            {
                // Only a provider that failed to start belongs here — see the
                // long-form reasoning in the historical RewardedAd.Show(),
                // preserved unchanged by this extraction.
                if (_inPublisherCallback)
                    throw;

                var error = new LevelMomentAdError("webview_error", err.Message);
                Terminate(delegate { FireFailedToShow(error); });
            }
        }

        /// <summary>
        /// Release the ad. Tears down the WebView without firing callbacks (the
        /// game is already handling teardown). Mirrors AdMob's Destroy().
        /// </summary>
        public void Destroy()
        {
            _disposed = true;
            if (!_terminal)
            {
                _terminal = true;
                Teardown();
            }
        }

        // ---- Watchdog tick (driven by LevelMomentRuntime, or tests) ---------

        public void Tick()
        {
            if (_terminal || _watchdog == null)
                return;
            if (_watchdog.Tick())
            {
                // Pre-`ready` timeout: clean resume, mirroring sdk/web (the page
                // never came up, so dismiss rather than surface an error).
                Terminate(FireDismissed);
            }
        }

        // ---- Bridge handling ------------------------------------------------

        private void HandleRawMessage(string raw)
        {
            if (_terminal)
                return;
            var msg = HostMessage.TryParse(raw);
            if (msg == null)
                return; // malformed / unknown — dropped, like the other shells.

            switch (msg.Type)
            {
                case HostMessageType.NeedCredential:
                    // The page asks rather than reading a token off its URL.
                    // Answer with whatever this game configured — usually
                    // nothing, because a paired device's credential already
                    // lives on the hosted origin.
                    CredentialBridge.Deliver(
                        _webView,
                        LevelMomentAds.Config.Mock ? null : _studentToken,
                        LevelMomentAds.Config.CustomData,
                        OriginOf(_hostedUrl));
                    break;
                case HostMessageType.CredentialIssued:
                case HostMessageType.CredentialInvalid:
                    // Unity keeps no copy to reconcile (no secure store yet).
                    break;
                case HostMessageType.OpenExternal:
                    // Non-terminal: this surface stays up and its poll is what
                    // hears the parent's approval.
                    ExternalBrowser.Open(msg.Url, _hostedUrl);
                    break;
                case HostMessageType.Ready:
                    if (_watchdog != null)
                        _watchdog.NotifyReady();
                    FireShowed();
                    break;
                case HostMessageType.EarnedReward:
                    // Non-terminal — may fire multiple times per break. Only
                    // relevant to a rewarded placement; InterstitialAd supplies
                    // no handler and the message is dropped here.
                    if (_handleEarnedReward != null)
                        _handleEarnedReward(msg);
                    break;
                // SignedIn belongs to the sign-in gate and never reaches a
                // break. If one ever arrives the surface is finished, so resume
                // the game rather than leaving it waiting for a dismiss that
                // will not come.
                case HostMessageType.SignedIn:
                case HostMessageType.Dismissed:
                    Terminate(FireDismissed);
                    break;
                case HostMessageType.Error:
                    var error = new LevelMomentAdError(msg.Code, msg.Message);
                    Terminate(delegate { FireFailedToShow(error); });
                    break;
            }
        }

        private void HandleClosed()
        {
            // Native-initiated close (e.g. hardware back). Treat as a dismiss.
            Terminate(FireDismissed);
        }

        // The terminal-once guard: dismissed/error/native-close/watchdog all
        // funnel here; only the first wins. Mirrors sdk/flutter's `terminal`
        // flag and sdk/react-native's dismissedRef.
        private void Terminate(Action fire)
        {
            if (_terminal)
                return;
            _terminal = true;
            Teardown();
            if (fire != null)
                fire();
        }

        private void Teardown()
        {
            if (_watchdog != null)
                _watchdog.Cancel();
            if (_webView != null)
            {
                _webView.OnMessage -= HandleRawMessage;
                _webView.OnClosed -= HandleClosed;
                try
                {
                    _webView.Close();
                }
                catch (Exception)
                {
                    // Teardown often runs while cleaning up after a provider that
                    // already threw. Its Close() throwing too must not mask the
                    // failure we are on our way to report.
                }
                _webView = null;
            }
            if (_ticker != null)
            {
                LevelMomentRuntime.Untrack(_ticker);
                _ticker = null;
            }
            // `_shown` is intentionally NOT reset here. It latches for the life
            // of this handle: once a break has been shown (any outcome —
            // dismissed, failed, closed), IsLoaded must stay false and a second
            // Show() must not reopen it. A game gets a fresh RewardedAd/
            // InterstitialAd from Load() for its next break, exactly like AdMob.
        }

        private void FireShowed()
        {
            if (_callbacks == null || _callbacks.OnAdShowedFullScreenContent == null)
                return;
            var showed = _callbacks.OnAdShowedFullScreenContent;
            FireToPublisher(delegate { showed(); });
        }

        private void FireDismissed()
        {
            if (_callbacks == null || _callbacks.OnAdDismissed == null)
                return;
            var dismissed = _callbacks.OnAdDismissed;
            FireToPublisher(delegate { dismissed(); });
        }

        private void FireFailedToShow(LevelMomentAdError error)
        {
            if (_callbacks == null || _callbacks.OnAdFailedToShow == null)
                return;
            var failed = _callbacks.OnAdFailedToShow;
            FireToPublisher(delegate { failed(error); });
        }

        /// <summary>
        /// Run one of the game's callbacks, marking the stack while it runs.
        /// The flag stays set if the callback throws, which is how Show()'s
        /// startup catch tells a game's exception from a provider's. Internal
        /// so a placement's own message handling (RewardedAd's reward dispatch
        /// is the only one today) wraps its callback firing the same way.
        /// </summary>
        internal void FireToPublisher(Action fire)
        {
            _inPublisherCallback = true;
            fire();
            _inPublisherCallback = false;
        }

        private static string OriginOf(string url)
        {
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed))
                return string.Empty;
            if (parsed.IsDefaultPort)
                return parsed.Scheme + "://" + parsed.Host;
            return parsed.GetLeftPart(UriPartial.Authority);
        }
    }
}
