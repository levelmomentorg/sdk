// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — show-phase callbacks shared by every break kind.
//
// RewardedAd and InterstitialAd both open the hosted /break page and end the
// same three ways: dismissed, failed to show, or (optionally) reported as
// showing. Factored out so the two placements' show-callback classes cannot
// drift on the shared half of their shape — RewardedAdShowCallbacks adds the
// reward callbacks on top; InterstitialAdShowCallbacks adds nothing.
// ---------------------------------------------------------------------------

using System;

namespace LevelMoment
{
    /// <summary>
    /// Show-phase callbacks common to every hosted-/break placement. Mirrors
    /// the shared half of AdMob's <c>FullScreenContentCallback</c>.
    /// </summary>
    public abstract class BreakShowCallbacksBase
    {
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
