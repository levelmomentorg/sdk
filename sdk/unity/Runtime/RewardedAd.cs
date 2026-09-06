// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — RewardedAd (ADR-001 WebView shell).
//
// Drop-in shape for AdMob's RewardedAd: Load() prepares, Show() presents. The
// SDK renders nothing — Show() opens the hosted /break page in a WebView and
// bridges the page's postMessage events (ready / earnedReward / dismissed /
// error) to the C# callbacks. Structurally mirrors sdk/flutter's
// LevelMomentRewardedAd and sdk/react-native's LevelMomentAd:
//   - Load() is a synchronous mark-ready — no network (the page does the fetch).
//   - dismissed/error are terminal and collapse to a single dismiss (the
//     terminal-once guard); earnedReward may fire many times before it.
//   - A pre-`ready` load watchdog (15s, mirroring sdk/web) prevents a crashed
//     page from covering the game forever.
//
// See docs/ADR-001-webview-rendering.md.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    public class RewardedAd
    {
        // RewardedAd is public and ITickable is internal, so RewardedAd cannot
        // implement it directly — C# forbids a public type from naming a less
        // accessible interface in its base list. A private adapter carries the
        // per-frame nudge instead, which also keeps the runtime's plumbing off
        // the public API a game developer sees.
        private sealed class Ticker : ITickable
        {
            private readonly RewardedAd _ad;

            public Ticker(RewardedAd ad)
            {
                _ad = ad;
            }

            public void Tick()
            {
                _ad.Tick();
            }
        }

        /// <summary>
        /// EditMode tests set this true to skip creating the runtime
        /// MonoBehaviour driver; they drive Tick() manually with an injected
        /// clock instead. Shared with the sign-in gate.
        /// </summary>
        internal static bool SkipRuntimeDriver
        {
            get { return LevelMomentRuntime.SkipDriver; }
            set { LevelMomentRuntime.SkipDriver = value; }
        }

        private readonly string _placementId;
        private readonly string _format;
        private readonly string _studentToken;

        private bool _loaded;
        private bool _shown;
        private bool _terminal;
        private bool _disposed;

        // Set while a publisher callback is on the stack, and left set if that
        // callback throws. A provider may raise OnMessage synchronously from
        // Open(), so an exception unwinding through Show()'s startup catch can
        // be the publisher's rather than the provider's — this is how the two
        // are told apart. See the catch in Show().
        private bool _inPublisherCallback;

        private RewardedAdShowCallbacks _callbacks;
        private ILevelMomentWebView _webView;
        // The hosted URL this break opened. An `openExternal` message is judged
        // against its origin, so a WebView that wandered off cannot send a
        // parent to a page of its own choosing.
        private string _hostedUrl;
        private LoadWatchdog _watchdog;
        private Ticker _ticker;

        private RewardedAd(string placementId, string format, string studentToken)
        {
            _placementId = placementId;
            _format = string.IsNullOrEmpty(format) ? "flashcard" : format;
            _studentToken = studentToken;
        }

        // ---- Static factory — mirrors RewardedAd.Load() ---------------------

        /// <summary>
        /// Mark an ad ready to Show(). Synchronous, no network — the hosted page
        /// fetches the question when Show() opens it. No token: a paired device's
        /// credential lives on the Level Moment origin, and the hosted page finds
        /// it there. Use the overload only for a sandbox token while you
        /// integrate.
        /// </summary>
        public static void Load(
            string placementId,
            RewardedAdLoadCallbacks callbacks,
            string format = "flashcard")
        {
            Load(placementId, null, callbacks, format);
        }

        /// <summary>
        /// Load with an explicit student session token — a sandbox token, for
        /// integration testing. A paired device needs none. Nothing reads a
        /// credential off the launch URL: a URL lands in history, in the referrer
        /// of whatever loads next, and in any crash report taken afterwards.
        /// </summary>
        public static void Load(
            string placementId,
            string studentToken,
            RewardedAdLoadCallbacks callbacks,
            string format = "flashcard")
        {
            if (!LevelMomentAds.IsInitialized)
            {
                Fail(callbacks, new LevelMomentAdError(
                    "not_initialized",
                    "Call LevelMomentAds.Initialize() before RewardedAd.Load()."));
                return;
            }
            if (string.IsNullOrEmpty(placementId))
            {
                Fail(callbacks, new LevelMomentAdError(
                    "invalid_request",
                    "placementId is required to load a break."));
                return;
            }

            string resolvedToken;
            try
            {
                resolvedToken = LevelMomentAds.ResolveStudentToken(studentToken);
            }
            catch (Exception err)
            {
                Fail(callbacks, new LevelMomentAdError("invalid_request", err.Message));
                return;
            }
            var ad = new RewardedAd(placementId, format, resolvedToken);
            ad._loaded = true;

            if (callbacks != null && callbacks.OnAdLoaded != null)
                callbacks.OnAdLoaded(ad);
        }

        private static void Fail(RewardedAdLoadCallbacks callbacks, LevelMomentAdError error)
        {
            if (callbacks != null && callbacks.OnAdFailedToLoad != null)
                callbacks.OnAdFailedToLoad(error);
        }

        // ---- Instance API ---------------------------------------------------

        /// <summary>True once loaded and not yet shown/disposed.</summary>
        public bool IsLoaded
        {
            get { return _loaded && !_shown && !_disposed; }
        }

        /// <summary>The break format: <c>flashcard</c>, <c>quiz</c>, or <c>deep_dive</c>.</summary>
        public string Format
        {
            get { return _format; }
        }

        /// <summary>
        /// Present the break. No network here — the hosted page does the fetch.
        /// Opens a fullscreen WebView at the hosted /break page and bridges its
        /// events to <paramref name="callbacks"/>.
        ///
        /// Catches WebView-provider startup failures. If the provider factory or
        /// <c>Open()</c> throws, the SDK reports <c>webview_error</c> through
        /// <c>OnAdFailedToShow</c> and tears the ad down, so the game resumes on
        /// its own callback. Exceptions thrown by your own callbacks propagate
        /// to you unchanged.
        /// </summary>
        public void Show(RewardedAdShowCallbacks callbacks)
        {
            if (_disposed || _shown)
                return;
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

            var url = BreakUrl.Build(LevelMomentAds.Config, _placementId, _format);
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
                // Only a provider that failed to start belongs here. A provider
                // may raise OnMessage synchronously from Open() — the bundled
                // NoWebViewFallback does — so this catch also sees exceptions
                // thrown by the game's own callbacks. Those are the game's, not
                // the provider's: reporting webview_error over an ad that
                // already opened would fire OnAdFailedToShow after
                // OnAdShowedFullScreenContent and tear down a live break.
                // Let them through untouched.
                if (_inPublisherCallback)
                    throw;

                // A third-party provider that throws out of its factory or its
                // Open() would otherwise escape into the game's break code —
                // past OnAdFailedToShow, and leaving a tracked ad ticking
                // forever with no way for the game to resume. Route it through
                // the same terminal path a posted `error` takes, which also
                // tears the ad down. Mirrors SignInGate.Open.
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

        internal void Tick()
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
                    // Unity keeps no copy to reconcile (no secure store yet),
                    // and the page never sends these to a host that did not ask
                    // for custody. Ignored rather than dropped as unknown, so
                    // adding a store later is a change in one place.
                    break;
                case HostMessageType.OpenExternal:
                    // A parent is off to approve the game in the device's
                    // browser. Non-terminal: this surface stays up and its poll
                    // is what hears them.
                    ExternalBrowser.Open(msg.Url, _hostedUrl);
                    break;
                case HostMessageType.Ready:
                    if (_watchdog != null)
                        _watchdog.NotifyReady();
                    if (_callbacks != null && _callbacks.OnAdShowedFullScreenContent != null)
                    {
                        var showed = _callbacks.OnAdShowedFullScreenContent;
                        FireToPublisher(delegate { showed(); });
                    }
                    break;
                case HostMessageType.EarnedReward:
                    // Non-terminal — may fire multiple times per break.
                    if (_callbacks != null && _callbacks.OnUserEarnedRewardItem != null)
                    {
                        var earnedItem = _callbacks.OnUserEarnedRewardItem;
                        var item = new LevelMomentRewardItem { Amount = msg.Amount, RewardId = msg.RewardId };
                        FireToPublisher(delegate { earnedItem(item); });
                    }
                    if (!_terminal && _callbacks != null && _callbacks.OnUserEarnedReward != null)
                    {
                        var earned = _callbacks.OnUserEarnedReward;
                        var amount = msg.Amount;
                        FireToPublisher(delegate { earned(amount); });
                    }
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
            _shown = false;
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
        /// startup catch tells a game's exception from a provider's.
        /// </summary>
        private void FireToPublisher(Action fire)
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
