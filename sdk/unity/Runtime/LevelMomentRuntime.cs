// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — hidden runtime driver.
//
// A single persistent MonoBehaviour, created lazily the first time anything
// needs a per-frame nudge, that ticks every registered ITickable once per
// frame: each active break's load watchdog, the sign-in gate, and the MAX
// compatibility facade's event pump. All timing decisions live in the pure
// LoadWatchdog (which the EditMode tests drive directly).
//
// The registry and the per-frame pass are static and testable: EditMode tests
// set SkipDriver = true, which suppresses only the MonoBehaviour, and call
// TickAll() where a frame would have passed. Creating the MonoBehaviour is
// the one part EditMode cannot reach.
// ---------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace LevelMoment
{
    /// <summary>
    /// Anything that needs a per-frame nudge: a break's load watchdog, the
    /// sign-in gate, and the MAX facade's event pump. Explicitly implemented
    /// so the per-frame plumbing stays off the public API of any of them.
    /// A Tick() may run game code (the pump raises game callbacks), so it can
    /// track or untrack anything, not only itself — see TickAll.
    /// </summary>
    internal interface ITickable
    {
        void Tick();
    }

    internal class LevelMomentRuntime : MonoBehaviour
    {
        private static LevelMomentRuntime _instance;

        // EditMode tests set this true to skip creating the MonoBehaviour
        // driver; they call TickAll() (or a surface's own Tick) manually with
        // an injected clock instead. The registry below is still maintained,
        // so the tests exercise the same registration path a game does.
        internal static bool SkipDriver;

        private static readonly List<ITickable> _active = new List<ITickable>();

        // Reused across frames so a per-frame pass allocates nothing.
        private static readonly List<ITickable> _tickScratch = new List<ITickable>();
        private static bool _ticking;

        public static void Track(ITickable tickable)
        {
            if (!_active.Contains(tickable))
                _active.Add(tickable);
            if (!SkipDriver)
                EnsureInstance();
        }

        public static void Untrack(ITickable tickable)
        {
            _active.Remove(tickable);
        }

        internal static bool IsTracked(ITickable tickable)
        {
            return _active.Contains(tickable);
        }

        internal static int TrackedCount
        {
            get { return _active.Count; }
        }

        /// <summary>
        /// One frame's worth of ticks. Iterates a snapshot, skipping anything
        /// untracked part-way through the pass: a Tick() runs game code, which
        /// can end an unrelated break (untracking an entry this pass has not
        /// reached) or register a new one (which waits for the next frame).
        /// Not re-entrant — a nested call is dropped rather than corrupting
        /// the pass in progress.
        /// </summary>
        internal static void TickAll()
        {
            if (_ticking)
                return;
            _ticking = true;
            try
            {
                _tickScratch.Clear();
                _tickScratch.AddRange(_active);
                for (var i = 0; i < _tickScratch.Count; i++)
                {
                    var tickable = _tickScratch[i];
                    if (_active.Contains(tickable))
                        tickable.Tick();
                }
            }
            finally
            {
                _tickScratch.Clear();
                _ticking = false;
            }
        }

        /// <summary>Test-only reset: forget every registered tickable.</summary>
        internal static void ResetForTests()
        {
            ResetRegistry();
        }

        // Shared by the test reset and the runtime init hook below, so a
        // test-only line added to ResetForTests cannot reach a player build.
        private static void ResetRegistry()
        {
            _active.Clear();
            _tickScratch.Clear();
            _ticking = false;
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

#if UNITY_5_3_OR_NEWER
        // The registry is static, so a play session with Domain Reload
        // disabled would otherwise start with the previous session's
        // tickables still registered and its driver GameObject already
        // destroyed.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticStateOnSubsystemRegistration()
        {
            ResetRegistry();
            _instance = null;
        }
#endif

        private void Update()
        {
            TickAll();
        }
    }
}
