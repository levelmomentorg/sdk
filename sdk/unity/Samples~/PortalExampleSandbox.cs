using System;
using LevelMoment;

// Install and configure the supported WebView provider from the Unity guide.
public static class SandboxLearningBreaks
{
    // Call once at app startup.
    public static void Initialize()
    {
        var config = new LevelMomentConfig();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Sandbox only: put the token in Assets/Resources/LevelMomentSandboxToken.txt
        // and gitignore that file. Release builds never read it.
        var sandboxToken = UnityEngine.Resources.Load<UnityEngine.TextAsset>("LevelMomentSandboxToken")?.text.Trim();
        if (!string.IsNullOrEmpty(sandboxToken)) config.UnsafeTesting = new UnsafeTesting { Token = sandboxToken };
#endif
        LevelMomentAds.Initialize(config);
    }

    // Call from the player's tap. The reward fires at most once per break; if a
    // game server also grants from the webhook, record RewardId there too.
    public static void Show(Action<string> grantBonus, Action resumeGame)
    {
        LevelMomentAds.EnsureAccess("YOUR_PLACEMENT_ID", access => {
            if (access != EnsureSignedInResult.Ready) { resumeGame(); return; }
            bool earned = false;
            // One slot per moment. Level Moment fits the break to the duration.
            var slot = new LevelMomentAdSlot("rewarded", 30, rewardAmount: 1);
            // A rewarded slot asks for a practice set at least; see Quickstart, step 3.
            RewardedAd.Load("YOUR_PLACEMENT_ID", new RewardedAdLoadCallbacks {
                OnAdLoaded = ad => ad.Show(new RewardedAdShowCallbacks {
                    OnUserEarnedRewardItem = reward => { if (!earned) { earned = true; grantBonus(reward.RewardId); } },
                    OnAdDismissed = () => { ad.Destroy(); resumeGame(); },
                    OnAdFailedToShow = error => { ad.Destroy(); resumeGame(); },
                }),
                OnAdFailedToLoad = error => resumeGame(),
            }, format: "practice_set", slot: slot);
        });
    }
}
