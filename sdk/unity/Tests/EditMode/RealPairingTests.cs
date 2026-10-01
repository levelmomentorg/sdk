// EditMode tests for UnsafeTesting.RealPairing: the loopback URL rule, the
// resolved mode and what each URL builder sends under it, the refusals, the
// development-build gate, and the origin the credential reply is bound to.
// See docs/decisions/sdk-real-pairing-testing-2026-10-01.md.
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using System;
using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class RealPairingTests
    {
        // Shared vectors: sdk/core (realPairing.test.ts) and the Flutter SDK
        // pin the same table. Keep the three in step.
        private static readonly string[] Allowed =
        {
            "http://localhost:3000/break",
            "http://127.0.0.1:3000/break",
            "http://LOCALHOST:3000/break",
        };

        private static readonly string[] Refused =
        {
            "https://levelmoment.com/break",
            "https://dev.levelmoment.com/break",
            "http://localhost/break",
            "http://localhost:80/break",
            "https://localhost:3000/break",
            "http://localhost.:3000/break",
            "http://127.1:3000/break",
            "http://[::1]:3000/break",
            "http://10.0.2.2:3000/break",
            "http://192.168.1.20:3000/break",
            "http://user@localhost:3000/break",
            "http://localhost:3000/break?x=1",
            "http://localhost:3000/break#x",
            "http:\\\\localhost:3000/break",
            "http://local\thost:3000/break",
            "http://localhost.evil.example:3000/break",
            "capacitor://localhost/break",
        };

        private const string LocalBreak = "http://localhost:3000/break";

        // ---- Fakes ----------------------------------------------------------

        private class ScriptableFakeWebView : ILevelMomentScriptableWebView
        {
            public event Action<string> OnMessage;
#pragma warning disable 0067 // never raised: these tests do not close the view natively
            public event Action OnClosed;
#pragma warning restore 0067

            public string LastUrl;
            public string LastJS;

            public void Open(string url)
            {
                LastUrl = url;
            }

            public void Close() { }

            public void EvaluateJS(string js)
            {
                LastJS = js;
            }

            public void EmitMessage(string raw)
            {
                if (OnMessage != null)
                    OnMessage(raw);
            }
        }

        private static LevelMomentConfig RealPairing(string breakUrl = LocalBreak)
        {
            return new LevelMomentConfig
            {
                UnsafeTesting = new UnsafeTesting { RealPairing = true, BreakUrl = breakUrl },
            };
        }

        private static ResolvedHostedConfig ResolveDev(LevelMomentConfig config)
        {
            return config.Resolve(true);
        }

        // ---- Fixture --------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            LevelMomentAds.ResetForTests();
            LevelMomentWebViewRegistry.Reset();
            RewardedAd.SkipRuntimeDriver = true;
            LevelMomentRuntime.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            LevelMomentWebViewRegistry.Reset();
            RewardedAd.SkipRuntimeDriver = false;
            LevelMomentRuntime.ResetForTests();
            LevelMomentAds.ResetForTests();
        }

        // ---- The URL vectors ------------------------------------------------

        [TestCaseSource(nameof(Allowed))]
        public void Vector_Allowed(string url)
        {
            Assert.IsTrue(RealPairingUrl.IsAllowed(url), url);
        }

        [TestCaseSource(nameof(Refused))]
        public void Vector_Refused(string url)
        {
            Assert.IsFalse(RealPairingUrl.IsAllowed(url), url);
        }

        [TestCaseSource(nameof(Refused))]
        public void Resolve_RefusesTheBreakUrl(string url)
        {
            var ex = Assert.Throws<ArgumentException>(() => ResolveDev(RealPairing(url)));
            StringAssert.Contains("RealPairing", ex.Message);
        }

        [Test]
        public void Vector_RefusesNonAsciiAndAnEmptyQueryOrFragment()
        {
            Assert.IsFalse(RealPairingUrl.IsAllowed("http://localhost:3000/bréak"));
            Assert.IsFalse(RealPairingUrl.IsAllowed("http://localhost:3000/break?"));
            Assert.IsFalse(RealPairingUrl.IsAllowed("http://localhost:3000/break#"));
            Assert.IsFalse(RealPairingUrl.IsAllowed("http://localhost:3000/break "));
            Assert.IsFalse(RealPairingUrl.IsAllowed(null));
            Assert.IsFalse(RealPairingUrl.IsAllowed(""));
        }

        // ---- Resolution -----------------------------------------------------

        [Test]
        public void Resolve_RealPairingCarriesNoApiUrlAndNoToken()
        {
            var resolved = ResolveDev(RealPairing());

            Assert.AreEqual(HostedMode.RealPairing, resolved.Mode);
            Assert.AreEqual(LocalBreak, resolved.BreakUrl);
            Assert.IsNull(resolved.ApiUrl);
            Assert.IsNull(resolved.Token);
            Assert.IsFalse(resolved.Mock);
        }

        [Test]
        public void Resolve_LoadsTheReserialisedUrlNotTheCallersString()
        {
            Assert.AreEqual(
                LocalBreak,
                ResolveDev(RealPairing("http://LOCALHOST:3000/break")).BreakUrl);
        }

        [Test]
        public void Resolve_WarnsOncePerOrigin()
        {
            var config = RealPairing("http://127.0.0.1:4100/break");

            ResolveDev(config);
            ResolveDev(config);
            Assert.AreEqual(1, RealPairingUrl.WarnedCount);

            ResolveDev(RealPairing());
            Assert.AreEqual(2, RealPairingUrl.WarnedCount);
        }

        [Test]
        public void Resolve_KeepsProductionAndSandboxModes()
        {
            Assert.AreEqual(HostedMode.Production, new LevelMomentConfig().Resolve(false).Mode);
            var sandbox = new LevelMomentConfig
            {
                UnsafeTesting = new UnsafeTesting { BreakUrl = "https://localhost:3000/break" },
            };
            Assert.AreEqual(HostedMode.Sandbox, sandbox.Resolve(false).Mode);
        }

        // ---- The development-build gate ------------------------------------

        [Test]
        public void Resolve_RefusesRealPairingOutsideADevelopmentBuild()
        {
            var ex = Assert.Throws<ArgumentException>(() => RealPairing().Resolve(false));
            StringAssert.Contains("development build", ex.Message);
        }

        [Test]
        public void Initialize_RefusesRealPairingInAReleaseBuild()
        {
            LevelMomentConfig.DevelopmentBuildOverrideForTests = false;

            Assert.Throws<ArgumentException>(() => LevelMomentAds.Initialize(RealPairing()));
            Assert.IsFalse(LevelMomentAds.IsInitialized);
        }

