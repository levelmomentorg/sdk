// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — InterstitialAd callback bundles.
//
// Mirrors RewardedAdCallbacks.cs minus the reward-earned callbacks: an
// interstitial break has nothing to grant, so InterstitialAdShowCallbacks is
// BreakShowCallbacksBase with nothing added on top.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    /// <summary>
    /// Load-phase callbacks. Mirrors <see cref="RewardedAdLoadCallbacks"/>.
    /// </summary>
    public class InterstitialAdLoadCallbacks
    {
        /// <summary>Fired when the ad is ready to Show(). Mirrors <c>OnAdLoaded</c>.</summary>
        public Action<InterstitialAd> OnAdLoaded;

        /// <summary>Fired when the ad could not be prepared. Mirrors <c>OnAdFailedToLoad</c>.</summary>
        public Action<LevelMomentAdError> OnAdFailedToLoad;
    }

    /// <summary>
    /// Show-phase callbacks. Same dismissed/failed/showed outcomes as
    /// <see cref="RewardedAdShowCallbacks"/> (see <see cref="BreakShowCallbacksBase"/>)
    /// minus the reward callbacks — an interstitial break has nothing to grant.
    /// </summary>
    public class InterstitialAdShowCallbacks : BreakShowCallbacksBase
    {
    }
}
