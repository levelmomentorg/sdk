// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — hidden runtime driver.
//
// A single persistent MonoBehaviour, created lazily on the first Show(), that
// ticks each active break's load watchdog once per frame. This is the only
// per-frame glue in the SDK; all timing decisions live in the pure LoadWatchdog
// (which the EditMode tests drive directly). Untestable in EditMode by design —
// the tests set RewardedAd.SkipRuntimeDriver = true and tick manually.
// ---------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace LevelMoment
{
    internal class LevelMomentRuntime : MonoBehaviour
    {
        private static LevelMomentRuntime _instance;

        private readonly List<RewardedAd> _active = new List<RewardedAd>();

        public static void Track(RewardedAd ad)
        {
            EnsureInstance();
            if (!_instance._active.Contains(ad))
                _instance._active.Add(ad);
        }

        public static void Untrack(RewardedAd ad)
        {
            if (_instance != null)
                _instance._active.Remove(ad);
        }

        private static void EnsureInstance()
        {
            if (_instance != null)
                return;
            var go = new GameObject("[LevelMomentRuntime]");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LevelMomentRuntime>();
        }

        private void Update()
        {
            // Iterate backwards: Tick() may terminate a break, which calls
            // Untrack() and removes it from the list mid-loop.
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (i < _active.Count)
                    _active[i].Tick();
            }
        }
    }
}