#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
        // Outside the editor this assembly is compiled as a release build
        // (the .NET harness defines neither symbol), so the compile-time gate
        // itself is what refuses here, with no override set.
        [Test]
        public void Initialize_RefusesRealPairingWithoutTheDevelopmentSymbols()
        {
            Assert.Throws<ArgumentException>(() => LevelMomentAds.Initialize(RealPairing()));
        }
#endif

        // ---- Refusals -------------------------------------------------------

        [Test]
        public void Resolve_RefusesAToken()
        {
            var config = RealPairing();
            config.UnsafeTesting.Token = "eply_sbx_test";
            StringAssert.Contains("token", Assert.Throws<ArgumentException>(() => ResolveDev(config)).Message);
        }

        [Test]
        public void Resolve_RefusesAnApiUrl()
        {
            var config = RealPairing();
            config.UnsafeTesting.ApiUrl = "http://localhost:8080";
            StringAssert.Contains("ApiUrl", Assert.Throws<ArgumentException>(() => ResolveDev(config)).Message);
        }

#pragma warning disable 0618 // the deprecated top-level fields are set to prove they are refused
        [Test]
        public void Resolve_RefusesATopLevelApiUrl()
        {
            var config = RealPairing();
            config.ApiUrl = "https://levelmoment.com/api";
            Assert.Throws<ArgumentException>(() => ResolveDev(config));
        }

        [Test]
        public void Resolve_RefusesATopLevelBreakUrl()
        {
            var config = RealPairing();
            config.BreakUrl = LocalBreak;
            Assert.Throws<ArgumentException>(() => ResolveDev(config));
        }
