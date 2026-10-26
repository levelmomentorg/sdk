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
//   - A Load() that names no format and declares no slot gets an
//     interstitial slot of DefaultDurationSeconds. The hosted page decides the
//     break from the slot's adType; without one it would size the break by the
//     format alone, and the old quick_question default served a rewarded
//     single question in an interstitial placement.
//   - The shell also announces `kind=interstitial` on the /break URL it opens
//     (BreakUrl.Build). The hosted page reads the slot, not that param.
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

        /// <summary>
        /// The slot length an interstitial gets when Load() names neither a
        /// format nor a slot: the resume-play size, about three questions.
        /// </summary>
        public const int DefaultDurationSeconds = 18;

        /// <summary>The format an interstitial asks for when Load() names none.</summary>
        internal const string DefaultFormat = "mastery_round";

        private InterstitialAd(string placementId, string format, string studentToken, LevelMomentAdSlot slot)
        {
            // Only a call that left both open gets the defaults: a game that
            // named a format without a slot keeps the break it always got.
            if (string.IsNullOrEmpty(format))
            {
                format = DefaultFormat;
                if (slot == null)
                    slot = new LevelMomentAdSlot("interstitial", DefaultDurationSeconds);
            }
            _core = new BreakSurfaceCore<InterstitialAdShowCallbacks>(
                placementId, format, "interstitial", studentToken, handleEarnedReward: null, slot: slot);
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
            string format = null,
            LevelMomentAdSlot slot = null)
        {
            Load(placementId, null, callbacks, format, slot);
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
            string format = null,
            LevelMomentAdSlot slot = null)
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

            var ad = new InterstitialAd(placementId, format, resolvedToken, slot);
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

        /// <summary>The break format: <c>quick_question</c>, <c>practice_set</c>, <c>mastery_round</c>, or <c>intro_lesson</c>.</summary>
        public string Format
        {
            get { return _core.Format; }
        }

        /// <summary>
        /// What the game declared for this slot. When Load() named neither a
        /// format nor a slot, the default interstitial slot of
        /// <see cref="DefaultDurationSeconds"/>; null when it named a format
        /// and no slot.
        /// </summary>
        public LevelMomentAdSlot Slot
        {
            get { return _core.Slot; }
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
