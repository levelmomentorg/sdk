// EditMode tests for the startup sign-in gate. A fake ILevelMomentWebView
// stands in for a real WebView so the message → result mapping, the
// terminal-once collapse, the watchdog path, mock mode, and the headless check
// are all exercised as pure C#. Mirrors RewardedAdTests, and the gate tests in
// sdk/web (gate.test.ts), sdk/react-native, and sdk/flutter.
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class SignInGateTests
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

        /// <summary>A provider that can run script in the page, so the
        /// credential the gate answers `needCredential` with is observable.</summary>
        private class ScriptableFakeWebView : FakeWebView, ILevelMomentScriptableWebView
        {
            public int EvaluateJSCount;
            public string LastJS;

            public void EvaluateJS(string js)
            {
                EvaluateJSCount++;
                LastJS = js;
            }
        }

        private class FakeHeadlessWebView : FakeWebView, ILevelMomentHeadlessWebView
        {
            public int OpenHiddenCount;

            public void OpenHidden(string url)
            {
                OpenHiddenCount++;
                LastUrl = url;
            }
        }

        /// <summary>A third-party provider that throws out of Open()/OpenHidden().</summary>
        private class ThrowingWebView : ILevelMomentHeadlessWebView
        {
            // Required by the interface; this fake never raises either.
#pragma warning disable 0067
            public event Action<string> OnMessage;
            public event Action OnClosed;
#pragma warning restore 0067

            public int CloseCount;

            public void Open(string url)
            {
                throw new InvalidOperationException("provider blew up on Open");
            }

            public void OpenHidden(string url)
            {
                throw new InvalidOperationException("provider blew up on OpenHidden");
            }

            public void Close()
            {
                CloseCount++;
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
        private const string SignedIn = "{\"type\":\"signedIn\"}";
        private const string Dismissed = "{\"type\":\"dismissed\"}";

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
            LevelMomentAds.CheckTimeoutSeconds = 30;
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

        // ---- URL building ---------------------------------------------------

        [Test]
        public void BuildGate_CarriesModePlacementAndApiUrl_ButNeverACredential()
        {
            var url = BreakUrl.BuildGate(
                LevelMomentAds.Config, "p1", "gate");

            StringAssert.StartsWith("https://app.example.com/break?", url);
            StringAssert.Contains("mode=gate", url);
            StringAssert.Contains("placementId=p1", url);
            StringAssert.Contains("apiUrl=https%3A%2F%2Fapi.example.com", url);
            StringAssert.DoesNotContain("token=", url);
            StringAssert.DoesNotContain("format=", url);
            StringAssert.DoesNotContain("mock=", url);
        }

        // The gate reads no credential off the launch URL either. Given none,
        // it answers the page's request with an empty reply and lets the hosted
        // origin's own stored credential decide whether this device is signed
        // in — which is the only place a paired credential lives.
        [Test]
        public void EnsureSignedIn_WithoutAnExplicitToken_HandsOverNothing()
        {
            var fake = new ScriptableFakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentAds.EnsureSignedIn("p1", delegate { });
            fake.EmitMessage("{\"type\":\"needCredential\"}");

            Assert.AreEqual(1, fake.EvaluateJSCount);
            StringAssert.Contains("\\\"token\\\":\\\"\\\"", fake.LastJS);
        }

        [Test]
        public void BuildGate_CheckModeUsesModeCheck()
        {
            var url = BreakUrl.BuildGate(LevelMomentAds.Config, "p1", "check");
            StringAssert.Contains("mode=check", url);
        }

        [Test]
        public void BuildAccess_UsesAccessPathAndPreservesUnsafeOrigin()
        {
            var url = BreakUrl.BuildAccess(LevelMomentAds.Config, "p1", "check");

            StringAssert.StartsWith("https://app.example.com/access?", url);
            StringAssert.Contains("mode=check", url);
            StringAssert.Contains("sandbox=true", url);
        }

        [Test]
        public void BuildGate_MockOmitsApiUrlAndToken()
        {
            LevelMomentAds.Config.Mock = true;
            var url = BreakUrl.BuildGate(LevelMomentAds.Config, "p1", "gate");

            StringAssert.Contains("mock=true", url);
            StringAssert.DoesNotContain("apiUrl=", url);
            StringAssert.DoesNotContain("token=", url);
        }

        [Test]
        public void BuildGate_RejectsUnsafeBreakUrlWithQuery()
        {
            LevelMomentAds.Config.UnsafeTesting.BreakUrl = "https://app.example.com/break?theme=dark";
            Assert.Throws<ArgumentException>(() =>
                BreakUrl.BuildGate(LevelMomentAds.Config, "p1", "gate"));
        }

        // ---- EnsureSignedIn — message mapping -------------------------------

        [Test]
        public void EnsureSignedIn_SignedInReportsReady()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r));

            StringAssert.Contains("mode=gate", fake.LastUrl);
            fake.EmitMessage(Ready);
            fake.EmitMessage(SignedIn);

            Assert.AreEqual(new List<EnsureSignedInResult> { EnsureSignedInResult.Ready }, results);
            Assert.AreEqual(1, fake.CloseCount);
        }

        [Test]
        public void EnsureSignedIn_DismissedReportsCanceled()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r));
            fake.EmitMessage(Dismissed);

            Assert.AreEqual(new List<EnsureSignedInResult> { EnsureSignedInResult.Canceled }, results);
        }

        [Test]
        public void EnsureAccess_UsesAccessSurfaceAndReportsReady()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureAccess("p1", r => results.Add(r));

            StringAssert.StartsWith("https://app.example.com/access?", fake.LastUrl);
            StringAssert.Contains("mode=gate", fake.LastUrl);
            fake.EmitMessage(SignedIn);

            Assert.AreEqual(new List<EnsureSignedInResult> { EnsureSignedInResult.Ready }, results);
        }

        [Test]
        public void EnsureSignedIn_ErrorReportsTechnicalFailure()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r));
            fake.EmitMessage(ErrorMsg("network_error", "offline"));

            Assert.AreEqual(
                new List<EnsureSignedInResult> { EnsureSignedInResult.TechnicalFailure },
                results);
        }

        [Test]
        public void EnsureSignedIn_NativeCloseReportsCanceled()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r));
            fake.EmitClosed();

            Assert.AreEqual(new List<EnsureSignedInResult> { EnsureSignedInResult.Canceled }, results);
        }

        // ---- Terminal-once ---------------------------------------------------

        [Test]
        public void EnsureSignedIn_ReportsOnceEvenWhenTerminalsRepeat()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r));

            fake.EmitMessage(SignedIn);
            fake.EmitMessage(Dismissed); // after terminal — ignored
            fake.EmitMessage(ErrorMsg("late", "ignored")); // ignored
            fake.EmitClosed(); // ignored

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(EnsureSignedInResult.Ready, results[0]);
            Assert.AreEqual(1, fake.CloseCount);
        }

        [Test]
        public void EnsureSignedIn_MalformedMessageIsIgnored()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r));
            fake.EmitMessage("{not valid json");

            Assert.AreEqual(0, results.Count);
        }

        // ---- Watchdog --------------------------------------------------------

        [Test]
        public void Watchdog_ReportsTechnicalFailureWhenNoReadyArrives()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            var gate = SignInGate.Open(
                "https://app.example.com/break?mode=gate",
                false,
                () => results.Add(EnsureSignedInResult.Ready),
                () => results.Add(EnsureSignedInResult.Canceled),
                _ => results.Add(EnsureSignedInResult.TechnicalFailure));

            _now = 14.999;
            gate.Tick();
            Assert.AreEqual(0, results.Count);

            _now = 15.0;
            gate.Tick();
            Assert.AreEqual(
                new List<EnsureSignedInResult> { EnsureSignedInResult.TechnicalFailure },
                results);

            _now = 30;
            gate.Tick();
            Assert.AreEqual(1, results.Count, "watchdog must not fire twice");
        }

        [Test]
        public void Watchdog_ReadyBeforeDeadlineLetsPairingTakeAsLongAsItTakes()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            var gate = SignInGate.Open(
                "https://app.example.com/break?mode=gate",
                false,
                () => results.Add(EnsureSignedInResult.Ready),
                () => results.Add(EnsureSignedInResult.Canceled),
                _ => results.Add(EnsureSignedInResult.TechnicalFailure));

            fake.EmitMessage(Ready);
            _now = 1000;
            gate.Tick();

            Assert.AreEqual(0, results.Count);
        }

        // ---- The headless check's total deadline ------------------------------

        [Test]
        public void CheckDeadline_ReportsAnErrorWhenThePageLoadsAndThenGoesQuiet()
        {
            var fake = new FakeHeadlessWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<bool>();
            string error = null;
            var gate = SignInGate.Open(
                "https://app.example.com/break?mode=check",
                true,
                () => results.Add(true),
                () => results.Add(false),
                code => error = code);

            fake.EmitMessage(Ready); // cancels the load watchdog, not the deadline

            _now = 29.999;
            gate.Tick();
            Assert.IsNull(error);

            _now = 30.0;
            gate.Tick();
            Assert.AreEqual(0, results.Count, "a stalled check is not a claim about the household");
            Assert.IsNotNull(error);
            StringAssert.Contains("check_timeout", error);
            Assert.AreEqual(1, fake.CloseCount);

            _now = 1000;
            gate.Tick();
            Assert.AreEqual(1, fake.CloseCount, "the deadline must not fire twice");
        }

        [Test]
        public void CheckDeadline_VerdictBeforeTheDeadlineCancelsIt()
        {
            var fake = new FakeHeadlessWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<bool>();
            string error = null;
            var gate = SignInGate.Open(
                "https://app.example.com/break?mode=check",
                true,
                () => results.Add(true),
                () => results.Add(false),
                code => error = code);

            fake.EmitMessage(SignedIn);

            _now = 1000;
            gate.Tick();

            Assert.AreEqual(new List<bool> { true }, results);
            Assert.IsNull(error);
        }

        [Test]
        public void EnsureSignedIn_HasNoTotalDeadline_PairingTakesAsLongAsItTakes()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            var gate = SignInGate.Open(
                "https://app.example.com/break?mode=gate",
                false,
                () => results.Add(EnsureSignedInResult.Ready),
                () => results.Add(EnsureSignedInResult.Canceled),
                _ => results.Add(EnsureSignedInResult.TechnicalFailure));

            fake.EmitMessage(Ready);
            _now = 100000;
            gate.Tick();

            Assert.AreEqual(0, results.Count, "the gate must still be waiting");

            fake.EmitMessage(SignedIn);
            Assert.AreEqual(new List<EnsureSignedInResult> { EnsureSignedInResult.Ready }, results);
        }

        // ---- Mock mode -------------------------------------------------------

        [Test]
        public void EnsureSignedIn_MockReportsReadyWithoutOpeningAnything()
        {
            LevelMomentAds.Config.Mock = true;
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r));

            Assert.AreEqual(new List<EnsureSignedInResult> { EnsureSignedInResult.Ready }, results);
            Assert.AreEqual(0, fake.OpenCount);
        }

        [Test]
        public void IsSignedIn_MockReportsTrueWithoutOpeningAnything()
        {
            LevelMomentAds.Config.Mock = true;
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<bool>();
            LevelMomentAds.IsSignedIn("p1", r => results.Add(r), _ => Assert.Fail("no error expected"));

            Assert.AreEqual(new List<bool> { true }, results);
            Assert.AreEqual(0, fake.OpenCount);
        }

        // ---- Not initialized -------------------------------------------------

        [Test]
        public void EnsureSignedIn_WithoutInitializeReportsTechnicalFailure()
        {
            LevelMomentAds.ResetForTests();

            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r));

            Assert.AreEqual(
                new List<EnsureSignedInResult> { EnsureSignedInResult.TechnicalFailure },
                results);
        }

        [Test]
        public void IsSignedIn_WithoutInitializeReportsAnError()
        {
            LevelMomentAds.ResetForTests();

            string error = null;
            LevelMomentAds.IsSignedIn(
                "p1",
                _ => Assert.Fail("no boolean answer is honest here"),
                e => error = e);

            Assert.IsNotNull(error);
        }

        // ---- IsSignedIn — the headless check ---------------------------------

        [Test]
        public void IsSignedIn_UsesTheHiddenOpenWhenTheProviderSupportsIt()
        {
            var fake = new FakeHeadlessWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<bool>();
            LevelMomentAds.IsSignedIn("p1", r => results.Add(r), e => Assert.Fail(e));

            Assert.AreEqual(1, fake.OpenHiddenCount);
            Assert.AreEqual(0, fake.OpenCount);
            StringAssert.Contains("mode=check", fake.LastUrl);

            fake.EmitMessage(SignedIn);
            Assert.AreEqual(new List<bool> { true }, results);
        }

        [Test]
        public void IsSignedIn_FallsBackToTheVisibleOpenForOlderProviders()
        {
            var fake = new FakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentAds.IsSignedIn("p1", _ => { }, e => Assert.Fail(e));

            Assert.AreEqual(1, fake.OpenCount);
            StringAssert.Contains("mode=check", fake.LastUrl);
        }

        [Test]
        public void IsSignedIn_DismissedIsFalse()
        {
            var fake = new FakeHeadlessWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<bool>();
            LevelMomentAds.IsSignedIn("p1", r => results.Add(r), e => Assert.Fail(e));
            fake.EmitMessage(Dismissed);

            Assert.AreEqual(new List<bool> { false }, results);
        }

        [Test]
        public void CheckAccess_DismissedIsFalse()
        {
            var fake = new FakeHeadlessWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<bool>();
            string error = null;
            LevelMomentAds.CheckAccess("p1", r => results.Add(r), e => error = e);

            Assert.IsNull(error);
            StringAssert.StartsWith("https://app.example.com/access?", fake.LastUrl);
            StringAssert.Contains("mode=check", fake.LastUrl);
            fake.EmitMessage(Dismissed);

            Assert.AreEqual(new List<bool> { false }, results);
        }

        [Test]
        public void CheckAccess_TimeoutReportsTechnicalError()
        {
            var fake = new FakeHeadlessWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<bool>();
            string error = null;
            var gate = SignInGate.Open(
                BreakUrl.BuildAccess(LevelMomentAds.Config, "p1", "check"),
                true,
                () => results.Add(true),
                () => results.Add(false),
                code => error = code);

            fake.EmitMessage(Ready);
            _now = 30.0;
            gate.Tick();

            Assert.IsEmpty(results);
            StringAssert.Contains("check_timeout", error);
        }

        [Test]
        public void IsSignedIn_ErrorReportsAnErrorRatherThanFalse()
        {
            var fake = new FakeHeadlessWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<bool>();
            string error = null;
            LevelMomentAds.IsSignedIn("p1", r => results.Add(r), e => error = e);
            fake.EmitMessage(ErrorMsg("network_error", "offline"));

            Assert.AreEqual(0, results.Count, "a network failure is not a claim about the household");
            Assert.IsNotNull(error);
            StringAssert.Contains("network_error", error);
        }

        [Test]
        public void IsSignedIn_NoProviderReportsAnError()
        {
            // No provider registered → NoWebViewFallback posts its error.
            var results = new List<bool>();
            string error = null;
            LevelMomentAds.IsSignedIn("p1", r => results.Add(r), e => error = e);

            Assert.AreEqual(0, results.Count);
            Assert.IsNotNull(error);
            StringAssert.Contains("no_webview_provider", error);
        }

        [Test]
        public void EnsureSignedIn_NoProviderReportsTechnicalFailure()
        {
            var results = new List<EnsureSignedInResult>();
            LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r));

            Assert.AreEqual(
                new List<EnsureSignedInResult> { EnsureSignedInResult.TechnicalFailure },
                results);
        }

        // ---- A provider that throws on startup -------------------------------

        [Test]
        public void EnsureSignedIn_ProviderThrowingOnOpenReportsTechnicalFailure()
        {
            var fake = new ThrowingWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            Assert.DoesNotThrow(() => LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r)));

            Assert.AreEqual(
                new List<EnsureSignedInResult> { EnsureSignedInResult.TechnicalFailure },
                results);
            Assert.AreEqual(1, fake.CloseCount, "the gate must tear its WebView down");
        }

        [Test]
        public void IsSignedIn_ProviderThrowingOnOpenHiddenReportsAnError()
        {
            var fake = new ThrowingWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<bool>();
            string error = null;
            Assert.DoesNotThrow(() =>
                LevelMomentAds.IsSignedIn("p1", r => results.Add(r), e => error = e));

            Assert.AreEqual(0, results.Count, "a broken provider is not a claim about the household");
            Assert.IsNotNull(error);
            StringAssert.Contains("webview_error", error);
            Assert.AreEqual(1, fake.CloseCount);
        }

        [Test]
        public void ProviderThrowingOnOpen_LeavesNoTickingGateBehind()
        {
            var fake = new ThrowingWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            var gate = SignInGate.Open(
                "https://app.example.com/break?mode=gate",
                false,
                () => results.Add(EnsureSignedInResult.Ready),
                () => results.Add(EnsureSignedInResult.Canceled),
                _ => results.Add(EnsureSignedInResult.TechnicalFailure));

            Assert.AreEqual(1, results.Count);

            // The watchdog was cancelled with the rest of the teardown, so a
            // later tick cannot report a second result.
            _now = 1000;
            gate.Tick();
            Assert.AreEqual(1, results.Count);
        }

        // Unlike RewardedAd.Show(), the gate's startup catch cannot mislabel a
        // game's own exception. Terminate() runs its guard and teardown BEFORE
        // firing, so an exception unwinding from the game's callback finds a
        // gate that is already terminal and the second Terminate is a no-op —
        // no second, contradictory result reaches the game. The exception is
        // swallowed, which is what `EnsureSignedIn never throws` promises.
        [Test]
        public void EnsureSignedIn_ThrowingCallbackDoesNotProduceASecondResult()
        {
            var fake = new SyncMessageWebView(SignedIn);
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<EnsureSignedInResult>();
            Assert.DoesNotThrow(() => LevelMomentAds.EnsureSignedIn("p1", r =>
            {
                results.Add(r);
                throw new InvalidOperationException("game callback blew up");
            }));

            Assert.AreEqual(new List<EnsureSignedInResult> { EnsureSignedInResult.Ready }, results);
        }

        [Test]
        public void IsSignedIn_ThrowingCallbackDoesNotBecomeAnError()
        {
            var fake = new SyncMessageWebView(SignedIn);
            LevelMomentWebViewRegistry.Register(() => fake);

            var results = new List<bool>();
            string error = null;
            Assert.DoesNotThrow(() => LevelMomentAds.IsSignedIn(
                "p1",
                r =>
                {
                    results.Add(r);
                    throw new InvalidOperationException("game callback blew up");
                },
                e => error = e));

            Assert.AreEqual(new List<bool> { true }, results);
            Assert.IsNull(error, "the game must not hear a second, contradictory result");
        }

        [Test]
        public void ProviderThrowingFromTheFactoryReportsTechnicalFailure()
        {
            LevelMomentWebViewRegistry.Register(
                () => { throw new InvalidOperationException("factory blew up"); });

            var results = new List<EnsureSignedInResult>();
            Assert.DoesNotThrow(() => LevelMomentAds.EnsureSignedIn("p1", r => results.Add(r)));

            Assert.AreEqual(
                new List<EnsureSignedInResult> { EnsureSignedInResult.TechnicalFailure },
                results);
        }
    }
}
