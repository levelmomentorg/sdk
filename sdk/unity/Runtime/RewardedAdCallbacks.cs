// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — RewardedAd callback bundles.
//
// Mirrors the AdMob Unity plugin's RewardedAdLoadCallback +
// FullScreenContentCallback + OnUserEarnedReward, collapsed into two small
// callback objects (the same shape sdk/flutter uses).
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    public class LevelMomentRewardItem
    {
        public int Amount;
        public string RewardId;
    }

    /// <summary>
    /// Load-phase callbacks. Mirrors AdMob's <c>RewardedAdLoadCallback</c>.
    /// </summary>
    public class RewardedAdLoadCallbacks
    {
        /// <summary>Fired when the ad is ready to Show(). Mirrors <c>OnAdLoaded</c>.</summary>
        public Action<RewardedAd> OnAdLoaded;

        /// <summary>Fired when the ad could not be prepared. Mirrors <c>OnAdFailedToLoad</c>.</summary>
        public Action<LevelMomentAdError> OnAdFailedToLoad;
    }

    /// <summary>
    /// Show-phase callbacks. Mirrors AdMob's <c>FullScreenContentCallback</c>
    /// plus the <c>OnUserEarnedReward</c> handler. The dismissed/failed/showed
    /// callbacks live on <see cref="BreakShowCallbacksBase"/>, shared with
    /// <see cref="InterstitialAdShowCallbacks"/>; this class adds only the
    /// reward callbacks a rewarded break has and an interstitial does not.
    /// </summary>
    public class RewardedAdShowCallbacks : BreakShowCallbacksBase
    {
        /// <summary>
        /// Fired on each answer: <c>amount</c> is 1 for a correct answer, 0
        /// otherwise. May fire multiple times per break. Mirrors AdMob's
        /// <c>OnUserEarnedReward</c> (grant a bonus when amount == 1).
        /// </summary>
        public Action<int> OnUserEarnedReward;

        /// <summary>Answer event with the opaque impression identifier.</summary>
        public Action<LevelMomentRewardItem> OnUserEarnedRewardItem;
    }
}
