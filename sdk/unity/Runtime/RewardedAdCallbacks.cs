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
    /// plus the <c>OnUserEarnedReward</c> handler.
    /// </summary>
    public class RewardedAdShowCallbacks
    {
        /// <summary>
        /// Fired on each answer: <c>amount</c> is 1 for a correct answer, 0
        /// otherwise. May fire multiple times per break. Mirrors AdMob's
        /// <c>OnUserEarnedReward</c> (grant a bonus when amount == 1).
        /// </summary>
        public Action<int> OnUserEarnedReward;

        /// <summary>
        /// Fired exactly once when the break ends (any outcome). ALWAYS resume
        /// your game here. Mirrors <c>OnAdFullScreenContentClosed</c>.
        /// </summary>
        public Action OnAdDismissed;

        /// <summary>
        /// Fired instead of OnAdDismissed when the break could not be shown
        /// (not loaded, no WebView provider, page error). Resume your game here
        /// too. Mirrors <c>OnAdFullScreenContentFailed</c>.
        /// </summary>
        public Action<LevelMomentAdError> OnAdFailedToShow;

        /// <summary>
        /// Optional. Fired once when the hosted page reports it is mounted and
        /// interactive (the page's <c>ready</c> event). Mirrors
        /// <c>OnAdFullScreenContentOpened</c>.
        /// </summary>
        public Action OnAdShowedFullScreenContent;
    }
}
