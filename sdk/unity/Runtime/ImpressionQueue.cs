// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — Impression Queue
// Buffers impression events in PlayerPrefs and flushes them to the API in
// the background. Ensures no impressions (= developer CPM payments) are
// dropped on network interruption.
//
// This is an internal implementation detail — game code uses LevelMomentAds.
// ---------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LevelMoment
{
    internal class ImpressionQueue
    {
        private const string PrefsKey = "levelmoment_impression_queue";

        private readonly string _apiUrl;
        private readonly string _studentToken;

        public ImpressionQueue(string apiUrl, string studentToken)
        {
            _apiUrl = apiUrl;
            _studentToken = studentToken;
        }

        public void Enqueue(ImpressionEvent evt)
        {
            var events = Load();
            events.Add(evt);
            Save(events);
        }

        /// <summary>
        /// Coroutine that flushes the queue every <paramref name="intervalSeconds"/> seconds.
        /// Start via MonoBehaviour.StartCoroutine() and keep it running for the session.
        /// </summary>
        public IEnumerator StartFlushing(float intervalSeconds = 10f)
        {
            while (true)
            {
                yield return new WaitForSeconds(intervalSeconds);
                yield return TryFlush();
            }
        }

        private IEnumerator TryFlush()
        {
            var events = Load();
            if (events.Count == 0) yield break;

            var batch = new ImpressionBatch { impressions = events.ToArray() };
            var json = JsonUtility.ToJson(batch);

            // Optimistic clear — events are re-enqueued on failure via a future flush
            Save(new List<ImpressionEvent>());

            var bytes = Encoding.UTF8.GetBytes(json);
            using var req = new UnityWebRequest(
                UrlSafety.BuildUrl(_apiUrl, "/impressions"),
                UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", $"Bearer {_studentToken}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // Re-enqueue all events so they are retried next interval
                var pending = Load();
                pending.AddRange(events);
                Save(pending);
            }
        }

        private List<ImpressionEvent> Load()
        {
            var json = PlayerPrefs.GetString(PrefsKey, "[]");
            try
            {
                var batch = JsonUtility.FromJson<ImpressionBatch>($"{{\"impressions\":{json}}}");
                return batch?.impressions != null
                    ? new List<ImpressionEvent>(batch.impressions)
                    : new List<ImpressionEvent>();
            }
            catch
            {
                return new List<ImpressionEvent>();
            }
        }

        private void Save(List<ImpressionEvent> events)
        {
            var batch = new ImpressionBatch { impressions = events.ToArray() };
            var full = JsonUtility.ToJson(batch);
            // Store only the array portion so parsing is symmetric
            var start = full.IndexOf('[');
            var end = full.LastIndexOf(']') + 1;
            var arrayJson = start >= 0 ? full.Substring(start, end - start) : "[]";
            PlayerPrefs.SetString(PrefsKey, arrayJson);
            PlayerPrefs.Save();
        }

        [Serializable]
        private class ImpressionBatch
        {
            public ImpressionEvent[] impressions;
        }
    }
}
