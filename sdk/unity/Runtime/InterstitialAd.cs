// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — InterstitialAd (ADR-001 WebView shell).
//
// Drop-in shape for AdMob's InterstitialAd: Load() prepares, Show() presents.
// Structurally identical to RewardedAd — same hosted /break WebView, same
// watchdog, same terminal-once dismissed/error/native-close collapse (all in
// BreakSurfaceCore, shared with RewardedAd; see RewardedAd.cs and
// BreakSurfaceCore.cs) — with two differences:
//   - No reward: InterstitialAdShowCallbacks carries none of
//     RewardedAdShowCallbacks' reward callbacks, and an `earnedReward` bridge
//     message (which the hosted page should not send for this kind) is simply
//     dropped rather than dispatched anywhere.
//   - The shell announces `kind=interstitial` on the /break URL it opens
//     (BreakUrl.Build), so the hosted page can tell the two kinds apart. As of
//     this SDK version the web /break page does not yet read that param — see
//     README.md → InterstitialAd.
//
// See docs/ADR-001-webview-rendering.md.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    public class InterstitialAd
    {
        /// <summary>
        /// EditMode tests set this true to skip creating the runtime
        /// MonoBehaviour driver; they drive Tick() manually with an injected
        /// clock instead. Shared with RewardedAd and the sign-in gate.
        /// </summary>
        internal static bool SkipRuntimeDriver
        {
            get { return LevelMomentRuntime.SkipDriver; }
            set { LevelMomentRuntime.SkipDriver = value; }
        }

        private readonly BreakSurfaceCore<InterstitialAdShowCallbacks> _core;

        private InterstitialAd(string placementId, string format, string studentToken)
        {
            _core = new BreakSurfaceCore<InterstitialAdShowCallbacks>(
                placementId, format, "interstitial", studentToken, handleEarnedReward: null);
        }

        // ---- Static factory — mirrors InterstitialAd.Load() -----------------

        /// <summary>
        /// Mark a break ready to Show(). Synchronous, no network — the hosted
        /// page fetches the question when Show() opens it. No token: a paired
        /// device's credential lives on the Level Moment origin, and the
        /// hosted page finds it there. Use the overload only for a sandbox
        /// token while you integrate.
        /// </summary>
        public static void Load(
            string placementId,
            InterstitialAdLoadCallbacks callbacks,
            string format = "flashcard")
        {
            Load(placementId, null, callbacks, format);
        }

        /// <summary>
        /// Load with an explicit student session token — a sandbox token, for
        /// integration testing. A paired device needs none. Nothing reads a
        /// credential off the launch URL — see RewardedAd.Load.
        /// </summary>
        public static void Load(
            string placementId,
            string studentToken,
            InterstitialAdLoadCallbacks callbacks,
            string format = "flashcard")
        {
            string resolvedToken;
            LevelMomentAdError error;
            if (!BreakSurfaceCore.TryResolveLoad(
                placementId, studentToken,
                "Call LevelMomentAds.Initialize() before InterstitialAd.Load().",
                out resolvedToken, out error))
            {
                Fail(callbacks, error);
                return;
            }

            var ad = new InterstitialAd(placementId, format, resolvedToken);
            ad._core.MarkLoaded();

            if (callbacks != null && callbacks.OnAdLoaded != null)
                callbacks.OnAdLoaded(ad);
        }

        private static void Fail(InterstitialAdLoadCallbacks callbacks, LevelMomentAdError error)
        {
            if (callbacks != null && callbacks.OnAdFailedToLoad != null)
                callbacks.OnAdFailedToLoad(error);
        }

        // ---- Instance API -----------------------------------------------------

        /// <summary>True once loaded and not yet shown/disposed.</summary>
        public bool IsLoaded
        {
            get { return _core.IsLoaded; }
        }

        /// <summary>The break format: <c>flashcard</c>, <c>quiz</c>, or <c>deep_dive</c>.</summary>
        public string Format
        {
            get { return _core.Format; }
        }

        /// <summary>
        /// Present the break. No network here — the hosted page does the fetch.
        /// Opens a fullscreen WebView at the hosted /break page and bridges its
        /// events to <paramref name="callbacks"/>. Same lifecycle and error
        /// handling as <see cref="RewardedAd.Show"/>.
        /// </summary>
        public void Show(InterstitialAdShowCallbacks callbacks)
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
    }
}
