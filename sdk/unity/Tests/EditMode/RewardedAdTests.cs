// EditMode tests for RewardedAd — the WebView-shell bridge logic. A fake
// ILevelMomentWebView stands in for a real WebView so the message → callback
// mapping, the terminal-once collapse, and the watchdog path are all exercised
// as pure C#. Mirrors the postMessage-bridge tests in sdk/web (client.test.ts)
// and the terminal-once discipline of sdk/flutter + sdk/react-native.
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class RewardedAdTests
    {
        private double _now;

        // ---- Fakes ----------------------------------------------------------

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

            public void EmitClosed()
            {
                if (OnClosed != null)
                    OnClosed();
            }
        }

        // ---- Bridge message helpers ----------------------------------------

        private const string Ready = "{\"type\":\"ready\"}";
        private const string Dismissed = "{\"type\":\"dismissed\"}";

        private static string Reward(int amount)
        {
            return "{\"type\":\"earnedReward\",\"payload\":{\"amount\":" + amount + "}}";
        }

        private static string ErrorMsg(string code, string message)
        {
            return "{\"type\":\"error\",\"payload\":{\"code\":\"" + code + "\",\"message\":\"" + message + "\"}}";
        }

        // ---- Fixture --------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            LevelMomentAds.ResetForTests();
            LevelMomentAds.Initialize(new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break",
            });
            LevelMomentAds.LaunchToken = () => "test-token";
            _now = 0;
            LevelMomentAds.ClockSeconds = () => _now;
            LevelMomentAds.LoadTimeoutSeconds = 15;
            LevelMomentWebViewRegistry.Reset();
            RewardedAd.SkipRuntimeDriver = true;
        }

        [TearDown]
        public void TearDown()
        {
            LevelMomentWebViewRegistry.Reset();
            RewardedAd.SkipRuntimeDriver = false;
            LevelMomentAds.ResetForTests();
        }

        private RewardedAd LoadAd()
        {
            RewardedAd ad = null;
            RewardedAd.Load("p1", new RewardedAdLoadCallbacks { OnAdLoaded = a => ad = a });
            return ad;
        }

        // ---- Load guards ----------------------------------------------------

        [Test]
        public void Load_WithoutInitialize_FailsWithNotInitialized()
        {
            LevelMomentAds.ResetForTests();
            LevelMomentAdError error = null;
            RewardedAd.Load("p1", new RewardedAdLoadCallbacks { OnAdFailedToLoad = e => error = e });
            Assert.IsNotNull(error);
            Assert.AreEqual("not_initialized", error.Code);
        }

        [Test]
        public void Load_EmptyPlacementId_FailsWithInvalidRequest()
        {
            LevelMomentAdError error = null;
            RewardedAd.Load("", new RewardedAdLoadCallbacks { OnAdFailedToLoad = e => error = e });
            Assert.IsNotNull(error);
            Assert.AreEqual("invalid_request", error.Code);
        }

        [Test]
        public void Load_MarksReadySynchronously()
        {
            var ad = LoadAd();
            Assert.IsNotNull(ad);
            Assert.IsTrue(ad.IsLoaded);
        }

        // ---- Show — URL + mounting -----------------------------------------

        [Test]
        public void Show_OpensWebViewWithBuiltUrl()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks());

            Assert.AreEqual(1, fake.OpenCount);
            StringAssert.Contains("placementId=p1", fake.LastUrl);
            StringAssert.Contains("token=test-token", fake.LastUrl);
        }

        [Test]
        public void Show_Twice_DoesNotOpenTwice()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks());
            ad.Show(new RewardedAdShowCallbacks());

            Assert.AreEqual(1, fake.OpenCount);
        }

        // ---- Ready ----------------------------------------------------------

        [Test]
        public void Ready_FiresOnAdShowed()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var shown = 0;
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks { OnAdShowedFullScreenContent = () => shown++ });

            fake.EmitMessage(Ready);
            Assert.AreEqual(1, shown);
        }

        // ---- earnedReward (non-terminal, may repeat) ------------------------

        [Test]
        public void EarnedReward_ForwardsEveryAnswer()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var amounts = new List<int>();
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks { OnUserEarnedReward = a => amounts.Add(a) });

            fake.EmitMessage(Ready);
            fake.EmitMessage(Reward(1));
            fake.EmitMessage(Reward(0));
            fake.EmitMessage(Reward(1));

            Assert.AreEqual(new List<int> { 1, 0, 1 }, amounts);
        }

        // ---- Terminal-once collapse -----------------------------------------

        [Test]
        public void Dismissed_FiresOnceEvenWhenRepeated()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var rewards = 0;
            var dismissed = 0;
            LevelMomentAdError failed = null;
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks
            {
                OnUserEarnedReward = a => rewards += a,
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => failed = e,
            });

            fake.EmitMessage(Ready);
            fake.EmitMessage(Reward(1));
            fake.EmitMessage(Dismissed);
            fake.EmitMessage(Dismissed); // duplicate terminal — collapsed
            fake.EmitMessage(ErrorMsg("late", "ignored")); // after terminal — ignored

            Assert.AreEqual(1, dismissed);
            Assert.AreEqual(1, rewards);
            Assert.IsNull(failed);
            Assert.AreEqual(1, fake.CloseCount);
        }

        [Test]
        public void Error_FiresOnAdFailedToShow_ThenCollapses()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var dismissed = 0;
            LevelMomentAdError failed = null;
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks
            {
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => failed = e,
            });

            fake.EmitMessage(ErrorMsg("no_fill", "no question"));
            fake.EmitMessage(Dismissed); // after terminal — ignored

            Assert.IsNotNull(failed);
            Assert.AreEqual("no_fill", failed.Code);
            Assert.AreEqual("no question", failed.Message);
            Assert.AreEqual(0, dismissed);
        }

        [Test]
        public void NativeClose_FiresOnAdDismissedOnce()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var dismissed = 0;
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks { OnAdDismissed = () => dismissed++ });

            fake.EmitClosed();
            fake.EmitMessage(Dismissed); // after terminal — ignored

            Assert.AreEqual(1, dismissed);
        }

        [Test]
        public void MalformedMessage_IsIgnored()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var dismissed = 0;
            LevelMomentAdError failed = null;
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks
            {
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => failed = e,
            });

            fake.EmitMessage("{not valid json");

            Assert.AreEqual(0, dismissed);
            Assert.IsNull(failed);
        }

        // ---- Watchdog through the ad ---------------------------------------

        [Test]
        public void Watchdog_FiresOnAdDismissedWhenNoReadyArrives()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var dismissed = 0;
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks { OnAdDismissed = () => dismissed++ });

            _now = 14.999;
            ad.Tick();
            Assert.AreEqual(0, dismissed);

            _now = 15.0;
            ad.Tick();
            Assert.AreEqual(1, dismissed);

            _now = 30;
            ad.Tick();
            Assert.AreEqual(1, dismissed, "watchdog must not fire twice");
        }

        [Test]
        public void Watchdog_ReadyBeforeDeadline_PreventsTimeout()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var dismissed = 0;
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks { OnAdDismissed = () => dismissed++ });

            fake.EmitMessage(Ready);
            _now = 1000;
            ad.Tick();

            Assert.AreEqual(0, dismissed);
        }

        // ---- No WebView provider -------------------------------------------

        [Test]
        public void Show_NoProvider_FailsWithClearError()
        {
            // No provider registered → NoWebViewFallback.
            var dismissed = 0;
            LevelMomentAdError failed = null;
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks
            {
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => failed = e,
            });

            Assert.IsNotNull(failed);
            Assert.AreEqual("no_webview_provider", failed.Code);
            Assert.AreEqual(0, dismissed);
        }
    }
}
