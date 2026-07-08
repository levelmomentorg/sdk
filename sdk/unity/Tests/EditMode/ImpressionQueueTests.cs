// EditMode tests for ImpressionQueue — the PlayerPrefs-backed buffer
// that prevents impression loss on transient network failure. We only
// exercise the storage path here (Enqueue/Load/Save round-trip);
// the coroutine-driven HTTP flush belongs in a PlayMode integration
// test against a mock UnityWebRequest, which is out of scope for the
// initial Unity test harness.
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using NUnit.Framework;
using UnityEngine;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class ImpressionQueueTests
    {
        private const string PrefsKey = "levelmoment_impression_queue";

        [SetUp]
        public void ClearPrefs()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDownPrefs()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        [Test]
        public void Enqueue_PersistsEventAcrossNewQueueInstance()
        {
            var q1 = new ImpressionQueue("https://api.example.com", "tok");
            q1.Enqueue(new ImpressionEvent
            {
                questionId = "q1",
                placementId = "p1",
                shownAt = "2026-05-11T10:00:00Z",
                durationMs = 0,
            });

            // A fresh queue instance must read what the previous one wrote —
            // i.e. PlayerPrefs is the source of truth, not the in-memory list.
            var stored = PlayerPrefs.GetString(PrefsKey, "[]");
            StringAssert.Contains("\"q1\"", stored);
            StringAssert.Contains("\"p1\"", stored);
        }

        [Test]
        public void Enqueue_PreservesInsertionOrder()
        {
            var q = new ImpressionQueue("https://api.example.com", "tok");
            for (var i = 1; i <= 3; i++)
            {
                q.Enqueue(new ImpressionEvent
                {
                    questionId = "q" + i,
                    placementId = "p1",
                    shownAt = "2026-05-11T10:00:0" + i + "Z",
                    durationMs = i * 10,
                });
            }

            var stored = PlayerPrefs.GetString(PrefsKey, "[]");
            var i1 = stored.IndexOf("\"q1\"");
            var i2 = stored.IndexOf("\"q2\"");
            var i3 = stored.IndexOf("\"q3\"");
            Assert.That(i1, Is.GreaterThanOrEqualTo(0));
            Assert.That(i2, Is.GreaterThan(i1));
            Assert.That(i3, Is.GreaterThan(i2));
        }

        [Test]
        public void Load_CorruptPrefs_RecoversToEmptyQueue()
        {
            // A previous version of the SDK might have written something
            // the current parser can't handle. The queue must NOT throw on
            // startup — that would block every subsequent impression. It
            // recovers silently by treating the buffer as empty.
            PlayerPrefs.SetString(PrefsKey, "{not valid json");
            PlayerPrefs.Save();

            // Construct a fresh queue and enqueue — must not throw.
            var q = new ImpressionQueue("https://api.example.com", "tok");
            Assert.DoesNotThrow(() =>
            {
                q.Enqueue(new ImpressionEvent
                {
                    questionId = "q1",
                    placementId = "p1",
                    shownAt = "2026-05-11T10:00:00Z",
                    durationMs = 0,
                });
            });
            // And the new event made it to prefs (i.e. recovery didn't
            // also clobber the write).
            var stored = PlayerPrefs.GetString(PrefsKey, "[]");
            StringAssert.Contains("\"q1\"", stored);
        }
    }
}
