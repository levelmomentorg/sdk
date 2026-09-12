// EditMode tests for the AppLovin MAX-shaped compatibility facade
// (Runtime/Compat/LevelMomentMaxSdk.cs). Mirrors InterstitialAdTests.cs /
// RewardedAdTests.cs style — same fixture shape, same FakeWebView — but
// drives everything through LevelMomentMaxSdk/LevelMomentMaxSdkCallbacks
// instead of InterstitialAd/RewardedAd directly, since that facade is the
// thing a migrating game actually calls. Run from Unity: Window → General →
// Test Runner → EditMode → Run All.
//
// The facade delivers its events from MaxEventPump, which the SDK's runtime
// driver ticks once a frame. These tests skip that driver (as the other
// EditMode suites already skip it for the load watchdog) and call Pump()
// where a frame would have passed. Nothing the facade raises is observable
// before a Pump() — that is the property under test, not an artifact of the
// harness.
//
// Tests that provoke a subscriber exception or a Debug.LogError wrap the
// assertion in `#if UNITY_5_3_OR_NEWER` LogAssert handling: the real Unity
// Test Framework fails a test that emits an unexpected LogType.Error/
// LogType.Exception log, which the dotnet compile-check harness (no
// UnityEngine.TestTools stub) never runs, so those blocks compile to nothing
// there and take effect only in the editor.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using LevelMoment;
using LevelMoment.Compat.Max;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.TestTools;
#endif

namespace LevelMoment.Tests.EditMode
{
    public class MaxCompatTests
    {
        private double _now;

        // ---- Fakes (same shape as InterstitialAdTests/RewardedAdTests) ------

        private class FakeWebView : ILevelMomentWebView
        {
            public event Action<string> OnMessage;
            public event Action OnClosed;

            public int OpenCount;
            public int CloseCount;
            public string LastUrl;

            public void Open(string url)
            {
                OpenCount++;
                LastUrl = url;
            }

            public void Close()
            {
                CloseCount++;
            }

            public void EmitMessage(string raw)
            {
                if (OnMessage != null)
                    OnMessage(raw);
            }
        }

        private const string Ready = "{\"type\":\"ready\"}";
        private const string Dismissed = "{\"type\":\"dismissed\"}";

        private static string Reward(int amount, string rewardId)
        {
            return "{\"type\":\"earnedReward\",\"payload\":{\"amount\":" + amount +
                ",\"rewardId\":\"" + rewardId + "\"}}";
        }

        /// <summary>
        /// One frame of the SDK's runtime driver: exactly what
        /// LevelMomentRuntime.Update() does, including which tickables are
        /// registered, so the facade's registration with that driver is under
        /// test rather than bypassed. Anything a handler queues while this
        /// runs waits for the NEXT Pump().
        /// </summary>
        private static void Pump(int frames = 1)
        {
            for (var i = 0; i < frames; i++)
                LevelMomentRuntime.TickAll();
        }

        // ---- Fixture ----------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            LevelMomentAds.ResetForTests();
            LevelMomentAds.Initialize(new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break",
                UnsafeTesting = new UnsafeTesting { ApiUrl = "https://api.example.com", BreakUrl = "https://app.example.com/break" },
            });
            _now = 0;
            LevelMomentAds.ClockSeconds = () => _now;
            LevelMomentAds.LoadTimeoutSeconds = 15;
            LevelMomentWebViewRegistry.Reset();
            InterstitialAd.SkipRuntimeDriver = true;
            RewardedAd.SkipRuntimeDriver = true;
            LevelMomentRuntime.ResetForTests();

            LevelMomentMaxSdk.ResetForTests();
            LevelMomentMaxSdkCallbacks.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            LevelMomentWebViewRegistry.Reset();
            InterstitialAd.SkipRuntimeDriver = false;
            RewardedAd.SkipRuntimeDriver = false;
            LevelMomentAds.ResetForTests();
            LevelMomentRuntime.ResetForTests();

