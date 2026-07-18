// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — Basic Integration Example (ADR-001 WebView shell).
//
// Attach to a Manager GameObject in your bootstrap scene. The SDK renders
// nothing — Show() opens the hosted /break page in a WebView and calls back on
// reward / dismiss. You only preload, show, and resume.
//
// Requires a WebView provider: install gree/unity-webview and add the
// LEVELMOMENT_GREE_WEBVIEW scripting define (see sdk/unity/README.md).
// ---------------------------------------------------------------------------

using UnityEngine;
using LevelMoment;

public class AdBreakExample : MonoBehaviour
{
    [Header("LevelMoment Config")]
    [SerializeField] private string apiUrl = "https://api.levelmoment.com";
    [SerializeField] private string breakUrl = "https://app.levelmoment.com/break";
    [SerializeField] private string placementId = "your-placement-id";
    [SerializeField] private bool mock = false;

    private RewardedAd _rewardedAd;

    private void Awake()
    {
        LevelMomentAds.Initialize(new LevelMomentConfig
        {
            ApiUrl = apiUrl,
            BreakUrl = breakUrl,
            Mock = mock,
        });

        LoadNext();
    }

    // Preload in the background so Show() is instant at the next pause point.
    private void LoadNext()
    {
        RewardedAd.Load(placementId, new RewardedAdLoadCallbacks
        {
            OnAdLoaded = ad =>
            {
                _rewardedAd = ad;
                Debug.Log("[LevelMoment] Break ready.");
            },
            OnAdFailedToLoad = error =>
            {
                _rewardedAd = null;
                Debug.LogWarning("[LevelMoment] Load failed: " + error);
            },
        });
    }

    // Call from your GameScene at a natural pause point (level complete, etc.).
    public void TriggerAdBreak()
    {
        if (_rewardedAd == null || !_rewardedAd.IsLoaded)
        {
            Debug.Log("[LevelMoment] No break ready — resuming without one.");
            return;
        }

        Time.timeScale = 0f; // pause the game while the break is up

        _rewardedAd.Show(new RewardedAdShowCallbacks
        {
            OnUserEarnedReward = amount =>
            {
                // amount == 1 → correct answer; 0 → skipped / wrong.
                if (amount == 1)
                    Debug.Log("[LevelMoment] Correct! Grant a bonus here.");
            },
            OnAdDismissed = () =>
            {
                ResumeGame();
                LoadNext(); // preload the next break
            },
            OnAdFailedToShow = error =>
            {
                Debug.LogWarning("[LevelMoment] Show failed: " + error);
                ResumeGame();
            },
        });
    }

    private void ResumeGame()
    {
        Time.timeScale = 1f;
        _rewardedAd = null;
    }
}
