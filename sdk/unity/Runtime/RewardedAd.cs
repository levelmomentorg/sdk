// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — RewardedAd (ADR-001 WebView shell).
//
// Drop-in shape for AdMob's RewardedAd: Load() preloads, Show() presents. The
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
        // EditMode tests set this true to skip creating the runtime MonoBehaviour
        // driver; they drive Tick() manually with an injected clock instead.
        internal static bool SkipRuntimeDriver;

        private readonly string _placementId;
        private readonly string _format;
        private readonly string _studentToken;

        private bool _loaded;
        private bool _shown;
        private bool _terminal;
        private bool _disposed;

        private RewardedAdShowCallbacks _callbacks;
        private ILevelMomentWebView _webView;
        private LoadWatchdog _watchdog;

        private RewardedAd(string placementId, string format, string studentToken)
        {
            _placementId = placementId;
            _format = string.IsNullOrEmpty(format) ? "flashcard" : format;
            _studentToken = studentToken;
        }

        // ---- Static factory — mirrors RewardedAd.Load() ---------------------

        /// <summary>
        /// Mark an ad ready to Show(). Synchronous, no network — the hosted page
        /// fetches the question when Show() opens it. The token defaults to the
        /// game's launch <c>?token=</c> param; pass one explicitly with the
        /// overload if you read it yourself.
        /// </summary>
        public static void Load(
            string placementId,
            RewardedAdLoadCallbacks callbacks,
            string format = "flashcard")
        {
            Load(placementId, null, callbacks, format);
        }

        /// <summary>
        /// Load with an explicit student session token (parent-portal issued).
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

            var token = LevelMomentAds.ResolveToken(studentToken);
            var ad = new RewardedAd(placementId, format, token);
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
            _callbacks = callbacks;

            var url = BreakUrl.Build(LevelMomentAds.Config, _placementId, _format, _studentToken);

            _webView = LevelMomentWebViewRegistry.Create();
            _webView.OnMessage += HandleRawMessage;
            _webView.OnClosed += HandleClosed;

            _watchdog = new LoadWatchdog(
                LevelMomentAds.LoadTimeoutSeconds, LevelMomentAds.ClockSeconds);
            _watchdog.Start();

            if (!SkipRuntimeDriver)
                LevelMomentRuntime.Track(this);

            // Open last: the NoWebViewFallback posts its error synchronously here,
            // and the handlers/watchdog are already wired to receive it.
            _webView.Open(url);
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
            var msg = HostMessage.TryParse(raw);
            if (msg == null)
                return; // malformed / unknown — dropped, like the other shells.

            switch (msg.Type)
            {
                case HostMessageType.Ready:
                    if (_watchdog != null)
                        _watchdog.NotifyReady();
                    if (_callbacks != null && _callbacks.OnAdShowedFullScreenContent != null)
                        _callbacks.OnAdShowedFullScreenContent();
                    break;
                case HostMessageType.EarnedReward:
                    // Non-terminal — may fire multiple times per break.
                    if (!_terminal && _callbacks != null && _callbacks.OnUserEarnedReward != null)
                        _callbacks.OnUserEarnedReward(msg.Amount);
                    break;
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
                _webView.Close();
                _webView = null;
            }
            if (!SkipRuntimeDriver)
                LevelMomentRuntime.Untrack(this);
            _shown = false;
        }

        private void FireDismissed()
        {
            if (_callbacks != null && _callbacks.OnAdDismissed != null)
                _callbacks.OnAdDismissed();
        }

        private void FireFailedToShow(LevelMomentAdError error)
        {
            if (_callbacks != null && _callbacks.OnAdFailedToShow != null)
                _callbacks.OnAdFailedToShow(error);
        }
    }
}
