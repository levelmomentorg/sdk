// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — AppLovin MAX-shaped compatibility facade.
//
// WHY: Level Moment's outreach is scoped to one migration pair — Unity games
// monetized through AppLovin MAX. A mechanical inspection of 179 App Store
// game bundles found Unity in 81% of ad-supported games, and among Unity
// games MAX leads AdMob and Unity LevelPlay roughly 2:1. This facade makes
// that migration a find-and-replace: a game whose code calls
// `MaxSdk.LoadInterstitial(id)` / `MaxSdk.ShowInterstitial(id)` and
// subscribes to `MaxSdkCallbacks.Interstitial.OnAdLoadedEvent` switches to
// Level Moment by changing the type prefixes (`MaxSdk` -> `LevelMomentMaxSdk`,
// `MaxSdkCallbacks` -> `LevelMomentMaxSdkCallbacks`) and the ad-unit ids,
// keeping their existing control flow and callback bodies.
//
// This file is a CONSUMER of the existing handle-based public API
// (InterstitialAd/RewardedAd/BreakSurfaceCore) — it does not change their
// behavior. It has ZERO dependency on the AppLovin MAX plugin: every type it
// exposes is defined in this package (see MaxCompatTypes.cs); the package
// compiles in a project that has never installed MAX.
//
// EVENT DELIVERY IS DEFERRED TO A FRAME TICK, as MAX's is. MAX's load is a
// network call, so a MAX game's callbacks always arrive on some later frame,
// and MAX games are written accordingly: `ShowX` from inside
// `OnAdLoadedEvent`, `LoadX` from inside a failure callback. This facade's
// underlying work is synchronous, so raising the MAX-shaped events in line
// would run those handlers INSIDE the Load/Show call they came from, and a
// handler that calls back in would re-enter the facade mid-call. Instead,
// every event is queued on MaxEventPump and raised from its Tick(), driven
// once per frame by the SDK's existing LevelMomentRuntime driver (the same
// driver that ticks each break's load watchdog). Consequences:
//   - No public call of this facade ever raises an event before it returns.
//     `LoadX`/`ShowX` called from a handler is a fresh top-level call on a
//     later frame, so there is no re-entrancy to guard and no deferral
//     machinery to get wrong.
//   - Events are delivered in FIFO order, and a Tick() delivers only what
//     was queued before it started — an event a handler queues lands on the
//     next tick. So a handler that unconditionally calls back in costs one
//     more event next frame instead of another stack frame now. The queue
//     itself is not bounded: handlers with a fan-out above one (two
//     subscribers each retrying the same failing call) still grow the
//     backlog every frame, which MaxEventPump warns about once.
//   - `IsInterstitialReady`/`IsRewardedAdReady` are unaffected: they read
//     the handle's current state synchronously, so the state a handler sees
//     is always the state as of the moment it runs.
//
// LIFECYCLE BRIDGE (MAX is id-keyed + re-loadable; our core is
// one-show-per-handle):
//   - Each mapped ad-unit id owns at most one live handle.
//     IsInterstitialReady/IsRewardedAdReady delegate to the handle's own
//     IsLoaded, which already goes false the instant Show() is called and
//     stays false until the next Load() — matching MAX, which never
//     auto-reloads on its own.
//   - A Load() replaces the ad unit's handle only once the NEW load
//     succeeds: the previous handle is Destroy()ed then, not before. A
//     failed reload therefore leaves whatever was already loaded still
//     ready to show, instead of discarding a good ad for a bad reload.
//   - A Load() called while that ad unit's ad is currently on screen does
//     NOT touch the live handle (destroying it mid-break would strand the
//     learner and never resume the game). It is queued instead; when the
//     show ends the queued load runs through the SAME public entry point a
//     game would call (LoadInterstitial/LoadRewardedAd), so if a show is
//     somehow live again by then it re-queues rather than destroying the
//     live handle. It fires its own OnAdLoadedEvent/OnAdLoadFailedEvent,
//     after that show's OnAdHiddenEvent (the queue is FIFO). Only the latest
//     queued Load per ad unit survives.
//   - A Show() while that ad unit's ad is ALREADY on screen is refused with
//     OnAdDisplayFailedEvent("already_showing", ...) BEFORE anything else
//     runs — before the handle lookup mutates any state, and in particular
//     before the "which handle is currently showing" record or the
//     rewarded once-per-Show grant flag are touched. Getting this ordering
//     wrong (checking after mutating) is exactly how a duplicate Show used
//     to re-enable destroying the live handle out from under the learner,
//     and reset the reward grant flag so a second correct answer could fire
//     a second reward — both fixed by checking first.
//   - Each ad unit's Show() records which handle opened it
//     (`_showingInterstitialHandle`/`_showingRewardedHandle`). The show-end
//     handler is a no-op unless the handle reporting the end is the one on
//     record — a stale or unrelated handle's completion can never touch the
//     live show's state.
//   - A second Show() on the same ad unit AFTER its show has already ended,
//     without an intervening Load(), hits BreakSurfaceCore's own
//     "already shown" guard (OnAdDisplayFailedEvent("not_loaded", ...)) —
//     a different case from "already_showing" above: the ad unit is no
//     longer showing (`_interstitialShowing`/`_rewardedShowing` no longer
//     contains it), just holding a handle that already spent its one show.
//
// THREADING: every call here runs on the caller's thread, the same as
// InterstitialAd/RewardedAd/BreakSurfaceCore — no new threading model. The
// queued events are raised on the frame tick, which in Unity is the main
// thread too. A subscriber exception is caught per-subscriber (see
// LevelMomentMaxSdkCallbacks.RaiseSafely) so one broken game-side handler
// cannot stop the others from running or escape into the pump.
//
// REWARD SEMANTICS: MAX's OnAdReceivedRewardEvent fires at most once per
// Show — grant one reward when the ad finishes. The underlying
// RewardedAdShowCallbacks.OnUserEarnedRewardItem instead fires once per
// GRADED ANSWER (including amount 0 for a wrong answer), because a break can
// contain several questions. To keep a migrated callback body's grant-once
// assumption true, ShowRewardedAd raises OnAdReceivedRewardEvent for at most
// the FIRST answer with Amount > 0 per Show, and never for a wrong answer;
// the once-flag resets only when a genuinely new Show actually starts (see
// the "already_showing" ordering note above — NOT on a duplicate Show call
// while one is already in progress). Per-answer granularity is still
// available — it just isn't behind the MAX-shaped event; see README.md.
//
// NOT PROVIDED: banner/MREC (MAX's `CreateBanner`/`CreateMRec` and friends),
// `OnAdClickedEvent`/`OnAdRevenuePaidEvent`/`OnExpiredAdReloadedEvent`/
// `OnAdReviewCreativeIdGeneratedEvent` (all describe mediation-waterfall data
// Level Moment has none of — see MaxCompatTypes.cs), and MAX's `SetSdkKey`
// (there is no SDK key; call `LevelMoment.LevelMomentAds.Initialize(...)`
// yourself before `LevelMomentMaxSdk.InitializeSdk()`, exactly as MAX
// requires `SetSdkKey` before `InitializeSdk`).
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace LevelMoment.Compat.Max
{
    /// <summary>
    /// The facade's outbound event queue: every MAX-shaped event is enqueued
    /// here and raised from <see cref="Tick"/>, which the SDK's existing
    /// per-frame driver (<see cref="LevelMomentRuntime"/>) calls once a frame
    /// while anything is queued. See LevelMomentMaxSdk.cs's header.
    /// </summary>
    internal sealed class MaxEventPump : ITickable
    {
        internal static readonly MaxEventPump Instance = new MaxEventPump();

        // A backlog this long means handlers are enqueueing faster than the
        // pump drains — see the warning text below.
        private const int BacklogWarnThreshold = 512;

        private readonly Queue<Action> _queue = new Queue<Action>();
        private bool _warnedBacklog;

        private MaxEventPump()
        {
        }

        /// <summary>Queue one event for the next tick.</summary>
        internal void Enqueue(Action raise)
        {
            _queue.Enqueue(raise);
            // Track() is idempotent; it registers with the per-frame driver
            // and creates that driver on demand.
            LevelMomentRuntime.Track(this);
            WarnOnceIfBacklogged();
        }

        /// <summary>Events waiting for a tick. Test/diagnostic use.</summary>
        internal int PendingCount
        {
            get { return _queue.Count; }
        }

        /// <summary>
        /// Raise every event queued BEFORE this tick started, in order. An
        /// event a handler queues while this runs waits for the next tick, so
        /// a handler that calls back into the facade never recurses and never
        /// grows the stack. What it does NOT bound is the queue: each
        /// delivery enqueues however much the handlers ask for, so a handler
        /// set with a fan-out above one (two subscribers that each retry the
        /// same failing call, or one that retries twice) makes the backlog
        /// grow every frame. <see cref="WarnOnceIfBacklogged"/> reports that.
        /// </summary>
        public void Tick()
        {
            var queuedBeforeThisTick = _queue.Count;
            for (var i = 0; i < queuedBeforeThisTick && _queue.Count > 0; i++)
            {
                // Each queued action raises through RaiseSafely, which is
                // already per-subscriber exception-isolated. The catch is for
                // anything else that might be queued here later: one failed
                // event must not cost the rest of this flush its turn.
                var raise = _queue.Dequeue();
                try
                {
                    raise();
                }
                catch (Exception ex)
                {
                    LogPumpException(ex);
                }
            }

            if (_queue.Count == 0)
                LevelMomentRuntime.Untrack(this);
        }

        /// <summary>Drop every queued event and stop being ticked.</summary>
        internal void Reset()
        {
            _queue.Clear();
            _warnedBacklog = false;
            LevelMomentRuntime.Untrack(this);
        }

        private void WarnOnceIfBacklogged()
        {
            if (_warnedBacklog || _queue.Count < BacklogWarnThreshold)
                return;
            _warnedBacklog = true;
            LogWarning(
                "LevelMomentMaxSdk: " + _queue.Count + " callbacks are waiting to be delivered. A handler is " +
                "queueing more than it consumes — typically two subscribers that each retry the same failing " +
                "Load/Show, or one that retries twice. Retry with a delay or a cap instead.");
        }

        private static void LogWarning(string message)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogWarning(message);
#else
            System.Diagnostics.Debug.WriteLine("[WARN] " + message);
#endif
        }

        private static void LogPumpException(Exception ex)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogException(ex);
#else
            System.Diagnostics.Debug.WriteLine(ex);
#endif
        }
    }

    /// <summary>
    /// Static facade keyed by ad-unit-id string, mirroring
    /// <c>AppLovin.MaxSdk</c>. See the file header for the full design.
    /// </summary>
    public static class LevelMomentMaxSdk
    {
        // adUnitId -> the Level Moment placementId/format a game registered
        // for it. Never guessed: LoadInterstitial/LoadRewardedAd on an
        // unmapped ad unit reports OnAdLoadFailedEvent("unmapped_ad_unit",
        // ...) instead of picking one.
        private static readonly Dictionary<string, string> _placementByAdUnit =
            new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _formatByAdUnit =
            new Dictionary<string, string>();

        // At most one live handle per ad unit, independently for each format.
        private static readonly Dictionary<string, InterstitialAd> _interstitials =
            new Dictionary<string, InterstitialAd>();
        private static readonly Dictionary<string, RewardedAd> _rewarded =
            new Dictionary<string, RewardedAd>();

        // Ad units whose ad is currently on screen. A Load() for one of
        // these is queued rather than run immediately — see LoadInterstitial.
        private static readonly HashSet<string> _interstitialShowing = new HashSet<string>();
        private static readonly HashSet<string> _rewardedShowing = new HashSet<string>();

        // Which handle opened the current show for an ad unit, if any — the
        // show-end handler ignores a completion that isn't from this handle.
        private static readonly Dictionary<string, InterstitialAd> _showingInterstitialHandle =
            new Dictionary<string, InterstitialAd>();
        private static readonly Dictionary<string, RewardedAd> _showingRewardedHandle =
            new Dictionary<string, RewardedAd>();

        // At most one queued Load per ad unit, flushed when its show ends.
        private static readonly HashSet<string> _interstitialLoadQueued = new HashSet<string>();
        private static readonly HashSet<string> _rewardedLoadQueued = new HashSet<string>();

        // Set once OnAdReceivedRewardEvent has been queued for the ad unit's
        // current Show; cleared only when a genuinely new Show starts.
        private static readonly HashSet<string> _rewardedGranted = new HashSet<string>();

        private static bool _warnedIgnoredShowParams;

        // ---- Ad-unit mapping --------------------------------------------------

        /// <summary>
        /// Register the Level Moment placement a MAX-shaped ad-unit id maps
        /// to. Call once per ad unit, before Load. This has no MAX
        /// equivalent — MAX's ad-unit ids are configured server-side in its
        /// dashboard; Level Moment has no such registry, so the mapping is
        /// explicit and local instead of guessed from the id string.
        /// Rejects (logs an error and does not register) an empty
        /// <paramref name="adUnitId"/> or <paramref name="placementId"/> here
        /// rather than deferring to a confusing failure the first time the
        /// ad unit is loaded.
        /// </summary>
        /// <param name="format">
        /// Level Moment break format (<c>flashcard</c>/<c>quiz</c>/
        /// <c>deep_dive</c>) to load for this ad unit. MAX has no equivalent
        /// parameter; defaults to <c>flashcard</c> so a straight port needs
        /// no changes.
        /// </param>
        public static void MapAdUnit(string adUnitId, string placementId, string format = "flashcard")
        {
            if (string.IsNullOrEmpty(adUnitId))
            {
                LogError("LevelMomentMaxSdk.MapAdUnit: adUnitId must not be empty.");
                return;
            }
            if (string.IsNullOrEmpty(placementId))
            {
                LogError("LevelMomentMaxSdk.MapAdUnit: placementId must not be empty for ad unit '" + adUnitId + "'.");
                return;
            }
            _placementByAdUnit[adUnitId] = placementId;
            _formatByAdUnit[adUnitId] = string.IsNullOrEmpty(format) ? "flashcard" : format;
        }

        private static bool TryResolvePlacement(string adUnitId, out string placementId, out string format)
        {
            format = "flashcard";
            if (string.IsNullOrEmpty(adUnitId) || !_placementByAdUnit.TryGetValue(adUnitId, out placementId))
            {
                placementId = null;
                return false;
            }
            string mappedFormat;
            if (_formatByAdUnit.TryGetValue(adUnitId, out mappedFormat))
                format = mappedFormat;
            return true;
        }

        // ---- SDK init -----------------------------------------------------

        /// <summary>
        /// Mirrors <c>MaxSdk.InitializeSdk(string[])</c>. There is no
        /// mediation handshake to wait on, but
        /// <see cref="LevelMomentMaxSdkCallbacks.OnSdkInitializedEvent"/>
        /// still arrives on a later frame rather than inside this call, as
        /// MAX's does — so subscribing right after this call still works.
        /// <paramref name="adUnitIds"/> is accepted for signature
        /// compatibility and ignored: MAX uses it to preload network adapters
        /// for those ad units; Level Moment has no adapters to preload.
        ///
        /// Call <c>LevelMoment.LevelMomentAds.Initialize(...)</c> first — this
        /// method does not configure endpoints or credentials itself. A
        /// Load() before that call still fails cleanly through
        /// OnAdLoadFailedEvent("not_initialized", ...); it is not required to
        /// call InitializeSdk() first the way MAX requires it.
        /// </summary>
        public static void InitializeSdk(string[] adUnitIds = null)
        {
            LevelMomentMaxSdkCallbacks.RaiseOnSdkInitializedEvent();
        }

        // ---- Interstitial ---------------------------------------------------

        /// <summary>
        /// Mirrors <c>MaxSdk.LoadInterstitial(string)</c>. Raises no event
        /// before it returns — see the file header.
        /// </summary>
        public static void LoadInterstitial(string adUnitId)
        {
            if (string.IsNullOrEmpty(adUnitId))
            {
                LogError("LevelMomentMaxSdk.LoadInterstitial: adUnitId must not be null or empty.");
                LevelMomentMaxSdkCallbacks.Interstitial.RaiseOnAdLoadFailedEvent(adUnitId, InvalidAdUnitIdError());
                return;
            }

            if (_interstitialShowing.Contains(adUnitId))
            {
                // Never destroy a handle that is currently on screen —
                // queue the load for when the show ends instead.
                _interstitialLoadQueued.Add(adUnitId);
                return;
            }

            string placementId;
            string format;
            if (!TryResolvePlacement(adUnitId, out placementId, out format))
            {
                LevelMomentMaxSdkCallbacks.Interstitial.RaiseOnAdLoadFailedEvent(
                    adUnitId, UnmappedError());
                return;
            }

            InterstitialAd.Load(placementId, new InterstitialAdLoadCallbacks
            {
                OnAdLoaded = ad =>
                {
                    // Destroy the previous handle only now that the new one
                    // is ready — a failed reload must not discard an ad that
                    // is still loaded and showable.
                    InterstitialAd previous;
                    if (_interstitials.TryGetValue(adUnitId, out previous) && previous != null && !ReferenceEquals(previous, ad))
                        previous.Destroy();
                    _interstitials[adUnitId] = ad;
                    LevelMomentMaxSdkCallbacks.Interstitial.RaiseOnAdLoadedEvent(
                        adUnitId, new AdInfo(adUnitId));
                },
                OnAdFailedToLoad = error =>
                {
                    LevelMomentMaxSdkCallbacks.Interstitial.RaiseOnAdLoadFailedEvent(
                        adUnitId, new ErrorInfo(error));
                },
            }, format);
        }

        /// <summary>
        /// Mirrors <c>MaxSdk.IsInterstitialReady(string)</c>. A synchronous
        /// read of current state, not a queued event.
        /// </summary>
        public static bool IsInterstitialReady(string adUnitId)
        {
            InterstitialAd ad;
            return adUnitId != null
                && _interstitials.TryGetValue(adUnitId, out ad)
                && ad != null
                && ad.IsLoaded;
        }

        /// <summary>
        /// Mirrors <c>MaxSdk.ShowInterstitial(string, string, string)</c>.
        /// <paramref name="placement"/>/<paramref name="customData"/> are
        /// accepted for signature compatibility and ignored — see
        /// README.md → "AppLovin MAX compatibility facade" for what that
        /// means at runtime. Raises no event before it returns.
        /// </summary>
        public static void ShowInterstitial(string adUnitId, string placement = null, string customData = null)
        {
            WarnIfIgnoredShowParams(placement, customData);

            if (string.IsNullOrEmpty(adUnitId))
            {
                LogError("LevelMomentMaxSdk.ShowInterstitial: adUnitId must not be null or empty.");
                LevelMomentMaxSdkCallbacks.Interstitial.RaiseOnAdDisplayFailedEvent(
                    adUnitId, InvalidAdUnitIdError(), new AdInfo(adUnitId));
                return;
            }

            InterstitialAd ad;
            if (!_interstitials.TryGetValue(adUnitId, out ad) || ad == null)
            {
                LevelMomentMaxSdkCallbacks.Interstitial.RaiseOnAdDisplayFailedEvent(
                    adUnitId, NotLoadedError(), new AdInfo(adUnitId));
                return;
            }

            // Handle resolved. Check "already showing" BEFORE mutating any
            // state — a duplicate Show while the ad unit's ad is genuinely
            // still on screen must not touch the showing flag, the recorded
            // handle, or the rewarded once-per-Show grant flag.
            if (_interstitialShowing.Contains(adUnitId))
            {
                LevelMomentMaxSdkCallbacks.Interstitial.RaiseOnAdDisplayFailedEvent(
                    adUnitId, AlreadyShowingError(), new AdInfo(adUnitId));
                return;
            }

            _interstitialShowing.Add(adUnitId);
            _showingInterstitialHandle[adUnitId] = ad;

            ad.Show(new InterstitialAdShowCallbacks
            {
                OnAdShowedFullScreenContent = () =>
                    LevelMomentMaxSdkCallbacks.Interstitial.RaiseOnAdDisplayedEvent(
                        adUnitId, new AdInfo(adUnitId)),
                OnAdDismissed = () =>
                    EndInterstitialShow(adUnitId, ad, delegate
                    {
                        LevelMomentMaxSdkCallbacks.Interstitial.RaiseOnAdHiddenEvent(
                            adUnitId, new AdInfo(adUnitId));
                    }),
                OnAdFailedToShow = error =>
                    EndInterstitialShow(adUnitId, ad, delegate
                    {
                        LevelMomentMaxSdkCallbacks.Interstitial.RaiseOnAdDisplayFailedEvent(
                            adUnitId, new ErrorInfo(error), new AdInfo(adUnitId));
                    }),
            });
        }

        // Runs when a shown interstitial ends (any outcome). A no-op unless
        // `handle` is the one recorded as currently showing for this ad unit
        // — a stale or unrelated handle's completion must never touch the
        // live show's state (see the file header).
        private static void EndInterstitialShow(string adUnitId, InterstitialAd handle, Action queueEvent)
        {
            InterstitialAd recorded;
            if (!_showingInterstitialHandle.TryGetValue(adUnitId, out recorded) || !ReferenceEquals(recorded, handle))
                return;

            _showingInterstitialHandle.Remove(adUnitId);
            _interstitialShowing.Remove(adUnitId);
            queueEvent();

            // The queued load runs through the public entry point, so a show
            // that is somehow live again re-queues instead of destroying the
            // live handle. Its load event is queued after this show's end
            // event, and the pump is FIFO, so the game sees them in order.
            if (_interstitialLoadQueued.Remove(adUnitId))
                LoadInterstitial(adUnitId);
        }

        // ---- Rewarded ---------------------------------------------------------

        /// <summary>
        /// Mirrors <c>MaxSdk.LoadRewardedAd(string)</c>. Raises no event
        /// before it returns — see the file header.
        /// </summary>
        public static void LoadRewardedAd(string adUnitId)
        {
            if (string.IsNullOrEmpty(adUnitId))
            {
                LogError("LevelMomentMaxSdk.LoadRewardedAd: adUnitId must not be null or empty.");
                LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdLoadFailedEvent(adUnitId, InvalidAdUnitIdError());
                return;
            }

            if (_rewardedShowing.Contains(adUnitId))
            {
                _rewardedLoadQueued.Add(adUnitId);
                return;
            }

            string placementId;
            string format;
            if (!TryResolvePlacement(adUnitId, out placementId, out format))
            {
                LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdLoadFailedEvent(
                    adUnitId, UnmappedError());
                return;
            }

            RewardedAd.Load(placementId, new RewardedAdLoadCallbacks
            {
                OnAdLoaded = ad =>
                {
                    RewardedAd previous;
                    if (_rewarded.TryGetValue(adUnitId, out previous) && previous != null && !ReferenceEquals(previous, ad))
                        previous.Destroy();
                    _rewarded[adUnitId] = ad;
                    LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdLoadedEvent(
                        adUnitId, new AdInfo(adUnitId));
                },
                OnAdFailedToLoad = error =>
                {
                    LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdLoadFailedEvent(
                        adUnitId, new ErrorInfo(error));
                },
            }, format);
        }

        /// <summary>
        /// Mirrors <c>MaxSdk.IsRewardedAdReady(string)</c>. A synchronous
        /// read of current state, not a queued event.
        /// </summary>
        public static bool IsRewardedAdReady(string adUnitId)
        {
            RewardedAd ad;
            return adUnitId != null
                && _rewarded.TryGetValue(adUnitId, out ad)
                && ad != null
                && ad.IsLoaded;
        }

        /// <summary>
        /// Mirrors <c>MaxSdk.ShowRewardedAd(string, string, string)</c>.
        /// <paramref name="placement"/>/<paramref name="customData"/> are
        /// accepted for signature compatibility and ignored, as in
        /// <see cref="ShowInterstitial"/>. See the file header, REWARD
        /// SEMANTICS, for how <see cref="LevelMomentMaxSdkCallbacks.Rewarded"/>'s
        /// <c>OnAdReceivedRewardEvent</c> maps onto per-answer grading.
        /// Raises no event before it returns.
        /// </summary>
        public static void ShowRewardedAd(string adUnitId, string placement = null, string customData = null)
        {
            WarnIfIgnoredShowParams(placement, customData);

            if (string.IsNullOrEmpty(adUnitId))
            {
                LogError("LevelMomentMaxSdk.ShowRewardedAd: adUnitId must not be null or empty.");
                LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdDisplayFailedEvent(
                    adUnitId, InvalidAdUnitIdError(), new AdInfo(adUnitId));
                return;
            }

            RewardedAd ad;
            if (!_rewarded.TryGetValue(adUnitId, out ad) || ad == null)
            {
                LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdDisplayFailedEvent(
                    adUnitId, NotLoadedError(), new AdInfo(adUnitId));
                return;
            }

            if (_rewardedShowing.Contains(adUnitId))
            {
                LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdDisplayFailedEvent(
                    adUnitId, AlreadyShowingError(), new AdInfo(adUnitId));
                return;
            }

            _rewardedShowing.Add(adUnitId);
            _showingRewardedHandle[adUnitId] = ad;
            _rewardedGranted.Remove(adUnitId); // a genuinely new Show resets the once-per-Show flag

            ad.Show(new RewardedAdShowCallbacks
            {
                OnAdShowedFullScreenContent = () =>
                    LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdDisplayedEvent(
                        adUnitId, new AdInfo(adUnitId)),
                OnAdDismissed = () =>
                    EndRewardedShow(adUnitId, ad, delegate
                    {
                        LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdHiddenEvent(
                            adUnitId, new AdInfo(adUnitId));
                    }),
                OnAdFailedToShow = error =>
                    EndRewardedShow(adUnitId, ad, delegate
                    {
                        LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdDisplayFailedEvent(
                            adUnitId, new ErrorInfo(error), new AdInfo(adUnitId));
                    }),
                OnUserEarnedRewardItem = item =>
                {
                    // MAX fires OnAdReceivedRewardEvent at most once per
                    // Show; the underlying reward fires once per graded
                    // answer (amount 0 for a wrong one). Collapse to
                    // MAX's shape: only the first correct answer reports.
                    if (item.Amount <= 0)
                        return;
                    if (_rewardedGranted.Contains(adUnitId))
                        return;
                    _rewardedGranted.Add(adUnitId);
                    LevelMomentMaxSdkCallbacks.Rewarded.RaiseOnAdReceivedRewardEvent(
                        adUnitId,
                        new Reward { Label = item.RewardId, Amount = item.Amount },
                        new AdInfo(adUnitId));
                },
            });
        }

        private static void EndRewardedShow(string adUnitId, RewardedAd handle, Action queueEvent)
        {
            RewardedAd recorded;
            if (!_showingRewardedHandle.TryGetValue(adUnitId, out recorded) || !ReferenceEquals(recorded, handle))
                return;

            _showingRewardedHandle.Remove(adUnitId);
            _rewardedShowing.Remove(adUnitId);
            queueEvent();

            if (_rewardedLoadQueued.Remove(adUnitId))
                LoadRewardedAd(adUnitId);
        }

        // ---- Shared helpers -----------------------------------------------

        private static void WarnIfIgnoredShowParams(string placement, string customData)
        {
            if (_warnedIgnoredShowParams)
                return;
            if (string.IsNullOrEmpty(placement) && string.IsNullOrEmpty(customData))
                return;
            _warnedIgnoredShowParams = true;
            LogWarning(
                "LevelMomentMaxSdk: the 'placement'/'customData' arguments to ShowInterstitial/ShowRewardedAd " +
                "are accepted for MAX signature compatibility and ignored — there is no per-show placement " +
                "override, and custom data set here does not reach the reward webhook. Configure " +
                "LevelMomentConfig.CustomData at Initialize() time instead.");
        }

        private static ErrorInfo UnmappedError()
        {
            return new ErrorInfo(
                "unmapped_ad_unit",
                "No placement is registered for this ad unit. Call LevelMomentMaxSdk.MapAdUnit(adUnitId, placementId) first.");
        }

        private static ErrorInfo NotLoadedError()
        {
            return new ErrorInfo(
                "not_loaded",
                "Show called with no loaded ad for this ad unit. Call Load for this ad unit first.");
        }

        private static ErrorInfo AlreadyShowingError()
        {
            return new ErrorInfo(
                "already_showing",
                "This ad unit's ad is already on screen. Wait for it to finish before calling Show again.");
        }

        private static ErrorInfo InvalidAdUnitIdError()
        {
            return new ErrorInfo(
                "invalid_ad_unit_id",
                "adUnitId must not be null or empty.");
        }

        private static void LogWarning(string message)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogWarning(message);
#else
            System.Diagnostics.Debug.WriteLine("[WARN] " + message);
#endif
        }

        private static void LogError(string message)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogError(message);
