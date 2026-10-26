// EditMode tests for offline handling: require-internet mode (the watcher, its
// message, Try again and the game's callback), the offline short-circuit on
// every hosted entry point, and the loading layer over a surface that has not
// posted `ready`. The device probe and the clock are injected, and fake
// WebViews stand in for real ones. Mirrors sdk/web (connection.test.ts) and
// sdk/flutter (connection_test.dart).
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class ConnectionTests
    {
        private double _now;
        private bool _offline;
        private int _webViewsCreated;
        private List<bool> _changes;

        // ---- Fakes ----------------------------------------------------------

        private class FakeWebView : ILevelMomentWebView
        {
            public event Action<string> OnMessage;
#pragma warning disable 0067
            public event Action OnClosed;
#pragma warning restore 0067

            public int OpenCount;
            public int CloseCount;

            public void Open(string url)
            {
                OpenCount++;
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

        private class FakeRevealableWebView : FakeWebView, ILevelMomentRevealableWebView
        {
            public int OpenHiddenCount;
            public int RevealCount;

            public void OpenHidden(string url)
            {
                OpenHiddenCount++;
            }

            public void Reveal()
            {
                RevealCount++;
            }
        }

        private class FakeHeadlessWebView : FakeWebView, ILevelMomentHeadlessWebView
        {
            public int OpenHiddenCount;

            public void OpenHidden(string url)
            {
                OpenHiddenCount++;
            }
        }

        private const string Ready = "{\"type\":\"ready\"}";

        private static string ErrorMsg(string code)
        {
            return "{\"type\":\"error\",\"payload\":{\"code\":\"" + code + "\",\"message\":\"m\"}}";
        }

        // ---- Fixture --------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            LevelMomentAds.ResetForTests();
            LevelMomentWebViewRegistry.Reset();
            RewardedAd.SkipRuntimeDriver = true;
            LevelMomentRuntime.ResetForTests();
            _now = 0;
            _offline = false;
            _webViewsCreated = 0;
            _changes = new List<bool>();
            LevelMomentAds.ClockSeconds = () => _now;
            LevelMomentConnection.ReportsOffline = () => _offline;
        }

        [TearDown]
        public void TearDown()
        {
            LevelMomentWebViewRegistry.Reset();
            RewardedAd.SkipRuntimeDriver = false;
            LevelMomentRuntime.ResetForTests();
            LevelMomentAds.ResetForTests();
        }

        private void Initialize(bool requireInternet, string gameName = "Sheepdog")
        {
            LevelMomentAds.Initialize(new LevelMomentConfig
            {
                UnsafeTesting = new UnsafeTesting
                {
                    ApiUrl = "https://api.example.com",
                    BreakUrl = "https://app.example.com/break",
                },
                RequireInternet = requireInternet,
                GameName = gameName,
                OnConnectionChange = online => _changes.Add(online),
            });
        }

        private T Register<T>(T webView) where T : ILevelMomentWebView
        {
            LevelMomentWebViewRegistry.Register(delegate
            {
                _webViewsCreated++;
                return webView;
            });
            return webView;
        }

        private RewardedAd LoadRewarded()
        {
            RewardedAd ad = null;
            RewardedAd.Load("p1", new RewardedAdLoadCallbacks { OnAdLoaded = a => ad = a });
            Assert.IsNotNull(ad);
            return ad;
        }

        private InterstitialAd LoadInterstitial()
        {
            InterstitialAd ad = null;
            InterstitialAd.Load("p1", new InterstitialAdLoadCallbacks { OnAdLoaded = a => ad = a });
            Assert.IsNotNull(ad);
            return ad;
        }

        // ---- Copy -----------------------------------------------------------

        [Test]
        public void Copy_NamesTheGame_AndMatchesWebAndFlutter()
        {
            var copy = NoConnectionCopy.For("  Sheepdog ");
            Assert.AreEqual("No internet connection", copy.Heading);
            Assert.AreEqual(
                "Sheepdog needs an internet connection. Turn on Wi-Fi or mobile data, then try again.",
                copy.Body);
            Assert.AreEqual("Try again", copy.Button);
        }

        [Test]
        public void Copy_FallsBackToThisGame()
        {
            StringAssert.StartsWith("This game needs", NoConnectionCopy.For(null).Body);
            StringAssert.StartsWith("This game needs", NoConnectionCopy.For("   ").Body);
        }

        // ---- Require-internet mode -----------------------------------------

        [Test]
        public void Online_AtInitialize_ShowsNothing_AndCallsNothing()
        {
            Initialize(true);
            LevelMomentRuntime.TickAll();

            Assert.IsTrue(LevelMomentAds.IsOnline);
            Assert.IsNull(LevelMomentChrome.NoConnection);
            CollectionAssert.IsEmpty(_changes);
        }

        [Test]
        public void Offline_AtInitialize_ShowsMessageAtOnce_AndCallsBackOnTheNextFrame()
        {
            _offline = true;
            Initialize(true);

            Assert.IsNotNull(LevelMomentChrome.NoConnection);
            Assert.IsFalse(LevelMomentAds.IsOnline);
            CollectionAssert.IsEmpty(_changes, "not from inside Initialize()");

            LevelMomentRuntime.TickAll();
            CollectionAssert.AreEqual(new[] { false }, _changes);

            LevelMomentRuntime.TickAll();
            CollectionAssert.AreEqual(new[] { false }, _changes, "once");
        }

        [Test]
        public void Offline_AtInitialize_ThenBackBeforeTheFirstFrame_CallsNothing()
        {
            _offline = true;
            Initialize(true);
            _offline = false;
            LevelMomentRuntime.NotifyApplicationResumed();
            LevelMomentRuntime.TickAll();

            Assert.IsNull(LevelMomentChrome.NoConnection);
            CollectionAssert.IsEmpty(_changes);
        }

        [Test]
        public void GoingOffline_IsSeenAtThePollInterval_NotBefore()
        {
            Initialize(true);
            _offline = true;

            _now = LevelMomentConnection.PollIntervalSeconds - 0.1;
            LevelMomentRuntime.TickAll();
            Assert.IsNull(LevelMomentChrome.NoConnection);
            CollectionAssert.IsEmpty(_changes);

            _now = LevelMomentConnection.PollIntervalSeconds;
            LevelMomentRuntime.TickAll();
            Assert.IsNotNull(LevelMomentChrome.NoConnection);
            Assert.IsFalse(LevelMomentAds.IsOnline);
            CollectionAssert.AreEqual(new[] { false }, _changes);
        }

        [Test]
        public void ConnectionReturning_RemovesMessage_AndCallsBackTrue()
        {
            Initialize(true);
            _offline = true;
            _now = 2;
            LevelMomentRuntime.TickAll();

            _offline = false;
            _now = 4;
            LevelMomentRuntime.TickAll();

            Assert.IsNull(LevelMomentChrome.NoConnection);
            Assert.IsTrue(LevelMomentAds.IsOnline);
            CollectionAssert.AreEqual(new[] { false, true }, _changes);
        }

        [Test]
        public void ApplicationResumed_RechecksAtOnce()
        {
            Initialize(true);
            _offline = true;
            LevelMomentRuntime.NotifyApplicationResumed();

            Assert.IsNotNull(LevelMomentChrome.NoConnection);
            CollectionAssert.AreEqual(new[] { false }, _changes);
        }

        [Test]
        public void TryAgain_WhileStillOffline_KeepsTheMessage()
        {
            _offline = true;
            Initialize(true);
            LevelMomentRuntime.TickAll();

            LevelMomentChrome.NoConnection.Press();

            Assert.IsNotNull(LevelMomentChrome.NoConnection);
            CollectionAssert.AreEqual(new[] { false }, _changes);
        }

        [Test]
        public void TryAgain_OnceOnline_RemovesTheMessage_WithoutWaitingForThePoll()
        {
            _offline = true;
            Initialize(true);
            LevelMomentRuntime.TickAll();

            _offline = false;
            LevelMomentChrome.NoConnection.Press();

            Assert.IsNull(LevelMomentChrome.NoConnection);
            CollectionAssert.AreEqual(new[] { false, true }, _changes);
        }

        [Test]
        public void Dispose_RemovesTheMessage_AndStopsWatching()
        {
            _offline = true;
            Initialize(true);
            LevelMomentRuntime.TickAll();

            LevelMomentAds.Dispose();
            Assert.IsNull(LevelMomentChrome.NoConnection);
            Assert.AreEqual(0, LevelMomentRuntime.TrackedCount);

            _offline = false;
            _now = 10;
            LevelMomentRuntime.TickAll();
            LevelMomentRuntime.NotifyApplicationResumed();
            CollectionAssert.AreEqual(new[] { false }, _changes);

            LevelMomentAds.Dispose(); // idempotent
        }

        [Test]
        public void Initialize_AgainWithoutTheMode_StopsWatching()
        {
            _offline = true;
            Initialize(true);
            Initialize(false);

            Assert.IsNull(LevelMomentChrome.NoConnection);
            Assert.AreEqual(0, LevelMomentRuntime.TrackedCount);
        }

        [Test]
        public void Initialize_AgainWithTheMode_KeepsOneMessage()
        {
            _offline = true;
            Initialize(true);
            Initialize(true);

            Assert.AreEqual(1, LevelMomentChrome.Layers.Count);
        }

        [Test]
        public void Initialize_AgainWhileOffline_DoesNotSayFalseTwice()
        {
            _offline = true;
            Initialize(true);
            LevelMomentRuntime.TickAll();
            Initialize(true);
            LevelMomentRuntime.TickAll();

            CollectionAssert.AreEqual(new[] { false }, _changes);
            Assert.IsNotNull(LevelMomentChrome.NoConnection);

            _offline = false;
            LevelMomentRuntime.NotifyApplicationResumed();
            CollectionAssert.AreEqual(new[] { false, true }, _changes);
        }

        [Test]
        public void Initialize_AgainAfterTheConnectionReturned_SaysTrue()
        {
            _offline = true;
            Initialize(true);
            LevelMomentRuntime.TickAll();
            _offline = false;
            Initialize(true);
            LevelMomentRuntime.TickAll();

            CollectionAssert.AreEqual(new[] { false, true }, _changes);
            Assert.IsNull(LevelMomentChrome.NoConnection);
        }

        [Test]
        public void Initialize_TwiceMoreAfterTheConnectionReturned_StillSaysTrue()
        {
            _offline = true;
            Initialize(true);
            LevelMomentRuntime.TickAll();
            _offline = false;
            Initialize(true);
            Initialize(true);
            LevelMomentRuntime.TickAll();

            CollectionAssert.AreEqual(new[] { false, true }, _changes);
        }

        [Test]
        public void APressDuringOnGui_RunsOnTheNextUpdate()
        {
            _offline = true;
            Initialize(true);
            LevelMomentRuntime.TickAll();
            _offline = false;

            LevelMomentChrome.RequestPress(LevelMomentChrome.NoConnection);
            CollectionAssert.AreEqual(new[] { false }, _changes, "not from inside OnGUI");

            LevelMomentChrome.RunPendingPress();
            CollectionAssert.AreEqual(new[] { false, true }, _changes);
            LevelMomentChrome.RunPendingPress();
            CollectionAssert.AreEqual(new[] { false, true }, _changes, "once");
        }

        [Test]
        public void WithoutTheMode_IsOnlineReadsTheDevice_AndNothingIsShown()
        {
            Initialize(false);
            Assert.IsTrue(LevelMomentAds.IsOnline);

            _offline = true;
            Assert.IsFalse(LevelMomentAds.IsOnline);
            Assert.IsNull(LevelMomentChrome.NoConnection);
            CollectionAssert.IsEmpty(_changes);
        }

        [Test]
        public void AProbeThatThrows_ReadsAsOnline()
        {
            LevelMomentConnection.ReportsOffline = () => { throw new InvalidOperationException(); };
            Initialize(true);
            LevelMomentRuntime.TickAll();

            Assert.IsTrue(LevelMomentAds.IsOnline);
            Assert.IsNull(LevelMomentChrome.NoConnection);
        }

        [Test]
        public void TheMessage_IsDrawnOverAnyLoadingLayer()
        {
            Initialize(true);
            var first = LevelMomentChrome.ShowLoading(null);
            _offline = true;
            LevelMomentRuntime.NotifyApplicationResumed();
            var second = LevelMomentChrome.ShowLoading(null);

            var layers = LevelMomentChrome.Layers;
            Assert.AreEqual(3, layers.Count);
            Assert.AreSame(first, layers[0]);
            Assert.AreSame(second, layers[1]);
            Assert.AreSame(LevelMomentChrome.NoConnection, layers[2]);
        }

        // ---- Offline short-circuit, every entry point -----------------------

        [Test]
        public void RewardedShow_Offline_FailsWithNetworkError_WithoutAWebView()
        {
            Initialize(false);
            Register(new FakeWebView());
            var ad = LoadRewarded();
            _offline = true;

            LevelMomentAdError error = null;
            var dismissed = 0;
            ad.Show(new RewardedAdShowCallbacks
            {
                OnAdFailedToShow = e => error = e,
                OnAdDismissed = () => dismissed++,
            });

            Assert.IsNotNull(error);
            Assert.AreEqual("network_error", error.Code);
            Assert.AreEqual(0, dismissed);
            Assert.AreEqual(0, _webViewsCreated);
            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);
            Assert.IsFalse(ad.IsLoaded, "the handle is spent, like any other failed show");
        }

        [Test]
        public void InterstitialShow_Offline_FailsWithNetworkError_WithoutAWebView()
        {
            Initialize(false);
            Register(new FakeWebView());
            var ad = LoadInterstitial();
            _offline = true;

            LevelMomentAdError error = null;
            ad.Show(new InterstitialAdShowCallbacks { OnAdFailedToShow = e => error = e });

            Assert.AreEqual("network_error", error.Code);
            Assert.AreEqual(0, _webViewsCreated);
        }

        [Test]
        public void RewardedShow_Offline_UnderTheMode_FailsFirst_ThenPausesOnTheNextFrame()
        {
            var order = new List<string>();
            LevelMomentAds.Initialize(new LevelMomentConfig
            {
                UnsafeTesting = new UnsafeTesting
                {
                    ApiUrl = "https://api.example.com",
                    BreakUrl = "https://app.example.com/break",
                },
                RequireInternet = true,
                OnConnectionChange = online => order.Add("online=" + online),
            });
            Register(new FakeWebView());
            var ad = LoadRewarded();
            _offline = true; // before the watcher's next poll

            ad.Show(new RewardedAdShowCallbacks { OnAdFailedToShow = e => order.Add(e.Code) });

            // The game resumes from the failure first and pauses on the next
            // frame, so it never ends up playing under the message.
            CollectionAssert.AreEqual(new[] { "network_error" }, order);
            LevelMomentRuntime.TickAll();
            CollectionAssert.AreEqual(new[] { "network_error", "online=False" }, order);
            Assert.IsNotNull(LevelMomentChrome.NoConnection);
        }

        [Test]
        public void EnsureSignedIn_Offline_IsATechnicalFailure_WithoutAWebView()
        {
            Initialize(false);
            Register(new FakeWebView());
            _offline = true;

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", results.Add);

            CollectionAssert.AreEqual(new[] { EnsureSignedInResult.TechnicalFailure }, results);
            Assert.AreEqual(0, _webViewsCreated);
            Assert.AreEqual(0, LevelMomentRuntime.TrackedCount);
        }

        [Test]
        public void EnsureAccess_Offline_IsATechnicalFailure_WithoutAWebView()
        {
            Initialize(false);
            Register(new FakeWebView());
            _offline = true;

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureAccess("p1", results.Add);

            CollectionAssert.AreEqual(new[] { EnsureSignedInResult.TechnicalFailure }, results);
            Assert.AreEqual(0, _webViewsCreated);
        }

        [Test]
        public void IsSignedIn_Offline_ReportsAnError_NeverFalse()
        {
            Initialize(false);
            Register(new FakeHeadlessWebView());
            _offline = true;

            var answers = new List<bool>();
            string error = null;
            LevelMomentAds.IsSignedIn("p1", answers.Add, e => error = e);

            CollectionAssert.IsEmpty(answers);
            Assert.AreEqual(
                "Level Moment could not check this device: this device has no internet connection.", error);
            Assert.AreEqual(0, _webViewsCreated);
        }

        [Test]
        public void CheckAccess_Offline_ReportsAnError_NeverFalse()
        {
            Initialize(false);
            Register(new FakeHeadlessWebView());
            _offline = true;

            var answers = new List<bool>();
            string error = null;
            LevelMomentAds.CheckAccess("p1", answers.Add, e => error = e);

            CollectionAssert.IsEmpty(answers);
            Assert.AreEqual(
                "Level Moment could not check access: this device has no internet connection.", error);
            Assert.AreEqual(0, _webViewsCreated);
        }

        [Test]
        public void MockGate_Offline_StillReportsReady()
        {
            LevelMomentAds.Initialize(new LevelMomentConfig { Mock = true });
            _offline = true;

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", results.Add);

            CollectionAssert.AreEqual(new[] { EnsureSignedInResult.Ready }, results);
        }

        [Test]
        public void OnlineButServerDown_IsNotBlocked_TheBreakFailsAndPlayContinues()
        {
            Initialize(true);
            var webView = Register(new FakeWebView());
            var ad = LoadRewarded();

            LevelMomentAdError error = null;
            ad.Show(new RewardedAdShowCallbacks { OnAdFailedToShow = e => error = e });
            webView.EmitMessage(ErrorMsg("network_error"));

            Assert.AreEqual("network_error", error.Code);
            Assert.IsNull(LevelMomentChrome.NoConnection, "a server failure never shows the message");
            CollectionAssert.IsEmpty(_changes);
        }

        // ---- The loading layer ----------------------------------------------

        [Test]
        public void Break_ShowsTheLoadingLayer_UntilReady()
        {
            Initialize(false);
            var webView = Register(new FakeWebView());
            var ad = LoadRewarded();
            var showed = 0;
            ad.Show(new RewardedAdShowCallbacks { OnAdShowedFullScreenContent = () => showed++ });

            Assert.AreEqual(1, LevelMomentChrome.LoadingCount);
            Assert.AreEqual(1, webView.OpenCount);

            webView.EmitMessage(Ready);
            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);
            Assert.AreEqual(1, showed);
        }

        [Test]
        public void Break_ClosingTheLoadingLayer_DismissesAndClosesTheWebView()
        {
            Initialize(false);
            var webView = Register(new FakeWebView());
            var ad = LoadRewarded();
            var dismissed = 0;
            LevelMomentAdError error = null;
            ad.Show(new RewardedAdShowCallbacks
            {
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => error = e,
            });

            LevelMomentChrome.Layers[0].Press();

            Assert.AreEqual(1, dismissed);
            Assert.IsNull(error);
            Assert.AreEqual(1, webView.CloseCount);
            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);

            webView.EmitMessage(Ready);
            Assert.AreEqual(1, dismissed, "terminal once");
        }

        [Test]
        public void Break_AnErrorBeforeReady_RemovesTheLoadingLayer()
        {
            Initialize(false);
            var webView = Register(new FakeWebView());
            var ad = LoadRewarded();
            ad.Show(new RewardedAdShowCallbacks());

            webView.EmitMessage(ErrorMsg("network_error"));
            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);
        }

        [Test]
        public void Break_TheLoadWatchdog_RemovesTheLoadingLayer()
        {
            Initialize(false);
            Register(new FakeWebView());
            var ad = LoadRewarded();
            ad.Show(new RewardedAdShowCallbacks());

            _now = LevelMomentAds.DefaultLoadTimeoutSeconds;
            LevelMomentRuntime.TickAll();
            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);
        }

        [Test]
        public void Break_ARevealableProvider_OpensHidden_AndRevealsOnReady()
        {
            Initialize(false);
            var webView = Register(new FakeRevealableWebView());
            var ad = LoadInterstitial();
            ad.Show(new InterstitialAdShowCallbacks());

            Assert.AreEqual(1, webView.OpenHiddenCount);
            Assert.AreEqual(0, webView.OpenCount);
            Assert.AreEqual(0, webView.RevealCount);

            webView.EmitMessage(Ready);
            Assert.AreEqual(1, webView.RevealCount);
        }

        private class ThrowingRevealWebView : FakeRevealableWebView, ILevelMomentRevealableWebView
        {
            void ILevelMomentRevealableWebView.Reveal()
            {
                throw new InvalidOperationException("provider blew up on Reveal");
            }
        }

        [Test]
        public void Break_ARevealThatThrows_FailsTheBreak_InsteadOfHidingIt()
        {
            Initialize(false);
            var webView = Register(new ThrowingRevealWebView());
            var ad = LoadRewarded();
            LevelMomentAdError error = null;
            var showed = 0;
            ad.Show(new RewardedAdShowCallbacks
            {
                OnAdFailedToShow = e => error = e,
                OnAdShowedFullScreenContent = () => showed++,
            });

            webView.EmitMessage(Ready);
            Assert.AreEqual("webview_error", error.Code);
            Assert.AreEqual(0, showed);
            Assert.AreEqual(1, webView.CloseCount);
            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);
        }

        [Test]
        public void Gate_ARevealThatThrows_IsATechnicalFailure()
        {
            Initialize(false);
            var webView = Register(new ThrowingRevealWebView());
            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", results.Add);

            webView.EmitMessage(Ready);
            CollectionAssert.AreEqual(new[] { EnsureSignedInResult.TechnicalFailure }, results);
            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);
        }

        [Test]
        public void Break_DestroyWhileLoading_RemovesTheLoadingLayer()
        {
            Initialize(false);
            Register(new FakeWebView());
            var ad = LoadRewarded();
            ad.Show(new RewardedAdShowCallbacks());

            ad.Destroy();
            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);
            Assert.AreEqual(0, LevelMomentRuntime.TrackedCount);
        }

        [Test]
        public void Gate_ShowsTheLoadingLayer_AndClosingItCancels()
        {
            Initialize(false);
            var webView = Register(new FakeRevealableWebView());
            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", results.Add);

            Assert.AreEqual(1, LevelMomentChrome.LoadingCount);
            Assert.AreEqual(1, webView.OpenHiddenCount);

            LevelMomentChrome.Layers[0].Press();
            CollectionAssert.AreEqual(new[] { EnsureSignedInResult.Canceled }, results);
            Assert.AreEqual(1, webView.CloseCount);
            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);
        }

        [Test]
        public void Gate_ReadyRemovesTheLoadingLayer_AndReveals()
        {
            Initialize(false);
            var webView = Register(new FakeRevealableWebView());
            LevelMomentAds.EnsureAccess("p1", delegate { });

            webView.EmitMessage(Ready);
            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);
            Assert.AreEqual(1, webView.RevealCount);
        }

        [Test]
        public void HeadlessCheck_ShowsNoLoadingLayer_AndNeverReveals()
        {
            Initialize(false);
            var webView = Register(new FakeRevealableWebView());
            LevelMomentAds.IsSignedIn("p1", delegate { }, delegate { });

            Assert.AreEqual(0, LevelMomentChrome.LoadingCount);
            Assert.AreEqual(1, webView.OpenHiddenCount);

            webView.EmitMessage(Ready);
            Assert.AreEqual(0, webView.RevealCount);
        }
    }
}
