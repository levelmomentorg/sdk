// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — Basic Integration Example
//
// This MonoBehaviour shows the minimal code needed to integrate LevelMoment.
// Attach it to a Manager GameObject in your bootstrap scene.
// ---------------------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;
using LevelMoment;

/// <summary>
/// Minimal example: preload → show → render → answer → resume.
///
/// In a real game:
/// - Read the student token from the ?token= launch URL (see GetTokenFromUrl())
/// - Replace the placeholder UI references with your own question overlay
/// - Call TriggerAdBreak() from your GameScene at a natural pause point
/// </summary>
public class AdBreakExample : MonoBehaviour,
    ILevelMomentLoadListener,
    ILevelMomentShowListener
{
    [Header("LevelMoment Config")]
    [SerializeField] private string apiUrl = "https://api.levelmoment.com";
    [SerializeField] private string placementId = "your-game-id";

    [Header("Question UI (assign in Inspector)")]
    [SerializeField] private GameObject questionPanel;
    [SerializeField] private Text promptText;
    [SerializeField] private Button[] optionButtons;

    private Question _currentQuestion;

    // ---- Lifecycle ----

    private void Awake()
    {
        var token = GetTokenFromUrl();

        LevelMomentAds.Initialize(new LevelMomentConfig
        {
            ApiUrl = apiUrl,
            PlacementId = placementId,
            StudentToken = token,
        });

        // Preload immediately so the question is ready at the first pause point
        LevelMomentAds.Load(placementId, this);
    }

    // ---- Called by your GameScene at a natural pause point ----

    public void TriggerAdBreak()
    {
        if (!LevelMomentAds.IsReady(placementId))
        {
            Debug.Log("[LevelMoment] Question not ready yet — resuming game without break.");
            return;
        }

        _currentQuestion = LevelMomentAds.Show(placementId, this);
        if (_currentQuestion != null)
        {
            RenderQuestion(_currentQuestion);
        }
    }

    // ---- ILevelMomentLoadListener ----

    public void OnLevelMomentAdLoaded(string pid)
    {
        Debug.Log($"[LevelMoment] Question ready for {pid}.");
        // Optionally preload the next one now so it's ready after this break
    }

    public void OnLevelMomentAdFailedToLoad(string pid, LevelMomentLoadError error, string message)
    {
        Debug.LogWarning($"[LevelMoment] Load failed ({error}): {message}");
        // No question available — skip the ad break and resume the game
    }

    // ---- ILevelMomentShowListener ----

    public void OnLevelMomentShowStart(string pid)
    {
        Debug.Log($"[LevelMoment] Ad break started for {pid}.");
        // Pause game time, blur background, etc.
        Time.timeScale = 0f;
    }

    public void OnLevelMomentShowComplete(string pid, LevelMomentShowCompletionState state)
    {
        HideQuestion();
        Time.timeScale = 1f;     // Always resume the game here

        if (state == LevelMomentShowCompletionState.Completed)
        {
            Debug.Log("[LevelMoment] Correct answer! Grant bonus if desired.");
            // e.g. player.AddCoins(5);
        }

        // Preload the next question immediately for the next break
        LevelMomentAds.Load(placementId, this);
    }

    public void OnLevelMomentShowFailure(string pid, LevelMomentShowError error, string message)
    {
        Debug.LogWarning($"[LevelMoment] Show failed ({error}): {message}");
        Time.timeScale = 1f;
    }

    // ---- Question rendering ----

    private void RenderQuestion(Question question)
    {
        if (question.meta == null || question.meta.type != "static") return;

        questionPanel.SetActive(true);
        promptText.text = question.meta.prompt;

        for (var i = 0; i < optionButtons.Length; i++)
        {
            var index = i; // capture for lambda
            optionButtons[i].gameObject.SetActive(i < question.meta.options.Length);

            if (i < question.meta.options.Length)
            {
                optionButtons[i].GetComponentInChildren<Text>().text = question.meta.options[i];
                optionButtons[i].onClick.RemoveAllListeners();
                optionButtons[i].onClick.AddListener(() => OnOptionSelected(index));
            }
        }
    }

    private void OnOptionSelected(int selectedIndex)
    {
        var isCorrect = _currentQuestion?.meta != null &&
                        selectedIndex == _currentQuestion.meta.correctIndex;

        LevelMomentAds.NotifyAnswer(placementId, selectedIndex, isCorrect, this);
    }

    private void HideQuestion()
    {
        questionPanel.SetActive(false);
    }

    // ---- Helpers ----

    private static string GetTokenFromUrl()
    {
        // On WebGL, read from Application.absoluteURL
        // On mobile, read from the deep link URL
        try
        {
            var url = Application.absoluteURL;
            if (!string.IsNullOrEmpty(url))
            {
                var uri = new Uri(url);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                return query["token"] ?? string.Empty;
            }
        }
        catch
        {
            // Non-WebGL or no URL available
        }

        return string.Empty;
    }
}