#else
            System.Diagnostics.Debug.WriteLine("[ERROR] " + message);
#endif
        }

        /// <summary>
        /// Test-only reset: clears every mapping, live handle, and any event
        /// still waiting on the pump.
        /// </summary>
        internal static void ResetForTests()
        {
            _placementByAdUnit.Clear();
            _formatByAdUnit.Clear();
            _interstitials.Clear();
            _rewarded.Clear();
            _interstitialShowing.Clear();
            _rewardedShowing.Clear();
            _showingInterstitialHandle.Clear();
            _showingRewardedHandle.Clear();
            _interstitialLoadQueued.Clear();
            _rewardedLoadQueued.Clear();
            _rewardedGranted.Clear();
            _warnedIgnoredShowParams = false;
            MaxEventPump.Instance.Reset();
        }

#if UNITY_5_3_OR_NEWER
        // A play session with Domain Reload disabled (Enter Play Mode
        // Options) does not clear static fields between plays; without this,
        // a second play in the same editor session would start with stale
        // mappings, "ready" handles, queued events, AND stale
        // LevelMomentMaxSdkCallbacks subscribers left over from the first —
        // the latter double-firing events (a reward included) into whatever
        // the second play's game code subscribed on top of them. Reset both
        // facade classes' own statics here — LevelMomentAds/RewardedAd/
        // InterstitialAd own their own reset paths.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticStateOnSubsystemRegistration()
        {
            ResetForTests();
            LevelMomentMaxSdkCallbacks.ResetForTests();
        }