#pragma warning restore 0618

        [Test]
        public void Resolve_RefusesMock()
        {
            var config = RealPairing();
            config.Mock = true;
            StringAssert.Contains("Mock", Assert.Throws<ArgumentException>(() => ResolveDev(config)).Message);
        }

        [Test]
        public void Resolve_RefusesAMissingBreakUrl()
        {
            StringAssert.Contains(
                "BreakUrl",
                Assert.Throws<ArgumentException>(() => ResolveDev(RealPairing(null))).Message);
        }

        // ---- What the URL builders send ------------------------------------

        [Test]
        public void Build_AddsSandboxOnlyInTheSandboxMode()
        {
            var production = BreakUrl.Build(new LevelMomentConfig().Resolve(false), "p1", "quick_question");
            var sandbox = BreakUrl.Build(new LevelMomentConfig
            {
                UnsafeTesting = new UnsafeTesting { BreakUrl = "http://localhost:3000/break" },
            }.Resolve(false), "p1", "quick_question");
            var realPairing = BreakUrl.Build(ResolveDev(RealPairing()), "p1", "quick_question");

            StringAssert.DoesNotContain("sandbox=", production);
            StringAssert.Contains("sandbox=true", sandbox);
            StringAssert.DoesNotContain("sandbox=", realPairing);
        }

        [Test]
        public void Build_UnderRealPairingSendsNoApiUrl()
        {
            var url = BreakUrl.Build(ResolveDev(RealPairing()), "p1", "quick_question");

            StringAssert.StartsWith(LocalBreak + "?placementId=p1", url);
            StringAssert.DoesNotContain("apiUrl=", url);
            StringAssert.DoesNotContain("token=", url);
            StringAssert.DoesNotContain("mock=", url);
        }

        [Test]
        public void BuildGate_UnderRealPairingSendsNoApiUrlOrSandbox()
        {
            var url = BreakUrl.BuildGate(ResolveDev(RealPairing()), "p1", "gate");

            StringAssert.StartsWith(LocalBreak + "?mode=gate", url);
            StringAssert.DoesNotContain("apiUrl=", url);
            StringAssert.DoesNotContain("sandbox=", url);
        }

        [Test]
        public void BuildAccess_UnderRealPairingStaysOnTheLocalOriginWithNoApiUrl()
        {
            var url = BreakUrl.BuildAccess(ResolveDev(RealPairing()), "p1", "check");

            StringAssert.StartsWith("http://localhost:3000/access?mode=check", url);
            StringAssert.DoesNotContain("apiUrl=", url);
            StringAssert.DoesNotContain("sandbox=", url);
        }

        // ---- Per-call tokens ------------------------------------------------

        private static void InitializeRealPairing()
        {
            LevelMomentConfig.DevelopmentBuildOverrideForTests = true;
            LevelMomentAds.Initialize(RealPairing());
        }

        [Test]
        public void RewardedAdLoad_RefusesAPerCallToken()
        {
            InitializeRealPairing();
            LevelMomentAdError error = null;
            RewardedAd.Load("p1", "eply_sbx_test", new RewardedAdLoadCallbacks
            {
                OnAdLoaded = ad => Assert.Fail("loaded with a token under real pairing"),
                OnAdFailedToLoad = err => error = err,
            });

            Assert.IsNotNull(error);
            Assert.AreEqual("invalid_request", error.Code);
            StringAssert.Contains("RealPairing", error.Message);
        }

        [Test]
        public void InterstitialAdLoad_RefusesAPerCallToken()
        {
            InitializeRealPairing();
            LevelMomentAdError error = null;
            InterstitialAd.Load("p1", "eply_sbx_test", new InterstitialAdLoadCallbacks
            {
                OnAdLoaded = ad => Assert.Fail("loaded with a token under real pairing"),
                OnAdFailedToLoad = err => error = err,
            });

            Assert.IsNotNull(error);
            Assert.AreEqual("invalid_request", error.Code);
        }

        [Test]
        public void Gates_RefuseAPerCallToken()
        {
            InitializeRealPairing();
            var results = new System.Collections.Generic.List<EnsureSignedInResult>();
            var errors = 0;

            LevelMomentAds.EnsureSignedIn("p1", results.Add, "eply_sbx_test");
            LevelMomentAds.EnsureAccess("p1", results.Add, "eply_sbx_test");
            LevelMomentAds.IsSignedIn("p1", ok => Assert.Fail("checked with a token"), err => errors++, "eply_sbx_test");
            LevelMomentAds.CheckAccess("p1", ok => Assert.Fail("checked with a token"), err => errors++, "eply_sbx_test");

            CollectionAssert.AreEqual(
                new[] { EnsureSignedInResult.TechnicalFailure, EnsureSignedInResult.TechnicalFailure },
                results);
            Assert.AreEqual(2, errors);
        }

        [Test]
        public void ResolveStudentToken_WithoutATokenAnswersNothing()
        {
            InitializeRealPairing();
            Assert.IsNull(LevelMomentAds.ResolveStudentToken(null));
            Assert.Throws<ArgumentException>(() => LevelMomentAds.ResolveStudentToken("eply_sbx_test"));
        }

        // ---- The credential reply answers only the local page ---------------

        [Test]
        public void NeedCredential_IsAnsweredOnlyToTheResolvedLocalOrigin()
        {
            InitializeRealPairing();
            var fake = new ScriptableFakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);

            LevelMomentAds.EnsureSignedIn("p1", delegate { });
            fake.EmitMessage("{\"type\":\"needCredential\"}");

            StringAssert.StartsWith(LocalBreak + "?mode=gate", fake.LastUrl);
            StringAssert.Contains("window.location.origin === \"http://localhost:3000\"", fake.LastJS);
            StringAssert.DoesNotContain("levelmoment.com\")", fake.LastJS);
            StringAssert.Contains("\\\"token\\\":\\\"\\\"", fake.LastJS);
            StringAssert.Contains("\\\"custody\\\":false", fake.LastJS);
        }

        [Test]
        public void RewardedAdShow_LoadsTheLocalPageAndAnswersOnlyItsOrigin()
        {
            InitializeRealPairing();
            var fake = new ScriptableFakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            RewardedAd loaded = null;
            RewardedAd.Load("p1", new RewardedAdLoadCallbacks { OnAdLoaded = ad => loaded = ad });

            loaded.Show(new RewardedAdShowCallbacks());
            fake.EmitMessage("{\"type\":\"needCredential\"}");

            StringAssert.StartsWith(LocalBreak + "?placementId=p1", fake.LastUrl);
            StringAssert.DoesNotContain("apiUrl=", fake.LastUrl);
            StringAssert.DoesNotContain("sandbox=", fake.LastUrl);
            StringAssert.Contains("window.location.origin === \"http://localhost:3000\"", fake.LastJS);
            StringAssert.Contains("\\\"token\\\":\\\"\\\"", fake.LastJS);
        }

        // A token is chosen when the page asks, not at Load: a sandbox token
        // kept from a Load before the game re-initialized for real pairing
        // must not reach the local page.
        [Test]
        public void ReinitializingForRealPairingAfterLoad_DropsTheSandboxToken()
        {
            LevelMomentAds.Initialize(new LevelMomentConfig
            {
                UnsafeTesting = new UnsafeTesting
                {
                    BreakUrl = "https://app.example.com/break",
                    ApiUrl = "https://api.example.com",
                    Token = "eply_sbx_test",
                },
            });
            var fake = new ScriptableFakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            RewardedAd loaded = null;
            RewardedAd.Load("p1", new RewardedAdLoadCallbacks { OnAdLoaded = ad => loaded = ad });
            Assert.IsNotNull(loaded);

            InitializeRealPairing();
            loaded.Show(new RewardedAdShowCallbacks());
            fake.EmitMessage("{\"type\":\"needCredential\"}");

            StringAssert.StartsWith(LocalBreak + "?", fake.LastUrl);
            StringAssert.Contains("\\\"token\\\":\\\"\\\"", fake.LastJS);
            StringAssert.DoesNotContain("eply_sbx_test", fake.LastJS);
        }

        [Test]
        public void ReinitializingForProductionAfterLoad_DropsTheSandboxToken()
        {
            LevelMomentAds.Initialize(new LevelMomentConfig
            {
                UnsafeTesting = new UnsafeTesting
                {
                    BreakUrl = "https://app.example.com/break",
                    ApiUrl = "https://api.example.com",
                    Token = "eply_sbx_test",
                },
            });
            var fake = new ScriptableFakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            RewardedAd loaded = null;
            RewardedAd.Load("p1", new RewardedAdLoadCallbacks { OnAdLoaded = ad => loaded = ad });
            Assert.IsNotNull(loaded);

            LevelMomentAds.Initialize(new LevelMomentConfig());
            loaded.Show(new RewardedAdShowCallbacks());
            fake.EmitMessage("{\"type\":\"needCredential\"}");

            StringAssert.StartsWith("https://levelmoment.com/break?", fake.LastUrl);
            StringAssert.Contains("\\\"token\\\":\\\"\\\"", fake.LastJS);
            StringAssert.DoesNotContain("eply_sbx_test", fake.LastJS);
        }

        [Test]
        public void CredentialBridge_RequiresAnOrigin()
        {
            Assert.Throws<ArgumentException>(() => CredentialBridge.BuildInjection(null, "tok"));
            Assert.Throws<ArgumentException>(() => CredentialBridge.BuildInjection("", "tok"));

            var fake = new ScriptableFakeWebView();
            CredentialBridge.Deliver(fake, null, "tok");
            Assert.IsNull(fake.LastJS);
        }

        // ---- The one origin helper -----------------------------------------

        [Test]
        public void HostedOrigin_DropsTheDefaultPortAndKeepsAnExplicitOne()
        {
            Assert.AreEqual("https://levelmoment.com", HostedOrigin.Of("https://levelmoment.com:443/break"));
            Assert.AreEqual("http://localhost:3000", HostedOrigin.Of("http://LOCALHOST:3000/break?x=1"));
        }

        [Test]
        public void HostedOrigin_RefusesUserinfo()
        {
            Assert.IsNull(HostedOrigin.Of("https://user@levelmoment.com/break"));
        }
    }
}
