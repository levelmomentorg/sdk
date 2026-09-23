using System;
using LevelMoment;

// Install and configure the supported WebView provider from the Unity guide.
public static class LearningBreaks
{
    // Call once at app startup.
    public static void Initialize() => LevelMomentAds.Initialize(new LevelMomentConfig {

    });

    // Call from the player's learning action. Persist each RewardId in your game
    // before granting so callback retries and webhooks cannot grant twice.
    public static void Show(Action<string> grantBonus, Action resumeGame)
    {
        LevelMomentAds.EnsureAccess("YOUR_PLACEMENT_ID", access => {
            if (access != EnsureSignedInResult.Ready) { resumeGame(); return; }
            bool earned = false;
            RewardedAd.Load("YOUR_PLACEMENT_ID", new RewardedAdLoadCallbacks {
                OnAdLoaded = ad => ad.Show(new RewardedAdShowCallbacks {
                    OnUserEarnedRewardItem = reward => { if (!earned) { earned = true; grantBonus(reward.RewardId); } },
                    OnAdDismissed = () => { ad.Destroy(); resumeGame(); },
                    OnAdFailedToShow = error => { ad.Destroy(); resumeGame(); },
                }),
                OnAdFailedToLoad = error => resumeGame(),
            });
        });
    }
}
