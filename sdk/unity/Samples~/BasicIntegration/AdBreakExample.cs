// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — basic integration example.
//
// Attach to a GameObject in your first scene. The SDK renders nothing itself:
// Show() opens the hosted break in a WebView and calls back when it ends.
// This script checks access, loads a fresh break, grants the reward once, and
// resumes the game. It follows https://levelmoment.com/docs/getting-started.
//
// Needs the SDK and gree lines in Packages/manifest.json. For sandbox, put the
// token in UserSettings/LevelMomentSandboxToken.txt; the SDK reads it in the
// Editor and development builds.
// ---------------------------------------------------------------------------

using UnityEngine;
using LevelMoment;

public class AdBreakExample : MonoBehaviour
{
    // From the game's page at levelmoment.com/games. It starts with pl_.
    [SerializeField] private string placementId = "YOUR_PLACEMENT_ID";

    // What the moment grants, for reporting. The game still grants it.
    [SerializeField] private int coinsPerBreak = 50;

    private bool _breakOpen;

    private void Awake()
    {
        if (!LevelMomentAds.IsInitialized)
            LevelMomentAds.Initialize(new LevelMomentConfig());
    }

    // Call from the player's tap on the "watch for coins" button.
    public void OnWatchForCoinsTapped()
    {
        // Set before EnsureAccess, so a second tap can't open a second break.
        if (_breakOpen) return;
        _breakOpen = true;
        LevelMomentAds.EnsureAccess(placementId, access =>
        {
            // Canceled or TechnicalFailure: carry on without a break.
            if (access != EnsureSignedInResult.Ready)
            {
                _breakOpen = false;
                return;
            }
            ShowBreak();
        });
    }

    private void ShowBreak()
    {
        Time.timeScale = 0f;

        var granted = false;
        // A reward moment: 30 seconds, about 5 questions.
        var slot = new LevelMomentAdSlot("rewarded", 30, rewardAmount: coinsPerBreak);

        // A fresh handle for every break: a shown handle can't show again.
        RewardedAd.Load(placementId, new RewardedAdLoadCallbacks
        {
            OnAdLoaded = ad => ad.Show(new RewardedAdShowCallbacks
            {
                OnUserEarnedReward = reward =>
                {
                    if (granted) return;
                    granted = true;
                    // Persist reward.RewardId if a grant must survive a restart.
                    GrantCoins(coinsPerBreak);
                },
                // A break that ends without a reward grants nothing.
                OnAdDismissed = Finish,
                OnAdFailedToShow = error =>
                {
                    Debug.LogWarning("[LevelMoment] Show failed: " + error);
                    Finish();
                },
            }),
            OnAdFailedToLoad = error =>
            {
                Debug.LogWarning("[LevelMoment] Load failed: " + error);
                Finish();
            },
        }, format: "practice_set", slot: slot);
    }

    // Replace with your game's own grant.
    private void GrantCoins(int amount)
    {
        Debug.Log("[LevelMoment] Granting " + amount + " coins.");
    }

    private void Finish()
    {
        _breakOpen = false;
        Time.timeScale = 1f;
    }
}
