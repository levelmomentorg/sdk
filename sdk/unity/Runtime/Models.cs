// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — Domain models
// Mirrors the types in sdk/core/src/types.ts
// ---------------------------------------------------------------------------

using System;
using UnityEngine;

namespace LevelMoment
{
    // -------------------------------------------------------------------------
    // Config — passed to LevelMomentAds.Initialize()
    // -------------------------------------------------------------------------

    [Serializable]
    public class LevelMomentConfig
    {
        /// <summary>Base URL of the LevelMoment API. Injected at build time.</summary>
        public string ApiUrl;

        /// <summary>
        /// The game's identifier from the developer portal.
        /// Equivalent to Unity Ads' gameId / AdMob's adUnitId.
        /// </summary>
        public string PlacementId;

        /// <summary>
        /// Short-lived session token issued by the parent portal. The SDK
        /// sends it only via the <c>Authorization: Bearer</c> header;
        /// <see cref="UrlSafety"/> enforces that it never lands in a URL.
        /// </summary>
        public string StudentToken;
    }

    // -------------------------------------------------------------------------
    // Question — returned by GetLoadedQuestion(); game renders it
    // -------------------------------------------------------------------------

    [Serializable]
    public class Question
    {
        public string id;
        public string generationType;   // "static" | "ai-generated"
        public QuestionMeta meta;
    }

    [Serializable]
    public class QuestionMeta
    {
        public string type;             // "static" | "ai-generated"

        // Static question fields (type == "static")
        public string prompt;
        public string[] options;
        public int correctIndex;

        // Common fields
        public string subject;
        public string level;
    }

    // -------------------------------------------------------------------------
    // Error enums — mirror Unity Ads' UnityAdsLoadError / UnityAdsShowError
    // -------------------------------------------------------------------------

    public enum LevelMomentLoadError
    {
        NetworkError,
        NoFill,         // No question available for this student
        InvalidToken,   // Student session token expired or invalid
        Unknown,
    }

    public enum LevelMomentShowError
    {
        NotLoaded,      // Show() called before Load() completed
        Unknown,
    }

    /// <summary>
    /// Mirrors UnityAdsShowCompletionState.
    /// Completed = correct answer; Skipped = wrong answer or dismissed.
    /// </summary>
    public enum LevelMomentShowCompletionState
    {
        Completed,  // onUserEarnedReward equivalent (amount: 1)
        Skipped,    // wrong answer or dismissed      (amount: 0)
    }

    // -------------------------------------------------------------------------
    // Listener interfaces — mirror IUnityAdsLoadListener / IUnityAdsShowListener
    // -------------------------------------------------------------------------

    public interface ILevelMomentLoadListener
    {
        /// <summary>Fired when the question is fetched and ready to show.</summary>
        void OnLevelMomentAdLoaded(string placementId);

        /// <summary>Fired when the question could not be fetched.</summary>
        void OnLevelMomentAdFailedToLoad(string placementId, LevelMomentLoadError error, string message);
    }

    public interface ILevelMomentShowListener
    {
        /// <summary>Fired when the question UI is presented to the player.</summary>
        void OnLevelMomentShowStart(string placementId);

        /// <summary>
        /// Fired when the ad break ends.
        /// completionState == Completed → correct answer (grant bonus here).
        /// completionState == Skipped   → wrong/dismissed (always resume game here).
        /// </summary>
        void OnLevelMomentShowComplete(string placementId, LevelMomentShowCompletionState completionState);

        /// <summary>Fired if Show() is called before Load() completes.</summary>
        void OnLevelMomentShowFailure(string placementId, LevelMomentShowError error, string message);
    }

    // -------------------------------------------------------------------------
    // Internal — impression event for the queue
    // -------------------------------------------------------------------------

    [Serializable]
    internal class ImpressionEvent
    {
        public string questionId;
        public string placementId;
        public string shownAt;
        public long durationMs;
    }
}
