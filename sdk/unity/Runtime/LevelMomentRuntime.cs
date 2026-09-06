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
    /// <summary>
    /// Anything with a load watchdog that needs a per-frame nudge: a break, and
    /// the sign-in gate. Explicitly implemented so the per-frame plumbing stays
    /// off the public API of either.
    /// </summary>
    internal interface ITickable
    {
        void Tick();
    }

    internal class LevelMomentRuntime : MonoBehaviour
    {
        private static LevelMomentRuntime _instance;

        // EditMode tests set this true to skip creating the MonoBehaviour
        // driver; they drive Tick() manually with an injected clock instead.
        internal static bool SkipDriver;

        private readonly List<ITickable> _active = new List<ITickable>();

        public static void Track(ITickable tickable)
        {
            if (SkipDriver)
                return;
            EnsureInstance();
            if (!_instance._active.Contains(tickable))
                _instance._active.Add(tickable);
        }

        public static void Untrack(ITickable tickable)
        {
            if (_instance != null)
                _instance._active.Remove(tickable);
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
