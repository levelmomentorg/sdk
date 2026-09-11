// EditMode tests for BreakUrl — the hosted /break page URL builder. Pure C#,
// no WebView. Mirrors the URL-building tests in sdk/web (client.test.ts).
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using System;
using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class BreakUrlTests
    {
        private static LevelMomentConfig Live()
        {
            return new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break",
                UnsafeTesting = new UnsafeTesting
                {
                    ApiUrl = "https://api.example.com",
                    BreakUrl = "https://app.example.com/break",
                },
            };
        }

        // ----- Live mode: placementId + format + apiUrl, and NO credential -----

        [Test]
        public void Build_Live_IncludesAllParamsEncoded()
        {
            var url = BreakUrl.Build(Live(), "place 1", "quiz");

            StringAssert.Contains("placementId=place%201", url);
            StringAssert.Contains("format=quiz", url);
            StringAssert.Contains("apiUrl=https%3A%2F%2Fapi.example.com", url);
            StringAssert.DoesNotContain("mock=true", url);
        }

        [Test]
        public void Build_Live_PlacementIdIsFirstParam()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard");
            StringAssert.Contains("/break?placementId=p1", url);
        }

        // The point of this change: no credential rides on the URL at all,
        // live or mock. The page asks for one over the bridge instead — see
        // CredentialBridgeTests.
        [Test]
        public void Build_NeverIncludesATokenParam()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard");
            StringAssert.Contains("apiUrl=https%3A%2F%2Fapi.example.com", url);
            StringAssert.DoesNotContain("token=", url);
        }

        // ----- Mock mode: mock=true, no apiUrl -----

        [Test]
        public void Build_Mock_UsesMockFlagAndOmitsApiUrlAndToken()
        {
            var config = new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break",
                Mock = true,
                UnsafeTesting = new UnsafeTesting
                {
                    ApiUrl = "https://api.example.com",
                    BreakUrl = "https://app.example.com/break",
                },
            };

            var url = BreakUrl.Build(config, "p1", "flashcard");

            StringAssert.Contains("mock=true", url);
            StringAssert.DoesNotContain("apiUrl=", url);
            StringAssert.DoesNotContain("token=", url);
        }

        // ----- SSV custom data travels in the credential handshake -----

        [Test]
        public void Build_WithCustomData_DoesNotAppendCredentialContext()
        {
            var config = new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break",
                CustomData = "order/42&x",
                UnsafeTesting = new UnsafeTesting
                {
                    ApiUrl = "https://api.example.com",
                    BreakUrl = "https://app.example.com/break",
                },
            };
            var url = BreakUrl.Build(config, "p1", "flashcard");
            StringAssert.DoesNotContain("customData=", url);
        }

        [Test]
        public void Build_WithCustomData_DoesNotRideAlongInMockMode()
        {
            var config = new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break",
                Mock = true,
                CustomData = "u42",
                UnsafeTesting = new UnsafeTesting
                {
                    ApiUrl = "https://api.example.com",
                    BreakUrl = "https://app.example.com/break",
                },
            };
            var url = BreakUrl.Build(config, "p1", "flashcard");
            StringAssert.Contains("mock=true", url);
            StringAssert.DoesNotContain("customData=", url);
        }

        [Test]
        public void Build_WithoutCustomData_OmitsParam()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard");
            StringAssert.DoesNotContain("customData=", url);
        }

        // ----- Placement kind (InterstitialAd only — RewardedAd passes none) -----

        [Test]
        public void Build_WithKind_AppendsKindParam()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard", "interstitial");
            StringAssert.Contains("kind=interstitial", url);
        }

        [Test]
        public void Build_WithoutKind_OmitsKindParam()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard");
            StringAssert.DoesNotContain("kind=", url);
        }

        [Test]
        public void Build_WithEmptyKind_OmitsKindParam()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard", "");
            StringAssert.DoesNotContain("kind=", url);
        }

        // ----- Format defaulting -----

        [Test]
        public void Build_EmptyFormat_DefaultsToFlashcard()
        {
            var url = BreakUrl.Build(Live(), "p1", "");
            StringAssert.Contains("format=flashcard", url);
        }

        [Test]
        public void Build_NullFormat_DefaultsToFlashcard()
        {
            var url = BreakUrl.Build(Live(), "p1", null);
            StringAssert.Contains("format=flashcard", url);
        }

        // ----- Separator selection -----

        [Test]
        public void Build_BreakUrlWithoutQuery_UsesQuestionMark()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard");
            StringAssert.Contains("/break?placementId=", url);
        }

        [Test]
        public void Build_RejectsUnsafeBreakUrlWithQuery()
        {
            var config = new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break?theme=dark",
                UnsafeTesting = new UnsafeTesting
                {
                    BreakUrl = "https://app.example.com/break?theme=dark",
                    ApiUrl = "https://api.example.com",
                },
            };
            Assert.Throws<ArgumentException>(() =>
                BreakUrl.Build(config, "p1", "flashcard"));
        }

        // ----- Capability announcement: an old shell that cannot open a browser
        // must not be offered the button -----

        [Test]
        public void Build_AnnouncesOpenExternalCapability()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard");
            StringAssert.Contains("caps=openExternal", url);
        }

        [Test]
        public void Build_UnsafeTesting_AnnouncesSandboxAndProtocol()
        {
            var config = new LevelMomentConfig
            {
                UnsafeTesting = new UnsafeTesting
                {
                    ApiUrl = "https://api.example.com",
                    BreakUrl = "https://app.example.com/break",
                    Token = "eply_sbx_test",
                },
            };

            var url = BreakUrl.Build(config, "p1", "flashcard");

            StringAssert.Contains("sandbox=true", url);
            StringAssert.Contains("protocolVersion=1", url);
            StringAssert.Contains("sdkVersion=0.2.0", url);
        }

        [Test]
        public void BuildGate_AnnouncesOpenExternalCapability()
        {
            var url = BreakUrl.BuildGate(Live(), "p1", "gate");
            StringAssert.Contains("caps=openExternal", url);
        }

        [Test]
        public void BuildAccess_DefaultsToCanonicalAccessSurface()
        {
            var url = BreakUrl.BuildAccess(new LevelMomentConfig(), "p1", "check");

            StringAssert.StartsWith("https://levelmoment.com/access?", url);
            StringAssert.Contains("mode=check", url);
            StringAssert.Contains("apiUrl=https%3A%2F%2Flevelmoment.com%2Fapi", url);
        }

        // ----- Input validation -----

        [Test]
        public void Build_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                BreakUrl.Build(null, "p1", "flashcard"));
        }

        [Test]
        public void Build_RejectsNoncanonicalLegacyApiUrl()
        {
            var config = new LevelMomentConfig { ApiUrl = "https://api.example.com", BreakUrl = "" };
            Assert.Throws<ArgumentException>(() =>
                BreakUrl.Build(config, "p1", "flashcard"));
        }

        [Test]
        public void Build_DefaultsToCanonicalEndpoints()
        {
            var url = BreakUrl.Build(new LevelMomentConfig(), "p1", "flashcard");

            StringAssert.StartsWith("https://levelmoment.com/break?", url);
            StringAssert.Contains("apiUrl=https%3A%2F%2Flevelmoment.com%2Fapi", url);
        }

        [Test]
        public void Build_EmptyPlacementId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                BreakUrl.Build(Live(), "", "flashcard"));
        }

        [Test]
        public void Config_UsesCanonicalDefaults()
        {
            var config = new LevelMomentConfig();

            config.Validate();

            Assert.AreEqual("https://levelmoment.com/api", config.EffectiveApiUrl);
            Assert.AreEqual("https://levelmoment.com/break", config.EffectiveBreakUrl);
        }

        [Test]
        public void Config_RejectsNoncanonicalProductionEndpoint()
        {
            var config = new LevelMomentConfig { BreakUrl = "https://example.test/break" };

            Assert.Throws<ArgumentException>(() => config.Validate());
        }

        [Test]
        public void Config_RejectsNonSandboxTestingToken()
        {
            var config = new LevelMomentConfig
            {
                UnsafeTesting = new UnsafeTesting { Token = "production-token" },
            };

            Assert.Throws<ArgumentException>(() => config.Validate());
        }
    }
}
