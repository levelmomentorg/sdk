// EditMode tests for InterstitialAd — mirrors RewardedAdTests.cs (both share
// BreakSurfaceCore), trimmed to what an interstitial break actually has: no
// reward dispatch, and `kind=interstitial` on the URL it opens instead of
// RewardedAd's bare URL. Run from Unity: Window → General → Test Runner →
// EditMode → Run All.

using System;
using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class InterstitialAdTests
    {
        private double _now;

        // ---- Fakes (same shapes as RewardedAdTests) --------------------------

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

        private class ThrowingWebView : ILevelMomentWebView
        {
#pragma warning disable 0067
            public event Action<string> OnMessage;
            public event Action OnClosed;
#pragma warning restore 0067

            public int CloseCount;

            public void Open(string url)
            {
                throw new InvalidOperationException("provider blew up on Open");
            }

            public void Close()
            {
                CloseCount++;
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
                UnsafeTesting = new UnsafeTesting { ApiUrl = "https://api.example.com", BreakUrl = "https://app.example.com/break" },
            });
            _now = 0;
            LevelMomentAds.ClockSeconds = () => _now;
            LevelMomentAds.LoadTimeoutSeconds = 15;
            LevelMomentWebViewRegistry.Reset();
            InterstitialAd.SkipRuntimeDriver = true;
        }

        [TearDown]
        public void TearDown()
        {
            LevelMomentWebViewRegistry.Reset();
            InterstitialAd.SkipRuntimeDriver = false;
            LevelMomentAds.ResetForTests();
        }

        private InterstitialAd LoadAd()
        {
            InterstitialAd ad = null;
            InterstitialAd.Load("p1", new InterstitialAdLoadCallbacks { OnAdLoaded = a => ad = a });
            return ad;
        }

        // ---- Load guards ----------------------------------------------------

        [Test]
        public void Load_WithoutInitialize_FailsWithNotInitialized()
        {
            LevelMomentAds.ResetForTests();
            LevelMomentAdError error = null;
            InterstitialAd.Load("p1", new InterstitialAdLoadCallbacks { OnAdFailedToLoad = e => error = e });
            Assert.IsNotNull(error);
            Assert.AreEqual("not_initialized", error.Code);
        }

        [Test]
        public void Load_EmptyPlacementId_FailsWithInvalidRequest()
        {
            LevelMomentAdError error = null;
            InterstitialAd.Load("", new InterstitialAdLoadCallbacks { OnAdFailedToLoad = e => error = e });
            Assert.IsNotNull(error);
            Assert.AreEqual("invalid_request", error.Code);
        }

        // BreakSurfaceCore.TryResolveLoad's third guard: ResolveStudentToken
        // throws for a non-sandbox token even though UnsafeTesting is
        // configured (this fixture's SetUp does). The throw must not escape
        // Load() — it becomes an invalid_request callback, like the other two
        // guards.
        [Test]
        public void Load_WithNonSandboxToken_FailsWithInvalidRequestAndDoesNotThrow()
        {
            LevelMomentAdError error = null;
            Assert.DoesNotThrow(() => InterstitialAd.Load(
                "p1",
                "not-a-sandbox-token",
                new InterstitialAdLoadCallbacks { OnAdFailedToLoad = e => error = e }));

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

        // ---- Show — URL + mounting, including the `kind` this placement adds -

        [Test]
        public void Show_OpensWebViewWithBuiltUrlIncludingInterstitialKind()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var ad = LoadAd();
            ad.Show(new InterstitialAdShowCallbacks());

            Assert.AreEqual(1, fake.OpenCount);
            StringAssert.Contains("placementId=p1", fake.LastUrl);
            StringAssert.Contains("kind=interstitial", fake.LastUrl);
        }

        [Test]
        public void Show_Twice_DoesNotOpenTwice()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var ad = LoadAd();
            ad.Show(new InterstitialAdShowCallbacks());
            ad.Show(new InterstitialAdShowCallbacks());

            Assert.AreEqual(1, fake.OpenCount);
        }

        // ---- IsLoaded latches once shown (regression: Teardown must not ------
        // ---- reset `_shown`, or a dismissed break becomes re-showable) -------

        [Test]
        public void Show_ThenDismissed_IsLoadedStaysFalseAndASecondShowDoesNotOpen()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var ad = LoadAd();
            ad.Show(new InterstitialAdShowCallbacks());
            fake.EmitMessage(Dismissed);

            Assert.IsFalse(ad.IsLoaded);

            var failedCount = 0;
            var dismissedCount = 0;
            LevelMomentAdError failed = null;
            ad.Show(new InterstitialAdShowCallbacks
            {
                OnAdFailedToShow = e => { failedCount++; failed = e; },
                OnAdDismissed = () => dismissedCount++,
            });

            Assert.AreEqual(1, fake.OpenCount, "a dismissed break must not reopen its WebView");
            Assert.AreEqual(1, failedCount);
            Assert.IsNotNull(failed);
            Assert.AreEqual("not_loaded", failed.Code);
            Assert.AreEqual(0, dismissedCount);
        }

        // ---- Destroy() --------------------------------------------------------

        [Test]
        public void Destroy_ClosesTheWebViewAndFiresNoCallbacks()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var dismissed = 0;
            LevelMomentAdError failed = null;
            var ad = LoadAd();
            ad.Show(new InterstitialAdShowCallbacks
            {
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => failed = e,
            });

            ad.Destroy();

            Assert.AreEqual(1, fake.CloseCount);
            Assert.AreEqual(0, dismissed);
            Assert.IsNull(failed);
            Assert.IsFalse(ad.IsLoaded);

            // Torn down and terminal: a dismissed message arriving late, and a
            // watchdog tick, must both be no-ops after Destroy().
            fake.EmitMessage(Dismissed);
            _now = 1000;
            ad.Tick();
            Assert.AreEqual(0, dismissed);
            Assert.IsNull(failed);
        }

        // ---- Ready ------------------------------------------------------------

        [Test]
        public void Ready_FiresOnAdShowed()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var shown = 0;
            var ad = LoadAd();
            ad.Show(new InterstitialAdShowCallbacks { OnAdShowedFullScreenContent = () => shown++ });

            fake.EmitMessage(Ready);
            Assert.AreEqual(1, shown);
        }

        // ---- No reward path: an interstitial break has nothing to grant -----

        [Test]
        public void EarnedReward_IsDroppedWithoutThrowingOrTerminating()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var dismissed = 0;
            var ad = LoadAd();
            ad.Show(new InterstitialAdShowCallbacks { OnAdDismissed = () => dismissed++ });

            // InterstitialAdShowCallbacks has no OnUserEarnedReward slot to
            // begin with — the point of this test is that an `earnedReward`
            // message arriving anyway (a page that mis-served a rewarded
            // response for an interstitial kind) does not crash or end the
            // break early.
            Assert.DoesNotThrow(() => fake.EmitMessage(Reward(1)));
            Assert.AreEqual(0, dismissed);

            fake.EmitMessage(Dismissed);
            Assert.AreEqual(1, dismissed);
        }

        // ---- Terminal-once collapse (identical contract to RewardedAd) ------

        [Test]
        public void Dismissed_FiresOnceEvenWhenRepeated()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var dismissed = 0;
            LevelMomentAdError failed = null;
            var ad = LoadAd();
            ad.Show(new InterstitialAdShowCallbacks
            {
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => failed = e,
            });

            fake.EmitMessage(Ready);
            fake.EmitMessage(Dismissed);
            fake.EmitMessage(Dismissed); // duplicate terminal — collapsed
            fake.EmitMessage(ErrorMsg("late", "ignored")); // after terminal — ignored

            Assert.AreEqual(1, dismissed);
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
            ad.Show(new InterstitialAdShowCallbacks
            {
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => failed = e,
            });

            fake.EmitMessage(ErrorMsg("no_fill", "no interstitial"));
            fake.EmitMessage(Dismissed); // after terminal — ignored

            Assert.IsNotNull(failed);
            Assert.AreEqual("no_fill", failed.Code);
            Assert.AreEqual("no interstitial", failed.Message);
            Assert.AreEqual(0, dismissed);
        }

        [Test]
        public void NativeClose_FiresOnAdDismissedOnce()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var dismissed = 0;
            var ad = LoadAd();
            ad.Show(new InterstitialAdShowCallbacks { OnAdDismissed = () => dismissed++ });

            fake.EmitClosed();
            fake.EmitMessage(Dismissed); // after terminal — ignored

            Assert.AreEqual(1, dismissed);
        }

        // ---- Watchdog through the ad ------------------------------------------

        [Test]
        public void Watchdog_FiresOnAdDismissedWhenNoReadyArrives()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var dismissed = 0;
            var ad = LoadAd();
            ad.Show(new InterstitialAdShowCallbacks { OnAdDismissed = () => dismissed++ });

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

        // ---- No WebView provider -----------------------------------------------

        [Test]
        public void Show_NoProvider_FailsWithClearError()
        {
            var dismissed = 0;
            LevelMomentAdError failed = null;
            var ad = LoadAd();
            ad.Show(new InterstitialAdShowCallbacks
            {
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => failed = e,
            });

            Assert.IsNotNull(failed);
            Assert.AreEqual("no_webview_provider", failed.Code);
            Assert.AreEqual(0, dismissed);
        }

        // ---- A provider that throws on startup ----------------------------------

        [Test]
        public void Show_ProviderThrowingOnOpen_FailsToShowInsteadOfThrowing()
        {
            var fake = new ThrowingWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentAdError failed = null;
            var dismissed = 0;
            var ad = LoadAd();

            Assert.DoesNotThrow(() => ad.Show(new InterstitialAdShowCallbacks
            {
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => failed = e,
            }));

            Assert.IsNotNull(failed, "the game must hear about this on its own callback");
            Assert.AreEqual("webview_error", failed.Code);
            StringAssert.Contains("provider blew up on Open", failed.Message);
            Assert.AreEqual(0, dismissed, "a failure to show is not a dismissal");
            Assert.AreEqual(1, fake.CloseCount, "the ad must tear its WebView down");
        }
    }
}
