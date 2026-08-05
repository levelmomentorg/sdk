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
            };
        }

        // ----- Live mode: placementId + format + apiUrl + token -----

        [Test]
        public void Build_Live_IncludesAllParamsEncoded()
        {
            var url = BreakUrl.Build(Live(), "place 1", "quiz", "tok 1");

            StringAssert.Contains("placementId=place%201", url);
            StringAssert.Contains("format=quiz", url);
            StringAssert.Contains("apiUrl=https%3A%2F%2Fapi.example.com", url);
            StringAssert.Contains("token=tok%201", url);
            StringAssert.DoesNotContain("mock=true", url);
        }

        [Test]
        public void Build_Live_PlacementIdIsFirstParam()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard", "tok");
            StringAssert.Contains("/break?placementId=p1", url);
        }

        [Test]
        public void Build_Live_MissingToken_OmitsTokenParam()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard", null);
            StringAssert.Contains("apiUrl=https%3A%2F%2Fapi.example.com", url);
            StringAssert.DoesNotContain("token=", url);
        }

        // ----- Mock mode: mock=true, no apiUrl/token -----

        [Test]
        public void Build_Mock_UsesMockFlagAndOmitsApiUrlAndToken()
        {
            var config = new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break",
                Mock = true,
            };

            var url = BreakUrl.Build(config, "p1", "flashcard", "tok");

            StringAssert.Contains("mock=true", url);
            StringAssert.DoesNotContain("apiUrl=", url);
            StringAssert.DoesNotContain("token=", url);
        }

        // ----- SSV custom data -----

        [Test]
        public void Build_WithCustomData_AppendsEncodedParam()
        {
            var config = new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break",
                CustomData = "order/42&x",
            };
            var url = BreakUrl.Build(config, "p1", "flashcard", "tok");
            StringAssert.Contains("customData=order%2F42%26x", url);
        }

        [Test]
        public void Build_WithCustomData_RidesAlongInMockMode()
        {
            var config = new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break",
                Mock = true,
                CustomData = "u42",
            };
            var url = BreakUrl.Build(config, "p1", "flashcard", "tok");
            StringAssert.Contains("mock=true", url);
            StringAssert.Contains("customData=u42", url);
        }

        [Test]
        public void Build_WithoutCustomData_OmitsParam()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard", "tok");
            StringAssert.DoesNotContain("customData=", url);
        }

        // ----- Format defaulting -----

        [Test]
        public void Build_EmptyFormat_DefaultsToFlashcard()
        {
            var url = BreakUrl.Build(Live(), "p1", "", "tok");
            StringAssert.Contains("format=flashcard", url);
        }

        [Test]
        public void Build_NullFormat_DefaultsToFlashcard()
        {
            var url = BreakUrl.Build(Live(), "p1", null, "tok");
            StringAssert.Contains("format=flashcard", url);
        }

        // ----- Separator selection -----

        [Test]
        public void Build_BreakUrlWithoutQuery_UsesQuestionMark()
        {
            var url = BreakUrl.Build(Live(), "p1", "flashcard", "tok");
            StringAssert.Contains("/break?placementId=", url);
        }

        [Test]
        public void Build_BreakUrlWithQuery_UsesAmpersand()
        {
            var config = new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break?theme=dark",
            };
            var url = BreakUrl.Build(config, "p1", "flashcard", "tok");
            StringAssert.Contains("theme=dark&placementId=p1", url);
        }

        // ----- Input validation -----

        [Test]
        public void Build_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                BreakUrl.Build(null, "p1", "flashcard", "tok"));
        }

        [Test]
        public void Build_EmptyBreakUrl_Throws()
        {
            var config = new LevelMomentConfig { ApiUrl = "https://api.example.com", BreakUrl = "" };
            Assert.Throws<ArgumentException>(() =>
                BreakUrl.Build(config, "p1", "flashcard", "tok"));
        }

        [Test]
        public void Build_EmptyPlacementId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                BreakUrl.Build(Live(), "", "flashcard", "tok"));
        }
    }
}
