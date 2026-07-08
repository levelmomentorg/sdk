// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — Main API
// Mirrors the Unity Ads SDK static API so integration is a near-identical swap.
//
// USAGE:
//   1. LevelMomentAds.Initialize(config)               — once, on game start
//   2. LevelMomentAds.Load(placementId, listener)      — preload in background
//   3. var q = LevelMomentAds.Show(placementId, listener) — at pause point; render q
//   4. LevelMomentAds.NotifyAnswer(placementId, idx, isCorrect, listener)
//
// Mirrors: UnityAds.Initialize / UnityAds.Load / UnityAds.Show
// See MIGRATION.md for a line-by-line swap guide.
// ---------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LevelMoment
{
    public class LevelMomentAds : MonoBehaviour
    {
        // ---- Singleton MonoBehaviour (drives coroutines) ----

        private static LevelMomentAds _instance;
        private static LevelMomentConfig _config;
        private static ImpressionQueue _queue;

        // placementId → loaded question
        private static readonly Dictionary<string, Question> _loadedAds =
            new Dictionary<string, Question>();

        // placementId → shownAt ISO-8601 timestamp
        private static readonly Dictionary<string, string> _shownAt =
            new Dictionary<string, string>();

        // ---- Public API ----

        /// <summary>
        /// Initialise the SDK. Call once from Awake() or Start() in your bootstrap scene.
        /// Mirrors: UnityAds.Initialize(gameId, testMode, initializationListener)
        /// </summary>
        public static void Initialize(LevelMomentConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            _config = config;

            if (_instance == null)
            {
                var go = new GameObject("[LevelMomentAds]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<LevelMomentAds>();
            }

            _queue = new ImpressionQueue(config.ApiUrl, config.StudentToken);
            _instance.StartCoroutine(_queue.StartFlushing());
        }

        /// <summary>
        /// Preload the next question in the background.
        /// Call while the game is running so the question is ready to show instantly.
        /// Mirrors: UnityAds.Load(adUnitId, loadListener)
        /// </summary>
        public static void Load(string placementId, ILevelMomentLoadListener listener)
        {
            if (_instance == null || _config == null)
            {
                Debug.LogError("[LevelMomentAds] Call Initialize() before Load().");
                listener?.OnLevelMomentAdFailedToLoad(placementId, LevelMomentLoadError.Unknown,
                    "LevelMomentAds not initialised. Call LevelMomentAds.Initialize() first.");
                return;
            }

            _instance.StartCoroutine(FetchQuestion(placementId, _config.StudentToken, listener));
        }

        /// <summary>
        /// Display a preloaded question — no network call, shows instantly.
        /// Returns the Question so you can render it in your game UI.
        /// Call NotifyAnswer() once the player responds.
        /// Mirrors: UnityAds.Show(adUnitId, showListener)
        /// </summary>
        /// <returns>The loaded Question, or null if not yet loaded.</returns>
        public static Question Show(string placementId, ILevelMomentShowListener listener)
        {
            if (!_loadedAds.TryGetValue(placementId, out var question))
            {
                listener?.OnLevelMomentShowFailure(
                    placementId,
                    LevelMomentShowError.NotLoaded,
                    "LevelMomentAds.Show() called before OnLevelMomentAdLoaded fired. Call Load() first.");
                return null;
            }

            var shownAt = DateTime.UtcNow.ToString("o");
            _shownAt[placementId] = shownAt;

            _queue.Enqueue(new ImpressionEvent
            {
                questionId = question.id,
                placementId = placementId,
                shownAt = shownAt,
                durationMs = 0,
            });

            listener?.OnLevelMomentShowStart(placementId);
            return question;
        }

        /// <summary>
        /// Returns the preloaded question without starting the ad break.
        /// Use this to pre-build your question UI before calling Show().
        /// </summary>
        public static Question GetLoadedQuestion(string placementId)
        {
            _loadedAds.TryGetValue(placementId, out var q);
            return q;
        }

        /// <summary>
        /// Returns true if a question is loaded and ready to show.
        /// Mirrors: UnityAds.IsReady(placementId)
        /// </summary>
        public static bool IsReady(string placementId) =>
            _loadedAds.ContainsKey(placementId);

        /// <summary>
        /// Call when the player submits an answer (correct or wrong).
        /// Fires OnLevelMomentShowComplete; always resume the game in that callback.
        /// Mirrors: fires onUserEarnedReward + OnUnityAdsShowComplete
        /// </summary>
        /// <param name="placementId">Placement that was shown.</param>
        /// <param name="selectedIndex">The option index the player chose (-1 if skipped).</param>
        /// <param name="isCorrect">True if the selected option is correct.</param>
        /// <param name="listener">The same listener passed to Show().</param>
        public static void NotifyAnswer(
            string placementId,
            int selectedIndex,
            bool isCorrect,
            ILevelMomentShowListener listener)
        {
            if (!_loadedAds.TryGetValue(placementId, out var question)) return;

            _loadedAds.Remove(placementId);
            _shownAt.TryGetValue(placementId, out var shownAt);
            _shownAt.Remove(placementId);

            var answeredAt = DateTime.UtcNow.ToString("o");

            if (_instance != null && _config != null)
            {
                _instance.StartCoroutine(SubmitAnswer(
                    question.id,
                    placementId,
                    shownAt ?? answeredAt,
                    answeredAt,
                    selectedIndex));
            }

            var state = isCorrect
                ? LevelMomentShowCompletionState.Completed
                : LevelMomentShowCompletionState.Skipped;

            listener?.OnLevelMomentShowComplete(placementId, state);
        }

        // ---- Private coroutines ----

        private static IEnumerator FetchQuestion(
            string placementId,
            string studentToken,
            ILevelMomentLoadListener listener)
        {
            var url = UrlSafety.BuildUrl(
                _config.ApiUrl,
                "/questions",
                new Dictionary<string, string> { { "placementId", placementId } });

            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", $"Bearer {studentToken}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                var code = req.responseCode == 401
                    ? LevelMomentLoadError.InvalidToken
                    : LevelMomentLoadError.NetworkError;
                listener?.OnLevelMomentAdFailedToLoad(placementId, code, req.error);
                yield break;
            }

            QuestionResponse wrapper;
            try
            {
                wrapper = JsonUtility.FromJson<QuestionResponse>(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                listener?.OnLevelMomentAdFailedToLoad(placementId, LevelMomentLoadError.Unknown, ex.Message);
                yield break;
            }

            if (wrapper?.question == null)
            {
                listener?.OnLevelMomentAdFailedToLoad(placementId, LevelMomentLoadError.NoFill,
                    "No question available for this student.");
                yield break;
            }

            _loadedAds[placementId] = wrapper.question;
            listener?.OnLevelMomentAdLoaded(placementId);
        }

        private static IEnumerator SubmitAnswer(
            string questionId,
            string placementId,
            string shownAt,
            string answeredAt,
            int selectedIndex)
        {
            var payload = new AnswerPayload
            {
                placementId = placementId,
                answers = new[]
                {
                    new AnswerItem
                    {
                        questionId = questionId,
                        shownAt = shownAt,
                        answeredAt = answeredAt,
                        selectedIndex = selectedIndex,
                    }
                }
            };

            var json = JsonUtility.ToJson(payload);
            var bytes = Encoding.UTF8.GetBytes(json);

            using var req = new UnityWebRequest(
                UrlSafety.BuildUrl(_config.ApiUrl, "/answers"),
                UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", $"Bearer {_config.StudentToken}");
            yield return req.SendWebRequest();

            // Best-effort — answer submission failure is non-critical and will not
            // block the game from resuming. Impression is already queued.
        }

        // ---- JSON helpers ----

        [Serializable]
        private class QuestionResponse { public Question question; }

        [Serializable]
        private class AnswerPayload
        {
            public string placementId;
            public AnswerItem[] answers;
        }

        [Serializable]
        private class AnswerItem
        {
            public string questionId;
            public string shownAt;
            public string answeredAt;
            public int selectedIndex;
        }
    }
}
