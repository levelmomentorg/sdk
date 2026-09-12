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
// The WebView lifecycle, watchdog, and terminal-once collapse live in
// BreakSurfaceCore, shared with InterstitialAd (see InterstitialAd.cs). This
// file is left owning only what is specific to a rewarded break: the reward
// dispatch, and the public Load/Show/Destroy shape a game developer sees.
//
// See docs/ADR-001-webview-rendering.md.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    public class RewardedAd
    {
        /// <summary>
        /// EditMode tests set this true to skip creating the runtime
        /// MonoBehaviour driver; they drive Tick() manually with an injected
        /// clock instead. Shared with InterstitialAd and the sign-in gate.
        /// </summary>
        internal static bool SkipRuntimeDriver
        {
            get { return LevelMomentRuntime.SkipDriver; }
            set { LevelMomentRuntime.SkipDriver = value; }
        }

        // No `kind` is ever passed to the core here — the /break URL this
        // placement opens is byte-for-byte what it always was. See
        // InterstitialAd, which passes "interstitial".
        private readonly BreakSurfaceCore<RewardedAdShowCallbacks> _core;

        private RewardedAd(string placementId, string format, string studentToken)
        {
            _core = new BreakSurfaceCore<RewardedAdShowCallbacks>(
                placementId, format, null, studentToken, HandleEarnedReward);
        }

        // ---- Static factory — mirrors RewardedAd.Load() ---------------------

        /// <summary>
        /// Mark an ad ready to Show(). Synchronous, no network — the hosted
        /// page fetches the question when Show() opens it. No token: a paired
        /// device's credential lives on the Level Moment origin, and the
        /// hosted page finds it there. Use the overload only for a sandbox
        /// token while you integrate.
        /// </summary>
        public static void Load(
            string placementId,
            RewardedAdLoadCallbacks callbacks,
            string format = "quick_question")
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
            string format = "quick_question")
        {
            string resolvedToken;
            LevelMomentAdError error;
            if (!BreakSurfaceCore.TryResolveLoad(
                placementId, studentToken,
                "Call LevelMomentAds.Initialize() before RewardedAd.Load().",
                out resolvedToken, out error))
            {
                Fail(callbacks, error);
                return;
            }

            var ad = new RewardedAd(placementId, format, resolvedToken);
            ad._core.MarkLoaded();

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
            get { return _core.IsLoaded; }
        }

        /// <summary>The break format: <c>quick_question</c>, <c>practice_set</c>, <c>mastery_round</c>, or <c>intro_lesson</c>.</summary>
        public string Format
        {
            get { return _core.Format; }
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
            _core.Show(callbacks);
        }

        /// <summary>
        /// Release the ad. Tears down the WebView without firing callbacks (the
        /// game is already handling teardown). Mirrors AdMob's Destroy().
        /// </summary>
        public void Destroy()
        {
            _core.Destroy();
        }

        // ---- Watchdog tick (driven by LevelMomentRuntime, or tests) ---------

        internal void Tick()
        {
            _core.Tick();
        }

        // ---- Reward dispatch (the one piece of bridge handling that is
        // specific to a rewarded break) --------------------------------------

        private void HandleEarnedReward(HostMessage msg)
        {
            var callbacks = _core.Callbacks;
            if (callbacks == null)
                return;

            if (callbacks.OnUserEarnedRewardItem != null)
            {
                var earnedItem = callbacks.OnUserEarnedRewardItem;
                var item = new LevelMomentRewardItem { Amount = msg.Amount, RewardId = msg.RewardId };
                _core.FireToPublisher(delegate { earnedItem(item); });
            }
            if (!_core.IsTerminal && callbacks.OnUserEarnedReward != null)
            {
                var earned = callbacks.OnUserEarnedReward;
                var amount = msg.Amount;
                _core.FireToPublisher(delegate { earned(amount); });
            }
        }
    }
}