#endif
    }

    /// <summary>
    /// Mirrors <c>AppLovin.MaxSdkCallbacks</c>: static events, keyed by
    /// ad-unit id passed as the first argument, matching MAX's parameter
    /// shapes exactly except for the compatibility types (see
    /// MaxCompatTypes.cs). Every event is QUEUED on <see cref="MaxEventPump"/>
    /// and raised on a later frame — never inside the facade call that caused
    /// it (see LevelMomentMaxSdk.cs's header) — and then raised through
    /// <see cref="RaiseSafely{THandler}"/>, so a subscriber that throws is
    /// caught and logged rather than stopping the other subscribers.
    /// </summary>
    public static class LevelMomentMaxSdkCallbacks
    {
        /// <summary>
        /// Mirrors <c>MaxSdkCallbacks.OnSdkInitializedEvent</c>. Divergence:
        /// MAX's carries a <c>MaxSdkBase.SdkConfiguration</c> payload; this
        /// facade defines no shape-compatible stand-in for that type (it
        /// describes consent/GDPR/country state this SDK does not expose the
        /// same way), so this event is parameterless. A callback body that
        /// reads the MAX event's <c>sdkConfiguration</c> argument needs a
        /// one-line edit to drop it; a body that ignores the argument (the
        /// common case — `sdkConfiguration => { StartAds(); }`) needs the
        /// lambda's parameter removed but nothing else. Like MAX's, it
        /// arrives on a later frame, not inside
        /// <c>LevelMomentMaxSdk.InitializeSdk()</c>.
        /// </summary>
        public static event Action OnSdkInitializedEvent;

        internal static void RaiseOnSdkInitializedEvent()
        {
            Queue(() => RaiseSafely(OnSdkInitializedEvent, h => h()));
        }

        /// <summary>Mirrors <c>MaxSdkCallbacks.Interstitial</c>.</summary>
        public static class Interstitial
        {
            public static event Action<string, AdInfo> OnAdLoadedEvent;
            public static event Action<string, ErrorInfo> OnAdLoadFailedEvent;
            public static event Action<string, AdInfo> OnAdDisplayedEvent;
            public static event Action<string, ErrorInfo, AdInfo> OnAdDisplayFailedEvent;
            public static event Action<string, AdInfo> OnAdHiddenEvent;

            internal static void RaiseOnAdLoadedEvent(string adUnitId, AdInfo info) =>
                Queue(() => RaiseSafely(OnAdLoadedEvent, h => h(adUnitId, info)));

            internal static void RaiseOnAdLoadFailedEvent(string adUnitId, ErrorInfo error) =>
                Queue(() => RaiseSafely(OnAdLoadFailedEvent, h => h(adUnitId, error)));

            internal static void RaiseOnAdDisplayedEvent(string adUnitId, AdInfo info) =>
                Queue(() => RaiseSafely(OnAdDisplayedEvent, h => h(adUnitId, info)));

            internal static void RaiseOnAdDisplayFailedEvent(string adUnitId, ErrorInfo error, AdInfo info) =>
                Queue(() => RaiseSafely(OnAdDisplayFailedEvent, h => h(adUnitId, error, info)));

            internal static void RaiseOnAdHiddenEvent(string adUnitId, AdInfo info) =>
                Queue(() => RaiseSafely(OnAdHiddenEvent, h => h(adUnitId, info)));

            internal static void ResetForTests()
            {
                OnAdLoadedEvent = null;
                OnAdLoadFailedEvent = null;
                OnAdDisplayedEvent = null;
                OnAdDisplayFailedEvent = null;
                OnAdHiddenEvent = null;
            }
        }

        /// <summary>Mirrors <c>MaxSdkCallbacks.Rewarded</c>.</summary>
        public static class Rewarded
        {
            public static event Action<string, AdInfo> OnAdLoadedEvent;
            public static event Action<string, ErrorInfo> OnAdLoadFailedEvent;
            public static event Action<string, AdInfo> OnAdDisplayedEvent;
            public static event Action<string, ErrorInfo, AdInfo> OnAdDisplayFailedEvent;
            public static event Action<string, AdInfo> OnAdHiddenEvent;
            public static event Action<string, Reward, AdInfo> OnAdReceivedRewardEvent;

            internal static void RaiseOnAdLoadedEvent(string adUnitId, AdInfo info) =>
                Queue(() => RaiseSafely(OnAdLoadedEvent, h => h(adUnitId, info)));

            internal static void RaiseOnAdLoadFailedEvent(string adUnitId, ErrorInfo error) =>
                Queue(() => RaiseSafely(OnAdLoadFailedEvent, h => h(adUnitId, error)));

            internal static void RaiseOnAdDisplayedEvent(string adUnitId, AdInfo info) =>
                Queue(() => RaiseSafely(OnAdDisplayedEvent, h => h(adUnitId, info)));

            internal static void RaiseOnAdDisplayFailedEvent(string adUnitId, ErrorInfo error, AdInfo info) =>
                Queue(() => RaiseSafely(OnAdDisplayFailedEvent, h => h(adUnitId, error, info)));

            internal static void RaiseOnAdHiddenEvent(string adUnitId, AdInfo info) =>
                Queue(() => RaiseSafely(OnAdHiddenEvent, h => h(adUnitId, info)));

            internal static void RaiseOnAdReceivedRewardEvent(string adUnitId, Reward reward, AdInfo info) =>
                Queue(() => RaiseSafely(OnAdReceivedRewardEvent, h => h(adUnitId, reward, info)));

            internal static void ResetForTests()
            {
                OnAdLoadedEvent = null;
                OnAdLoadFailedEvent = null;
                OnAdDisplayedEvent = null;
                OnAdDisplayFailedEvent = null;
                OnAdHiddenEvent = null;
                OnAdReceivedRewardEvent = null;
            }
        }

        internal static void ResetForTests()
        {
            OnSdkInitializedEvent = null;
            Interstitial.ResetForTests();
            Rewarded.ResetForTests();
        }

        // ---- Deferred, safe, typed multicast dispatch ----------------------
        //
        // Queue() defers delivery to the pump's next tick. Each queued action
        // reads its event field when it RUNS, not when it was queued, so a
        // handler subscribed between the call and the tick still hears about
        // it and one unsubscribed in between does not — the same as MAX,
        // whose events are likewise delivered later.
        //
        // A plain `evt?.Invoke(...)` stops at the first subscriber that
        // throws and never calls the rest. RaiseSafely instead walks the
        // invocation list and gives every subscriber a turn, so one game's
        // broken handler (or a broken test double) cannot swallow another
        // subscriber's callback or escape into the frame driver.
        //
        // Invoking through a typed cast (rather than Delegate.DynamicInvoke)
        // avoids boxing every argument on every raise, keeps a subscriber's
        // own exception un-wrapped (DynamicInvoke reports it as a
        // TargetInvocationException, which is a worse stack trace for a
        // game developer to read), and avoids DynamicInvoke as an IL2CPP/AOT
        // hazard with the generic Reward struct argument.

        private static void Queue(Action raise)
        {
            MaxEventPump.Instance.Enqueue(raise);
        }

        private static void RaiseSafely<THandler>(THandler evt, Action<THandler> invoke)
            where THandler : Delegate
        {
            if (evt == null)
                return;
            foreach (var raw in evt.GetInvocationList())
            {
                try
                {
                    invoke((THandler)raw);
                }
                catch (Exception ex)
                {
                    LogSubscriberException(ex);
                }
            }
        }

        private static void LogSubscriberException(Exception ex)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogException(ex);
#else
            System.Diagnostics.Debug.WriteLine(ex);
#endif
        }
    }
}
