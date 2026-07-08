// EditMode tests for UrlSafety — the SDK's defence against student
// session tokens regressing into URL query strings. The denylist here
// is intentionally narrower than the API's SSRF guard
// (platform/api/src/lib/url-safety.ts), which checks hostnames + IP
// ranges; this SDK-side guard only blocks credential-shaped query
// keys before any URL leaves the device.
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class UrlSafetyTests
    {
        // ----- Happy path: well-formed URLs with safe query keys -----

        [Test]
        public void BuildUrl_NoQuery_ReturnsBasePlusPath()
        {
            var url = UrlSafety.BuildUrl("https://api.example.com", "/questions");
            Assert.AreEqual("https://api.example.com/questions", url);
        }

        [Test]
        public void BuildUrl_TrailingSlashOnBase_IsCollapsed()
        {
            var url = UrlSafety.BuildUrl("https://api.example.com/", "/questions");
            Assert.AreEqual("https://api.example.com/questions", url);
        }

        [Test]
        public void BuildUrl_PathWithoutLeadingSlash_AddsOne()
        {
            var url = UrlSafety.BuildUrl("https://api.example.com", "questions");
            Assert.AreEqual("https://api.example.com/questions", url);
        }

        [Test]
        public void BuildUrl_SafeQueryKeys_AreEscapedAndAppended()
        {
            var url = UrlSafety.BuildUrl(
                "https://api.example.com",
                "/questions",
                new Dictionary<string, string> { { "placementId", "place 1" } });
            Assert.AreEqual("https://api.example.com/questions?placementId=place%201", url);
        }

        [Test]
        public void BuildUrl_MultipleSafeKeys_JoinsWithAmpersand()
        {
            var url = UrlSafety.BuildUrl(
                "https://api.example.com",
                "/questions",
                new Dictionary<string, string>
                {
                    { "placementId", "p1" },
                    { "subject", "math" },
                });
            // Dictionary ordering in .NET is insertion-stable on the Mono and
            // CoreCLR runtimes Unity ships with, so we can assert exact order.
            Assert.AreEqual(
                "https://api.example.com/questions?placementId=p1&subject=math",
                url);
        }

        // ----- Denylist: any credential-shaped key must be rejected -----

        [TestCase("token")]
        [TestCase("Token")]
        [TestCase("TOKEN")]
        [TestCase("studentToken")]
        [TestCase("auth_token")]
        [TestCase("authorization")]
        [TestCase("Authorization")]
        [TestCase("bearer")]
        [TestCase("Bearer-token")]
        [TestCase("secret")]
        [TestCase("apiSecret")]
        [TestCase("password")]
        [TestCase("apikey")]
        [TestCase("ApiKey")]
        [TestCase("api_key")]
        public void BuildUrl_CredentialShapedKey_Throws(string forbiddenKey)
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                UrlSafety.BuildUrl(
                    "https://api.example.com",
                    "/questions",
                    new Dictionary<string, string> { { forbiddenKey, "x" } }));
            StringAssert.Contains(forbiddenKey, ex.Message);
        }

        // ----- Empty-key edge case -----

        [Test]
        public void BuildUrl_EmptyQueryKey_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                UrlSafety.BuildUrl(
                    "https://api.example.com",
                    "/questions",
                    new Dictionary<string, string> { { "", "value" } }));
        }

        // ----- Input validation on baseUrl / path -----

        [Test]
        public void BuildUrl_NullBaseUrl_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                UrlSafety.BuildUrl(null, "/x"));
        }

        [Test]
        public void BuildUrl_EmptyBaseUrl_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                UrlSafety.BuildUrl("", "/x"));
        }

        [Test]
        public void BuildUrl_NullPath_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                UrlSafety.BuildUrl("https://api.example.com", null));
        }

        [Test]
        public void BuildUrl_EmptyPath_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                UrlSafety.BuildUrl("https://api.example.com", ""));
        }
    }
}
