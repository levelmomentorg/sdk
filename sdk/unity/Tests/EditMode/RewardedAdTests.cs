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

        /// <summary>A provider that can run script inside the page, so the
        /// credential answer this SDK sends back is observable.</summary>
        private class ScriptableFakeWebView : ILevelMomentScriptableWebView
        {
            public event Action<string> OnMessage;
#pragma warning disable 0067
            public event Action OnClosed;
#pragma warning restore 0067

            public int EvaluateJSCount;
            public string LastJS;
            public string LastUrl;

            public void Open(string url)
            {
                LastUrl = url;
            }

            public void Close()
            {
            }

            public void EvaluateJS(string js)
            {
                EvaluateJSCount++;
                LastJS = js;
            }

            public void EmitMessage(string raw)
            {
                if (OnMessage != null)
                    OnMessage(raw);
            }
        }

        /// <summary>A third-party provider that throws out of Open(). Mirrors
        /// the fake in SignInGateTests.</summary>
        private class ThrowingWebView : ILevelMomentWebView
        {
            // Required by the interface; this fake never raises either.
#pragma warning disable 0067
            public event Action<string> OnMessage;
            public event Action OnClosed;
#pragma warning restore 0067

            public int CloseCount;

            /// <summary>Also throw from Close(), the way a half-built native
            /// view can when teardown runs after its Open() failed.</summary>
            public bool ThrowOnClose;

            public void Open(string url)
            {
                throw new InvalidOperationException("provider blew up on Open");
            }

            public void Close()
            {
                CloseCount++;
                if (ThrowOnClose)
                    throw new InvalidOperationException("provider blew up on Close");
            }
        }

        /// <summary>A provider that posts a message synchronously from Open(),
        /// the way the bundled NoWebViewFallback does.</summary>
        private class SyncMessageWebView : ILevelMomentWebView
        {
            public event Action<string> OnMessage;
#pragma warning disable 0067
            public event Action OnClosed;
#pragma warning restore 0067

            private readonly string _raw;

            public int CloseCount;

            public SyncMessageWebView(string raw)
            {
                _raw = raw;
            }

            public void Open(string url)
            {
                if (OnMessage != null)
                    OnMessage(_raw);
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

        /// <summary>
        /// Load with an explicit token — the only way a token reaches this SDK
        /// now, and only for a sandbox token while integrating.
        /// </summary>
        private RewardedAd LoadAdWithToken(string token)
        {
            RewardedAd ad = null;
            RewardedAd.Load("p1", token, new RewardedAdLoadCallbacks { OnAdLoaded = a => ad = a });
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

        // BreakSurfaceCore.TryResolveLoad's third guard: ResolveStudentToken
        // throws for a non-sandbox token even though UnsafeTesting is
        // configured (this fixture's SetUp does). The throw must not escape
        // Load() — it becomes an invalid_request callback, like the other two
        // guards.
        [Test]
        public void Load_WithNonSandboxToken_FailsWithInvalidRequestAndDoesNotThrow()
        {
            LevelMomentAdError error = null;
            Assert.DoesNotThrow(() => RewardedAd.Load(
                "p1",
                "not-a-sandbox-token",
                new RewardedAdLoadCallbacks { OnAdFailedToLoad = e => error = e }));

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
        }

        // The URL this placement opens must not change shape: BreakSurfaceCore
        // (shared with InterstitialAd) passes no `kind` for a rewarded break,
        // matching the URL before InterstitialAd existed.
        [Test]
        public void Show_NeverIncludesAKindParam()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks());

            StringAssert.DoesNotContain("kind=", fake.LastUrl);
        }

        // A token the game supplied never lands on the URL. It only ever reaches
        // the page through CredentialBridge — see the CredentialBridge tests.
        [Test]
        public void Show_NeverPutsTheTokenOnTheUrl()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var ad = LoadAdWithToken("eply_sbx_test-token");
            ad.Show(new RewardedAdShowCallbacks());

            StringAssert.DoesNotContain("token=", fake.LastUrl);
        }

        [Test]
        public void Show_MockModeDoesNotSendSandboxTokenToTheBridge()
        {
            var fake = new ScriptableFakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            var ad = LoadAdWithToken("eply_sbx_test-token");
            LevelMomentAds.Config.Mock = true;

            ad.Show(new RewardedAdShowCallbacks());
            fake.EmitMessage("{\"type\":\"needCredential\"}");

            Assert.AreEqual(1, fake.EvaluateJSCount);
            StringAssert.Contains("\\\"token\\\":\\\"\\\"", fake.LastJS);
        }

        // The launch URL is not a credential source. Web and React Native
        // stopped reading credentials off URLs; Unity kept reading `?token=`
        // from Application.absoluteURL as its DEFAULT, which put a live
        // credential in the browser history, in the referrer of everything the
        // page loaded next, and in any crash report taken afterwards — for
        // every game, without the game asking for it.
        //
        // There is no seam left to stub, which is the assertion: a load given
        // no token holds no token, so `needCredential` is answered with an
        // empty reply and the hosted origin's own stored credential is what
        // serves the break.
        [Test]
        public void Load_WithoutAnExplicitToken_HoldsNoCredential()
        {
            var fake = new ScriptableFakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks());
            fake.EmitMessage("{\"type\":\"needCredential\"}");

            // Answered — a silent host makes the page wait out its whole
            // window — but with nothing in it.
            Assert.AreEqual(1, fake.EvaluateJSCount);
            StringAssert.Contains("\\\"token\\\":\\\"\\\"", fake.LastJS);

            // Pin the origin check CredentialBridge emits: it must gate on
            // this break's own hosted origin (https://app.example.com, from
            // this fixture's BreakUrl), not a hardcoded or wildcard origin —
            // that is the guard that keeps a wandered-off WebView from
            // fishing a credential out of this host.
            StringAssert.Contains(
                "window.location.origin === \"https://app.example.com\"", fake.LastJS);
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

        // ---- IsLoaded latches once shown (regression: Teardown must not ------
        // ---- reset `_shown`, or a dismissed break becomes re-showable) -------

        [Test]
        public void Show_ThenDismissed_IsLoadedStaysFalseAndASecondShowDoesNotOpen()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks());
            fake.EmitMessage(Dismissed);

            Assert.IsFalse(ad.IsLoaded);

            var failedCount = 0;
            var dismissedCount = 0;
            LevelMomentAdError failed = null;
            ad.Show(new RewardedAdShowCallbacks
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
            ad.Show(new RewardedAdShowCallbacks
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

        [Test]
        public void EarnedReward_ForwardsOpaqueRewardId()
        {
            var fake = new ScriptableFakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentRewardItem item = null;
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks
            {
                OnUserEarnedRewardItem = value => item = value,
            });

            fake.EmitMessage("{\"type\":\"earnedReward\",\"payload\":{\"amount\":1,\"rewardId\":\"impression-7\"}}");

            Assert.IsNotNull(item);
            Assert.AreEqual(1, item.Amount);
            Assert.AreEqual("impression-7", item.RewardId);
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

        // ---- A provider that throws on startup ------------------------------

        [Test]
        public void Show_ProviderThrowingOnOpen_FailsToShowInsteadOfThrowing()
        {
            var fake = new ThrowingWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentAdError failed = null;
            var dismissed = 0;
            var ad = LoadAd();

            Assert.DoesNotThrow(() => ad.Show(new RewardedAdShowCallbacks
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

        [Test]
        public void Show_ProviderThrowingFromTheFactory_FailsToShowInsteadOfThrowing()
        {
            LevelMomentWebViewRegistry.Register(
                () => { throw new InvalidOperationException("factory blew up"); });

            LevelMomentAdError failed = null;
            var ad = LoadAd();

            Assert.DoesNotThrow(() => ad.Show(new RewardedAdShowCallbacks
            {
                OnAdFailedToShow = e => failed = e,
            }));

            Assert.IsNotNull(failed);
            Assert.AreEqual("webview_error", failed.Code);
        }

        [Test]
        public void Show_ProviderThrowingOnOpen_LeavesNoTickingAdBehind()
        {
            var fake = new ThrowingWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var calls = 0;
            var ad = LoadAd();
            ad.Show(new RewardedAdShowCallbacks
            {
                OnAdDismissed = () => calls++,
                OnAdFailedToShow = _ => calls++,
            });

            Assert.AreEqual(1, calls);

            // The watchdog was cancelled with the rest of the teardown, so a
            // later tick cannot report a second result.
            _now = 1000;
            ad.Tick();
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Show_ProviderThrowingFromClose_DoesNotMaskTheFailure()
        {
            var fake = new ThrowingWebView { ThrowOnClose = true };
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentAdError failed = null;
            var ad = LoadAd();

            Assert.DoesNotThrow(() => ad.Show(new RewardedAdShowCallbacks
            {
                OnAdFailedToShow = e => failed = e,
            }));

            Assert.IsNotNull(failed);
            Assert.AreEqual("webview_error", failed.Code);
        }

        // ---- A game callback that throws during a synchronous Open() --------

        [Test]
        public void Show_ThrowingShowedCallback_IsNotReportedAsAProviderFailure()
        {
            // The provider posts `ready` from inside Open(), so the game's
            // OnAdShowedFullScreenContent runs on Show()'s stack. An exception
            // from there is the game's, not the provider's.
            var fake = new SyncMessageWebView(Ready);
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentAdError failed = null;
            var dismissed = 0;
            var ad = LoadAd();

            Assert.Throws<InvalidOperationException>(() => ad.Show(new RewardedAdShowCallbacks
            {
                OnAdShowedFullScreenContent = () =>
                    throw new InvalidOperationException("game callback blew up"),
                OnAdDismissed = () => dismissed++,
                OnAdFailedToShow = e => failed = e,
            }));

            Assert.IsNull(failed, "a game's own exception is not a webview_error");
            Assert.AreEqual(0, dismissed);
            Assert.AreEqual(0, fake.CloseCount, "the ad opened; it must not be torn down");
        }

        [Test]
        public void Show_ThrowingRewardCallback_IsNotReportedAsAProviderFailure()
        {
            var fake = new SyncMessageWebView(Reward(1));
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentAdError failed = null;
            var ad = LoadAd();

            Assert.Throws<InvalidOperationException>(() => ad.Show(new RewardedAdShowCallbacks
            {
                OnUserEarnedReward = _ =>
                    throw new InvalidOperationException("game callback blew up"),
                OnAdFailedToShow = e => failed = e,
            }));

            Assert.IsNull(failed);
            Assert.AreEqual(0, fake.CloseCount);
        }

        [Test]
        public void Show_ThrowingDismissCallback_DoesNotBecomeASecondFailure()
        {
            // The page posts `dismissed` synchronously. The terminal already
            // fired, so a throwing OnAdDismissed must reach the game rather
            // than be converted into an OnAdFailedToShow it never expected.
            var fake = new SyncMessageWebView(Dismissed);
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentAdError failed = null;
            var ad = LoadAd();

            Assert.Throws<InvalidOperationException>(() => ad.Show(new RewardedAdShowCallbacks
            {
                OnAdDismissed = () =>
                    throw new InvalidOperationException("game callback blew up"),
                OnAdFailedToShow = e => failed = e,
            }));

            Assert.IsNull(failed, "the game must not hear a second, contradictory result");
        }

        [Test]
        public void Show_AfterAThrowingCallback_StillReportsARealProviderFailure()
        {
            // The flag Show() reads is left set by a throwing callback. A later
            // Show() must not inherit it and swallow a genuine provider fault.
            var syncFake = new SyncMessageWebView(Ready);
            LevelMomentWebViewRegistry.Register(() => syncFake);

            var ad = LoadAd();
            Assert.Throws<InvalidOperationException>(() => ad.Show(new RewardedAdShowCallbacks
            {
                OnAdShowedFullScreenContent = () =>
                    throw new InvalidOperationException("game callback blew up"),
            }));

            LevelMomentWebViewRegistry.Register(() => new ThrowingWebView());
            LevelMomentAdError failed = null;
            var next = LoadAd();
            Assert.DoesNotThrow(() => next.Show(new RewardedAdShowCallbacks
            {
                OnAdFailedToShow = e => failed = e,
            }));

            Assert.IsNotNull(failed);
            Assert.AreEqual("webview_error", failed.Code);
        }
    }
}