            LevelMomentMaxSdk.ResetForTests();
            LevelMomentMaxSdkCallbacks.ResetForTests();
        }

        // ---- Ad-unit mapping --------------------------------------------------

        [Test]
        public void LoadInterstitial_UnmappedAdUnit_FiresLoadFailedAndNeverOpensAWebView()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            string failedAdUnit = null;
            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent +=
                (adUnitId, error) => { failedAdUnit = adUnitId; failedError = error; };

            LevelMomentMaxSdk.LoadInterstitial("unmapped-unit");
            Pump();

            Assert.AreEqual("unmapped-unit", failedAdUnit);
            Assert.IsNotNull(failedError);
            Assert.AreEqual("unmapped_ad_unit", failedError.Code);
            Assert.AreEqual(0, fake.OpenCount);
        }

        [Test]
        public void LoadRewardedAd_UnmappedAdUnit_FiresLoadFailedEvent()
        {
            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += (adUnitId, error) => failedError = error;

            LevelMomentMaxSdk.LoadRewardedAd("unmapped-unit");
            Pump();

            Assert.IsNotNull(failedError);
            Assert.AreEqual("unmapped_ad_unit", failedError.Code);
        }

        [Test]
        public void MapAdUnit_ThenLoad_UsesTheMappedPlacementOnTheBuiltUrl()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentMaxSdk.MapAdUnit("interstitial-unit", "p1");

            AdInfo loadedInfo = null;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (adUnitId, info) => loadedInfo = info;

            LevelMomentMaxSdk.LoadInterstitial("interstitial-unit");
            Pump();
            Assert.IsNotNull(loadedInfo);
            Assert.AreEqual("interstitial-unit", loadedInfo.AdUnitIdentifier);
            Assert.AreEqual("LevelMoment", loadedInfo.NetworkName);

            LevelMomentMaxSdk.ShowInterstitial("interstitial-unit");
            StringAssert.Contains("placementId=p1", fake.LastUrl);
        }

        [Test]
        public void MapAdUnit_FormatParameterReachesTheBuiltUrl()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentMaxSdk.MapAdUnit("unit1", "p1", "practice_set");
            LevelMomentMaxSdk.LoadInterstitial("unit1");
            LevelMomentMaxSdk.ShowInterstitial("unit1");

            StringAssert.Contains("format=practice_set", fake.LastUrl);
        }

        [Test]
        public void MapAdUnit_EmptyAdUnitId_DoesNotRegisterAndLogsAnError()
        {
#if UNITY_5_3_OR_NEWER
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("adUnitId must not be empty"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("adUnitId must not be null or empty"));
#endif
            Assert.DoesNotThrow(() => LevelMomentMaxSdk.MapAdUnit("", "p1"));

            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += (adUnitId, error) => failedError = error;
            LevelMomentMaxSdk.LoadInterstitial("");
            Pump();

            Assert.IsNotNull(failedError);
            Assert.AreEqual("invalid_ad_unit_id", failedError.Code);
        }

        [Test]
        public void MapAdUnit_EmptyPlacementId_DoesNotRegisterAndLogsAnError()
        {
#if UNITY_5_3_OR_NEWER
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("placementId must not be empty"));
#endif
            Assert.DoesNotThrow(() => LevelMomentMaxSdk.MapAdUnit("unit1", ""));

            Assert.IsFalse(LevelMomentMaxSdk.IsInterstitialReady("unit1"));
        }

        // ---- Load failure surfaced through the underlying SDK -----------------

        [Test]
        public void LoadInterstitial_MappedButSdkNotInitialized_FiresLoadFailedEvent()
        {
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");
            LevelMomentAds.ResetForTests(); // SDK no longer initialized
            InterstitialAd.SkipRuntimeDriver = true; // that reset re-enabled the driver

            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += (adUnitId, error) => failedError = error;

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Pump();

            Assert.IsNotNull(failedError);
            Assert.AreEqual("not_initialized", failedError.Code);
        }

        // ---- Show sequence: displayed -> hidden --------------------------------

        [Test]
        public void ShowInterstitial_FiresDisplayedThenHidden()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            var displayed = 0;
            var hidden = 0;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += (adUnitId, info) => displayed++;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdHiddenEvent += (adUnitId, info) => hidden++;

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            LevelMomentMaxSdk.ShowInterstitial("unit1");

            fake.EmitMessage(Ready);
            Assert.AreEqual(0, displayed, "the core callback must not deliver the event either");
            Pump();
            Assert.AreEqual(1, displayed);
            Assert.AreEqual(0, hidden);

            fake.EmitMessage(Dismissed);
            Assert.AreEqual(0, hidden, "the core callback must not deliver the event either");
            Pump();
            Assert.AreEqual(1, displayed);
            Assert.AreEqual(1, hidden);
        }

        // ---- placement/customData are accepted and ignored ---------------------

        [Test]
        public void ShowInterstitial_WithPlacementAndCustomData_StillShowsNormally()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            var displayed = 0;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += (adUnitId, info) => displayed++;

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            LevelMomentMaxSdk.ShowInterstitial("unit1", "some-placement", "some-custom-data");

            Assert.AreEqual(1, fake.OpenCount);
            fake.EmitMessage(Ready);
            Pump();
            Assert.AreEqual(1, displayed);
        }

        // ---- Ready flag false after show until reload --------------------------

        [Test]
        public void IsInterstitialReady_FalseAfterShowUntilLoadedAgain()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            Assert.IsFalse(LevelMomentMaxSdk.IsInterstitialReady("unit1"));

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Assert.IsTrue(LevelMomentMaxSdk.IsInterstitialReady("unit1"),
                "readiness is a synchronous read of current state, not a queued event");

            LevelMomentMaxSdk.ShowInterstitial("unit1");
            Assert.IsFalse(LevelMomentMaxSdk.IsInterstitialReady("unit1"));

            fake.EmitMessage(Dismissed);
            Pump();
            Assert.IsFalse(LevelMomentMaxSdk.IsInterstitialReady("unit1"), "must stay false until the next Load");

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Assert.IsTrue(LevelMomentMaxSdk.IsInterstitialReady("unit1"), "a fresh Load must make it ready again");
        }

        [Test]
        public void IsRewardedAdReady_StatesAcrossTheLifecycle()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("runit", "p1");

            Assert.IsFalse(LevelMomentMaxSdk.IsRewardedAdReady("runit"), "never mapped/loaded");

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            Assert.IsTrue(LevelMomentMaxSdk.IsRewardedAdReady("runit"));

            LevelMomentMaxSdk.ShowRewardedAd("runit");
            Assert.IsFalse(LevelMomentMaxSdk.IsRewardedAdReady("runit"));

            fake.EmitMessage(Dismissed);
            Pump();
            Assert.IsFalse(LevelMomentMaxSdk.IsRewardedAdReady("runit"));

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            Assert.IsTrue(LevelMomentMaxSdk.IsRewardedAdReady("runit"));
        }

        // ---- Second Show without reload -----------------------------------------

        [Test]
        public void ShowInterstitial_NeverLoaded_FiresDisplayFailedRatherThanDoingNothing()
        {
            ErrorInfo failedError = null;
            var displayed = 0;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (adUnitId, error, info) => failedError = error;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += (adUnitId, info) => displayed++;

            LevelMomentMaxSdk.ShowInterstitial("never-loaded-unit");
            Pump();

            Assert.IsNotNull(failedError);
            Assert.AreEqual("not_loaded", failedError.Code);
            Assert.AreEqual(0, displayed);
        }

        [Test]
        public void ShowRewardedAd_NeverLoaded_FiresDisplayFailedRatherThanDoingNothing()
        {
            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += (adUnitId, error, info) => failedError = error;

            LevelMomentMaxSdk.ShowRewardedAd("never-loaded-runit");
            Pump();

            Assert.IsNotNull(failedError);
            Assert.AreEqual("not_loaded", failedError.Code);
        }

        [Test]
        public void ShowInterstitial_SecondShowWithoutReload_FiresDisplayFailedInsteadOfReopening()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            LevelMomentMaxSdk.ShowInterstitial("unit1");
            fake.EmitMessage(Dismissed);
            Pump(); // drain the first break's events

            var failedCount = 0;
            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent +=
                (adUnitId, error, info) => { failedCount++; failedError = error; };

            LevelMomentMaxSdk.ShowInterstitial("unit1"); // no LoadInterstitial in between
            Pump();

            Assert.AreEqual(1, fake.OpenCount, "must not reopen the WebView");
            Assert.AreEqual(1, failedCount);
            Assert.IsNotNull(failedError);
            Assert.AreEqual("not_loaded", failedError.Code,
                "a consumed handle reports not_loaded, not already_showing");
        }

        [Test]
        public void ShowRewardedAd_SecondShowWithoutReload_FiresDisplayFailed()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("runit", "p1");

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            LevelMomentMaxSdk.ShowRewardedAd("runit");
            fake.EmitMessage(Dismissed);
            Pump();

            var failedCount = 0;
            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent +=
                (adUnitId, error, info) => { failedCount++; failedError = error; };

            LevelMomentMaxSdk.ShowRewardedAd("runit");
            Pump();

            Assert.AreEqual(1, fake.OpenCount);
            Assert.AreEqual(1, failedCount);
            Assert.AreEqual("not_loaded", failedError.Code);
        }

        // ---- Reward event: at most one per Show, only for a correct answer -----

        [Test]
        public void RewardedAd_FiveGradedAnswers_FiresOnAdReceivedRewardEventExactlyOnce()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("runit", "p1");

            var rewardCount = 0;
            string rewardedAdUnit = null;
            Reward reward = default;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (adUnitId, r, info) =>
            {
                rewardCount++;
                rewardedAdUnit = adUnitId;
                reward = r;
            };

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            LevelMomentMaxSdk.ShowRewardedAd("runit");

            fake.EmitMessage(Reward(1, "impression-1"));
            fake.EmitMessage(Reward(0, "impression-2"));
            fake.EmitMessage(Reward(1, "impression-3"));
            fake.EmitMessage(Reward(1, "impression-4"));
            fake.EmitMessage(Reward(0, "impression-5"));
            Pump();

            Assert.AreEqual(1, rewardCount, "MAX fires OnAdReceivedRewardEvent at most once per Show");
            Assert.AreEqual("runit", rewardedAdUnit);
            Assert.AreEqual(1, reward.Amount);
            Assert.AreEqual("impression-1", reward.Label, "the FIRST correct answer wins, not a later one");
        }

        [Test]
        public void RewardedAd_WrongAnswersOnly_FiresNoRewardEvent()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("runit", "p1");

            var rewardCount = 0;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (adUnitId, r, info) => rewardCount++;

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            LevelMomentMaxSdk.ShowRewardedAd("runit");

            fake.EmitMessage(Reward(0, "impression-1"));
            fake.EmitMessage(Reward(0, "impression-2"));
            fake.EmitMessage(Reward(0, "impression-3"));
            Pump();

            Assert.AreEqual(0, rewardCount);
        }

        [Test]
        public void RewardedAd_SecondShowAfterReload_CanFireTheRewardEventAgain()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("runit", "p1");

            var rewardCount = 0;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (adUnitId, r, info) => rewardCount++;

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            LevelMomentMaxSdk.ShowRewardedAd("runit");
            fake.EmitMessage(Reward(1, "impression-1"));
            fake.EmitMessage(Dismissed);
            Pump();
            Assert.AreEqual(1, rewardCount);

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            LevelMomentMaxSdk.ShowRewardedAd("runit");
            fake.EmitMessage(Reward(1, "impression-2"));
            Pump();

            Assert.AreEqual(2, rewardCount, "the once-flag must reset on the next Show");
        }

        // ---- Load while a break is on screen ------------------------------------

        [Test]
        public void LoadInterstitial_DuringShow_QueuesInsteadOfClosingTheWebViewAndFlushesAfterHidden()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            LevelMomentMaxSdk.ShowInterstitial("unit1");
            fake.EmitMessage(Ready);
            Pump();

            var order = new List<string>();
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (adUnitId, info) => order.Add("loaded");
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdHiddenEvent += (adUnitId, info) => order.Add("hidden");

            // A game preloading the next interstitial from inside
            // OnAdDisplayedEvent (normal MAX practice) must not tear down the
            // break currently on screen.
            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Pump();

            Assert.AreEqual(0, fake.CloseCount, "the live break must stay open while a load is queued");
            CollectionAssert.IsEmpty(order, "the queued load must not run until the show ends");

            fake.EmitMessage(Dismissed);
            Pump();

            Assert.AreEqual(1, fake.CloseCount);
            CollectionAssert.AreEqual(new[] { "hidden", "loaded" }, order,
                "the queued load runs once the show ends, and its event follows OnAdHiddenEvent");
            Assert.IsTrue(LevelMomentMaxSdk.IsInterstitialReady("unit1"));
        }

        [Test]
        public void LoadRewardedAd_DuringShow_QueuesInsteadOfClosingTheWebViewAndFlushesAfterHidden()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("runit", "p1");

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            LevelMomentMaxSdk.ShowRewardedAd("runit");
            Pump();

            var loadedCount = 0;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdLoadedEvent += (adUnitId, info) => loadedCount++;

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            Pump();
            Assert.AreEqual(0, fake.CloseCount);
            Assert.AreEqual(0, loadedCount);

            fake.EmitMessage(Dismissed);
            Pump();

            Assert.AreEqual(1, fake.CloseCount);
            Assert.AreEqual(1, loadedCount);
        }

        [Test]
        public void LoadInterstitial_QueuedDuringShow_SurvivesADismissRaisedFromInsideACallback()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            LevelMomentMaxSdk.ShowInterstitial("unit1");

            // Queue a load while genuinely still showing.
            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Pump();

            var loadedCount = 0;
            var hiddenCount = 0;
            var displayFailedCount = 0;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (adUnitId, info) => loadedCount++;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdHiddenEvent += (adUnitId, info) => hiddenCount++;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (adUnitId, error, info) =>
            {
                displayFailedCount++;
                // A handler that tears the live break down from inside a
                // callback: the break ends while the pump is mid-flush. The
                // load queued above must still run afterwards, not vanish.
                if (error.Code == "already_showing")
                    fake.EmitMessage(Dismissed);
            };

            // A duplicate Show while still on screen.
            LevelMomentMaxSdk.ShowInterstitial("unit1");
            Pump(); // delivers already_showing; the handler dismisses the break
            Pump(); // delivers the resulting hidden + the queued load's event

            Assert.AreEqual(1, displayFailedCount);
            Assert.AreEqual(1, hiddenCount, "the original break still completes exactly once");
            Assert.AreEqual(1, fake.CloseCount);
            Assert.AreEqual(1, loadedCount, "the queued load must still run, not vanish");
            Assert.IsTrue(LevelMomentMaxSdk.IsInterstitialReady("unit1"));
        }

        // ---- A failed reload must not discard an already-ready handle ----------

        [Test]
        public void LoadInterstitial_FailedReload_DoesNotDiscardAnAlreadyReadyHandle()
        {
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");
            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Pump();
            Assert.IsTrue(LevelMomentMaxSdk.IsInterstitialReady("unit1"));

            LevelMomentAds.ResetForTests(); // the next Load fails with not_initialized
            InterstitialAd.SkipRuntimeDriver = true; // that reset re-enabled the driver

            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += (adUnitId, error) => failedError = error;

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Pump();

            Assert.IsNotNull(failedError);
            Assert.AreEqual("not_initialized", failedError.Code);
            Assert.IsTrue(LevelMomentMaxSdk.IsInterstitialReady("unit1"), "a failed reload must not discard the still-ready handle");
        }

        // ---- Stale callbacks from a replaced handle -----------------------------

        [Test]
        public void LoadInterstitial_SuccessfulReload_DestroysThePreviousHandle()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");
            LevelMomentMaxSdk.LoadInterstitial("unit1");

            LevelMomentMaxSdk.MapAdUnit("unit1", "p2"); // re-map before the second load
            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Pump();

            // The stale (first) handle must be destroyed — not observable
            // directly from outside the assembly's InternalsVisibleTo
            // boundary beyond IsInterstitialReady/Show, so prove it the way
            // a game can: only the SECOND placement is ever shown.
            LevelMomentMaxSdk.ShowInterstitial("unit1");
            StringAssert.Contains("placementId=p2", fake.LastUrl);
            Assert.AreEqual(1, fake.OpenCount, "only one handle — the replacement — is ever shown");
        }

        // ---- Never throws out of a publisher callback --------------------------

        [Test]
        public void OnAdLoadedEvent_OneThrowingSubscriberDoesNotStopTheOthersOrEscape()
        {
#if UNITY_5_3_OR_NEWER
            LogAssert.ignoreFailingMessages = true;
#endif
            try
            {
                LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

                var secondSubscriberRan = false;
                LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (adUnitId, info) =>
                {
                    throw new InvalidOperationException("a broken game handler");
                };
                LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (adUnitId, info) =>
                {
                    secondSubscriberRan = true;
                };

                LevelMomentMaxSdk.LoadInterstitial("unit1");
                Assert.DoesNotThrow(() => Pump(), "a throwing subscriber must not escape the pump");
                Assert.IsTrue(secondSubscriberRan, "a throwing subscriber must not block the next one");
            }
            finally
            {
#if UNITY_5_3_OR_NEWER
                LogAssert.ignoreFailingMessages = false;
#endif
            }
        }

        [Test]
        public void ShowInterstitial_ThrowingDisplayedSubscriber_DoesNotEscapeThePump()
        {
#if UNITY_5_3_OR_NEWER
            LogAssert.ignoreFailingMessages = true;
#endif
            try
            {
                var fake = new FakeWebView();
                LevelMomentWebViewRegistry.Register(() => fake);
                LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

                LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += (adUnitId, info) =>
                {
                    throw new InvalidOperationException("a broken game handler");
                };

                LevelMomentMaxSdk.LoadInterstitial("unit1");
                LevelMomentMaxSdk.ShowInterstitial("unit1");

                Assert.DoesNotThrow(() => fake.EmitMessage(Ready));
                Assert.DoesNotThrow(() => Pump());
            }
            finally
            {
#if UNITY_5_3_OR_NEWER
                LogAssert.ignoreFailingMessages = false;
#endif
            }
        }

        // ---- Duplicate Show while already showing --------------------------------

        [Test]
        public void ShowInterstitial_DuplicateWhileAlreadyShowing_DoesNotDestroyTheLiveHandleOrReopen()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            LevelMomentMaxSdk.ShowInterstitial("unit1");
            fake.EmitMessage(Ready);
            Pump();

            var hiddenCount = 0;
            var displayFailedCount = 0;
            ErrorInfo displayFailedError = null;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdHiddenEvent += (adUnitId, info) => hiddenCount++;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (adUnitId, error, info) =>
            {
                displayFailedCount++;
                displayFailedError = error;
            };

            // A duplicate Show while the break is genuinely still on screen —
            // a fresh top-level call, like a game double-tapping a button.
            LevelMomentMaxSdk.ShowInterstitial("unit1");
            Pump();

            Assert.AreEqual(1, displayFailedCount);
            Assert.AreEqual("already_showing", displayFailedError.Code);
            Assert.AreEqual(0, fake.CloseCount, "the live break must not be torn down by the duplicate Show");

            // A Load while the break is (still, genuinely) on screen must
            // still queue rather than destroy the live handle — proving the
            // duplicate Show above did not clear the showing flag.
            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Pump();
            Assert.AreEqual(0, fake.CloseCount, "queuing the load must not touch the live handle either");

            fake.EmitMessage(Dismissed);
            Pump();

            Assert.AreEqual(1, hiddenCount, "the original break still delivers exactly one OnAdHiddenEvent");
            Assert.AreEqual(1, fake.CloseCount);
            Assert.AreEqual(1, fake.OpenCount, "only the original Show ever opened a WebView");
        }

        [Test]
        public void ShowRewardedAd_DuplicateWhileAlreadyShowing_DoesNotResetTheRewardGrantFlag()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("runit", "p1");

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            LevelMomentMaxSdk.ShowRewardedAd("runit");
            fake.EmitMessage(Reward(1, "impression-1"));
            Pump();

            var rewardCount = 0;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (adUnitId, r, info) => rewardCount++;

            // A duplicate Show while genuinely still on screen must not reset
            // the once-per-Show grant flag the first correct answer already set.
            LevelMomentMaxSdk.ShowRewardedAd("runit");

            fake.EmitMessage(Reward(1, "impression-2"));
            Pump();

            Assert.AreEqual(0, rewardCount, "the grant flag must still be set from the first correct answer");
        }

        // ---- Calling back into the facade from a callback ------------------------
        //
        // The four historical P1s in this facade all came from raising events
        // synchronously inside the call that caused them, so a MAX-shaped
        // handler that calls LoadX/ShowX re-entered the facade mid-call. The
        // tests below pin the property that replaced all that machinery: an
        // event is delivered from the pump, so such a handler is just a fresh
        // top-level call on a later frame.

        [Test]
        public void NoEventIsDeliveredInsideThePublicCallThatTriggeredIt()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var insideAFacadeCall = false;
            var deliveredInsideACall = 0;
            var delivered = 0;
            Action countDelivery = () =>
            {
                delivered++;
                if (insideAFacadeCall)
                    deliveredInsideACall++;
            };

            LevelMomentMaxSdkCallbacks.OnSdkInitializedEvent += () => countDelivery();
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (adUnitId, info) => countDelivery();
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += (adUnitId, error) => countDelivery();
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += (adUnitId, info) => countDelivery();
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (adUnitId, error, info) => countDelivery();
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdHiddenEvent += (adUnitId, info) => countDelivery();
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdLoadedEvent += (adUnitId, info) => countDelivery();
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (adUnitId, r, info) => countDelivery();
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdHiddenEvent += (adUnitId, info) => countDelivery();

            Action<Action> inCall = call =>
            {
                insideAFacadeCall = true;
                try
                {
                    call();
                }
                finally
                {
                    insideAFacadeCall = false;
                }
            };

            inCall(() => LevelMomentMaxSdk.InitializeSdk());
            inCall(() => LevelMomentMaxSdk.MapAdUnit("unit1", "p1"));
            inCall(() => LevelMomentMaxSdk.MapAdUnit("runit", "p1"));
            inCall(() => LevelMomentMaxSdk.LoadInterstitial("unit1"));
            inCall(() => LevelMomentMaxSdk.LoadInterstitial("unmapped"));
            inCall(() => LevelMomentMaxSdk.ShowInterstitial("unit1"));
            inCall(() => LevelMomentMaxSdk.LoadRewardedAd("runit"));
            inCall(() => LevelMomentMaxSdk.ShowRewardedAd("runit"));

            // The core ad callbacks are the other half of requirement 1: a
            // break's own ready/reward/dismissed messages must not deliver a
            // facade event in line either.
            inCall(() => fake.EmitMessage(Ready));
            inCall(() => fake.EmitMessage(Reward(1, "impression-1")));
            inCall(() => fake.EmitMessage(Dismissed));

            Assert.AreEqual(0, deliveredInsideACall, "no event may be delivered inside the call that caused it");
            Assert.AreEqual(0, delivered, "nothing at all is delivered before a frame passes");

            Pump();
            Assert.Greater(delivered, 0, "the queued events arrive on the next frame");
            Assert.AreEqual(0, deliveredInsideACall);
        }

        // ---- The pump's registration with the runtime driver ---------------

        [Test]
        public void TheEventPump_RegistersWithTheRuntimeDriverWhileItHasWorkAndDropsOffWhenDrained()
        {
            Assert.IsFalse(LevelMomentRuntime.IsTracked(MaxEventPump.Instance),
                "an idle pump must not be ticked every frame");

            LevelMomentMaxSdk.LoadInterstitial("unmapped-unit"); // queues one failure event

            Assert.IsTrue(LevelMomentRuntime.IsTracked(MaxEventPump.Instance),
                "queueing an event must register the pump with the per-frame driver, or nothing is ever delivered");
            Assert.AreEqual(1, MaxEventPump.Instance.PendingCount);

            Pump();

            Assert.AreEqual(0, MaxEventPump.Instance.PendingCount);
            Assert.IsFalse(LevelMomentRuntime.IsTracked(MaxEventPump.Instance),
                "a drained pump stops being ticked");
        }

        [Test]
        public void AFinishedBreakAndADrainedPump_LeaveNothingRegisteredWithTheDriver()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            LevelMomentMaxSdk.ShowInterstitial("unit1");
            fake.EmitMessage(Ready);
            fake.EmitMessage(Dismissed);
            Pump();

            Assert.AreEqual(0, LevelMomentRuntime.TrackedCount,
                "a finished break and a drained pump must both stop costing a per-frame tick");
        }

        // A tickable the driver-pass tests can steer. Real tickables are a
        // break's watchdog, the sign-in gate, and the facade's event pump.
        private sealed class StubTickable : ITickable
        {
            public int Ticks;
            public Action OnTick;

            public void Tick()
            {
                Ticks++;
                if (OnTick != null)
                    OnTick();
            }
        }

        [Test]
        public void TickAll_SkipsATickableUntrackedEarlierInTheSamePass()
        {
            var first = new StubTickable();
            var second = new StubTickable();
            LevelMomentRuntime.Track(first);
            LevelMomentRuntime.Track(second);

            // A tick runs game code now (the pump raises callbacks), so it can
            // end an unrelated break — that break must not then be ticked.
            first.OnTick = () => LevelMomentRuntime.Untrack(second);

            LevelMomentRuntime.TickAll();

            Assert.AreEqual(1, first.Ticks);
            Assert.AreEqual(0, second.Ticks, "an entry untracked mid-pass must not be ticked");
        }

        [Test]
        public void TickAll_DefersATickableTrackedDuringThePassToTheNextPass()
        {
            var first = new StubTickable();
            var late = new StubTickable();
            LevelMomentRuntime.Track(first);
            first.OnTick = () => LevelMomentRuntime.Track(late);

            LevelMomentRuntime.TickAll();
            Assert.AreEqual(0, late.Ticks, "registering during a pass waits for the next frame");

            LevelMomentRuntime.TickAll();
            Assert.AreEqual(1, late.Ticks);
        }

        [Test]
        public void TickAll_DropsANestedCallInsteadOfReTicking()
        {
            var tickable = new StubTickable();
            LevelMomentRuntime.Track(tickable);
            tickable.OnTick = () => LevelMomentRuntime.TickAll();

            Assert.DoesNotThrow(() => LevelMomentRuntime.TickAll());
            Assert.AreEqual(1, tickable.Ticks, "a nested pass must not re-tick the pass in progress");
        }

        [Test]
        public void ShowInterstitial_FromEveryDisplayFailedCallback_TerminatesWithABoundedEventCount()
        {
            // The round-4 P1: a handler that unconditionally calls back in
            // used to recurse into an uncatchable StackOverflowException.
            // With delivery on the pump it costs exactly one event per frame.
            var failedCount = 0;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (adUnitId, error, info) =>
            {
                failedCount++;
                LevelMomentMaxSdk.ShowInterstitial(adUnitId); // never loaded: fails again
            };

            LevelMomentMaxSdk.ShowInterstitial("never-loaded-unit");
            Assert.AreEqual(0, failedCount, "nothing is delivered inside the call");

            Assert.DoesNotThrow(() => Pump(10));

            Assert.AreEqual(10, failedCount, "exactly one event per frame — bounded, not recursive");
        }

        [Test]
        public void LoadInterstitial_FromEveryLoadFailedCallback_TerminatesWithABoundedEventCount()
        {
            var failedCount = 0;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += (adUnitId, error) =>
            {
                failedCount++;
                LevelMomentMaxSdk.LoadInterstitial(adUnitId); // still unmapped: fails again
            };

            LevelMomentMaxSdk.LoadInterstitial("unmapped-unit");

            Assert.DoesNotThrow(() => Pump(5));
            Assert.AreEqual(5, failedCount);
        }

        [Test]
        public void LoadInterstitial_NullAdUnitId_RetryFromTheHandlerTerminates()
        {
#if UNITY_5_3_OR_NEWER
            LogAssert.ignoreFailingMessages = true;
#endif
            try
            {
                var failedCount = 0;
                LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += (adUnitId, error) =>
                {
                    failedCount++;
                    LevelMomentMaxSdk.LoadInterstitial(null);
                };

                LevelMomentMaxSdk.LoadInterstitial("");
                Assert.DoesNotThrow(() => Pump(3));

                Assert.AreEqual(3, failedCount, "a null id retries on a frame boundary like any other");
            }
            finally
            {
#if UNITY_5_3_OR_NEWER
                LogAssert.ignoreFailingMessages = false;
#endif
            }
        }

        [Test]
        public void ShowInterstitial_CalledFromWithinOnAdLoadedEvent_ShowsOnTheNextFrame()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            // The common MAX pattern: show as soon as the load completes.
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (adUnitId, info) =>
                LevelMomentMaxSdk.ShowInterstitial(adUnitId);

            var displayed = 0;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += (adUnitId, info) => displayed++;

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Assert.AreEqual(0, fake.OpenCount, "OnAdLoadedEvent has not been delivered yet");

            Pump();
            Assert.AreEqual(1, fake.OpenCount, "the handler's Show runs as a fresh top-level call");

            fake.EmitMessage(Ready);
            Pump();
            Assert.AreEqual(1, displayed);
        }

        [Test]
        public void ShowRewardedAd_CalledFromWithinOnAdLoadedEvent_ShowsOnTheNextFrame()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("runit", "p1");

            LevelMomentMaxSdkCallbacks.Rewarded.OnAdLoadedEvent += (adUnitId, info) =>
                LevelMomentMaxSdk.ShowRewardedAd(adUnitId);

            LevelMomentMaxSdk.LoadRewardedAd("runit");
            Pump();

            Assert.AreEqual(1, fake.OpenCount);
        }

        [Test]
        public void LoadInterstitial_CalledFromWithinOnAdDisplayFailedEvent_ActuallyLoads()
        {
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            var retried = false;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (adUnitId, error, info) =>
            {
                if (retried)
                    return;
                retried = true;
                LevelMomentMaxSdk.LoadInterstitial(adUnitId);
            };

            var loaded = 0;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (adUnitId, info) => loaded++;

            LevelMomentMaxSdk.ShowInterstitial("unit1"); // never loaded — fails, the handler retries Load
            Pump(); // delivers not_loaded; the handler's Load runs here
            Assert.IsTrue(retried);
            Assert.IsTrue(LevelMomentMaxSdk.IsInterstitialReady("unit1"), "the retried Load ran");

            Pump(); // delivers that Load's OnAdLoadedEvent
            Assert.AreEqual(1, loaded);
        }

        // ---- Rejecting a null/empty adUnitId -------------------------------------

        [Test]
        public void LoadInterstitial_NullAdUnitId_FiresLoadFailedWithInvalidAdUnitIdAndLogsAnError()
        {
#if UNITY_5_3_OR_NEWER
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("adUnitId must not be null or empty"));
#endif
            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += (adUnitId, error) => failedError = error;

            Assert.DoesNotThrow(() => LevelMomentMaxSdk.LoadInterstitial(null));
            Pump();

            Assert.IsNotNull(failedError);
            Assert.AreEqual("invalid_ad_unit_id", failedError.Code);
        }

        [Test]
        public void ShowInterstitial_EmptyAdUnitId_FiresDisplayFailedWithInvalidAdUnitIdAndLogsAnError()
        {
#if UNITY_5_3_OR_NEWER
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("adUnitId must not be null or empty"));
#endif
            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (adUnitId, error, info) => failedError = error;

            Assert.DoesNotThrow(() => LevelMomentMaxSdk.ShowInterstitial(""));
            Pump();

            Assert.IsNotNull(failedError);
            Assert.AreEqual("invalid_ad_unit_id", failedError.Code);
        }

        [Test]
        public void LoadRewardedAd_NullAdUnitId_FiresLoadFailedWithInvalidAdUnitId()
        {
#if UNITY_5_3_OR_NEWER
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("adUnitId must not be null or empty"));
#endif
            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += (adUnitId, error) => failedError = error;

            Assert.DoesNotThrow(() => LevelMomentMaxSdk.LoadRewardedAd(null));
            Pump();

            Assert.IsNotNull(failedError);
            Assert.AreEqual("invalid_ad_unit_id", failedError.Code);
        }

        [Test]
        public void ShowRewardedAd_NullAdUnitId_FiresDisplayFailedWithInvalidAdUnitId()
        {
#if UNITY_5_3_OR_NEWER
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("adUnitId must not be null or empty"));
#endif
            ErrorInfo failedError = null;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += (adUnitId, error, info) => failedError = error;

            Assert.DoesNotThrow(() => LevelMomentMaxSdk.ShowRewardedAd(null));
            Pump();

            Assert.IsNotNull(failedError);
            Assert.AreEqual("invalid_ad_unit_id", failedError.Code);
        }

        // ---- Domain-Reload-disabled reset clears callback subscribers too -------

        [Test]
        public void ResetForTests_ClearsCallbackSubscribersToo_PreventingDoubleFiringAcrossSessions()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            LevelMomentMaxSdk.MapAdUnit("runit", "p1");

            var rewardCount = 0;
            LevelMomentMaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (adUnitId, r, info) => rewardCount++;

            // Simulate what ResetStaticStateOnSubsystemRegistration does for
            // a second play session with Domain Reload disabled.
            LevelMomentMaxSdk.ResetForTests();
            LevelMomentMaxSdkCallbacks.ResetForTests();

            LevelMomentMaxSdk.MapAdUnit("runit", "p1");
            LevelMomentMaxSdk.LoadRewardedAd("runit");
            LevelMomentMaxSdk.ShowRewardedAd("runit");
            fake.EmitMessage(Reward(1, "impression-1"));
            Pump();

            Assert.AreEqual(0, rewardCount, "the first session's subscriber must not still be attached");
        }

        [Test]
        public void ResetForTests_DropsEventsStillWaitingOnThePump()
        {
            LevelMomentMaxSdk.MapAdUnit("unit1", "p1");

            var loadedCount = 0;
            LevelMomentMaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (adUnitId, info) => loadedCount++;

            LevelMomentMaxSdk.LoadInterstitial("unit1");
            Assert.AreEqual(1, MaxEventPump.Instance.PendingCount);

            LevelMomentMaxSdk.ResetForTests();
            Assert.AreEqual(0, MaxEventPump.Instance.PendingCount);

            Pump();
            Assert.AreEqual(0, loadedCount, "a queued event must not survive the statics reset");
        }

        // ---- SDK init -----------------------------------------------------------

        [Test]
        public void InitializeSdk_FiresOnSdkInitializedEventOnALaterFrame()
        {
            var initialized = false;
            LevelMomentMaxSdkCallbacks.OnSdkInitializedEvent += () => initialized = true;

            LevelMomentMaxSdk.InitializeSdk();
            Assert.IsFalse(initialized, "MAX's init callback is async, and so is this one");

            Pump();
            Assert.IsTrue(initialized);
        }

        [Test]
        public void InitializeSdk_SubscribingAfterTheCallStillHearsTheEvent()
        {
            LevelMomentMaxSdk.InitializeSdk();

            var initialized = false;
            LevelMomentMaxSdkCallbacks.OnSdkInitializedEvent += () => initialized = true;

            Pump();
            Assert.IsTrue(initialized);
        }
    }
}
