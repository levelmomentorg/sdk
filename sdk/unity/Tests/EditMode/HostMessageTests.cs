// EditMode tests for HostMessage.TryParse — parsing the hosted page's bridge
// messages. Pure C# (JsonUtility only), no WebView. Mirrors the parsing tests
// behind sdk/flutter's HostMessage.tryParse and sdk/react-native's bridge.
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class HostMessageTests
    {
        // ----- The 4 valid message types -----

        [Test]
        public void TryParse_Ready()
        {
            var msg = HostMessage.TryParse("{\"type\":\"ready\"}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.Ready, msg.Type);
        }

        [Test]
        public void TryParse_EarnedReward_CorrectAmount()
        {
            var msg = HostMessage.TryParse("{\"type\":\"earnedReward\",\"payload\":{\"amount\":1}}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.EarnedReward, msg.Type);
            Assert.AreEqual(1, msg.Amount);
        }

        [Test]
        public void TryParse_EarnedReward_MapsOpaqueRewardId()
        {
            var msg = HostMessage.TryParse(
                "{\"type\":\"earnedReward\",\"payload\":{\"amount\":1,\"rewardId\":\"impression-7\"}}");

            Assert.IsNotNull(msg);
            Assert.AreEqual("impression-7", msg.RewardId);
        }

        [Test]
        public void TryParse_RejectsInvalidRewardAndProtocolVersions()
        {
            Assert.IsNull(HostMessage.TryParse(
                "{\"type\":\"earnedReward\",\"payload\":{\"amount\":2}}"));
            Assert.IsNull(HostMessage.TryParse(
                "{\"type\":\"ready\",\"protocolVersion\":2}"));
        }

        [Test]
        public void TryParse_EarnedReward_ZeroAmount()
        {
            var msg = HostMessage.TryParse("{\"type\":\"earnedReward\",\"payload\":{\"amount\":0}}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.EarnedReward, msg.Type);
            Assert.AreEqual(0, msg.Amount);
        }

        [Test]
        public void TryParse_EarnedReward_MissingPayload_IsRejected()
        {
            var msg = HostMessage.TryParse("{\"type\":\"earnedReward\"}");
            Assert.IsNull(msg);
        }

        [Test]
        public void TryParse_Dismissed()
        {
            var msg = HostMessage.TryParse("{\"type\":\"dismissed\"}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.Dismissed, msg.Type);
        }

        [Test]
        public void TryParse_Error_WithCodeAndMessage()
        {
            var msg = HostMessage.TryParse(
                "{\"type\":\"error\",\"payload\":{\"code\":\"no_fill\",\"message\":\"No question\"}}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.Error, msg.Type);
            Assert.AreEqual("no_fill", msg.Code);
            Assert.AreEqual("No question", msg.Message);
        }

        [Test]
        public void TryParse_Error_MissingPayload_UsesDefaults()
        {
            var msg = HostMessage.TryParse("{\"type\":\"error\"}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.Error, msg.Type);
            Assert.AreEqual("unknown", msg.Code);
            Assert.AreEqual("Unknown error", msg.Message);
        }

        // ----- Credential bridge messages -----
        // The page used to read its credential off the URL; now it asks for one
        // over the bridge with these three types. See CredentialBridgeTests for
        // how the SDK answers `needCredential`.

        [Test]
        public void TryParse_NeedCredential()
        {
            var msg = HostMessage.TryParse("{\"type\":\"needCredential\"}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.NeedCredential, msg.Type);
        }

        [Test]
        public void TryParse_CredentialIssued_ExtractsToken()
        {
            var msg = HostMessage.TryParse(
                "{\"type\":\"credentialIssued\",\"payload\":{\"token\":\"abc123\"}}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.CredentialIssued, msg.Type);
            Assert.AreEqual("abc123", msg.Token);
        }

        // Unity never asks for custody (no secure store), so the page never
        // actually sends this one here — but the shells share the message
        // shape, and a missing payload must not throw on the way to being
        // ignored.
        [Test]
        public void TryParse_CredentialIssued_MissingPayload_TokenIsNullNotThrown()
        {
            HostMessage msg = null;
            Assert.DoesNotThrow(() => msg = HostMessage.TryParse("{\"type\":\"credentialIssued\"}"));
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.CredentialIssued, msg.Type);
            Assert.IsTrue(string.IsNullOrEmpty(msg.Token));
        }

        [Test]
        public void TryParse_CredentialInvalid()
        {
            var msg = HostMessage.TryParse("{\"type\":\"credentialInvalid\"}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.CredentialInvalid, msg.Type);
        }

        // ----- openExternal: sending a parent to the device's browser --------

        [Test]
        public void TryParse_OpenExternal_CarriesTheApprovalUrl()
        {
            var msg = HostMessage.TryParse(
                "{\"type\":\"openExternal\",\"payload\":{\"url\":\"https://app.levelmoment.com/link?code=AB12CD\"}}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.OpenExternal, msg.Type);
            Assert.AreEqual("https://app.levelmoment.com/link?code=AB12CD", msg.Url);
        }

        [Test]
        public void TryParse_OpenExternal_WithoutUrl_ReturnsNull()
        {
            Assert.IsNull(HostMessage.TryParse("{\"type\":\"openExternal\"}"));
            Assert.IsNull(
                HostMessage.TryParse("{\"type\":\"openExternal\",\"payload\":{\"url\":\"\"}}"));
        }

        private const string Hosted = "https://app.levelmoment.com/break?placementId=game-42";

        [Test]
        public void LaunchableUrl_AllowsTheHostedOrigin()
        {
            Assert.AreEqual(
                "https://app.levelmoment.com/link?code=AB12CD",
                HostMessage.LaunchableUrl("https://app.levelmoment.com/link?code=AB12CD", Hosted));
            // The default port is the same origin spelled out.
            Assert.AreEqual(
                "https://app.levelmoment.com:443/link",
                HostMessage.LaunchableUrl("https://app.levelmoment.com:443/link", Hosted));
        }

        // The bridge belongs to whatever the WebView is showing. A page that
        // took it somewhere else could otherwise full-screen a sign-in prompt
        // in the device's own browser.
        [Test]
        public void LaunchableUrl_RefusesEveryOtherOrigin()
        {
            Assert.IsNull(HostMessage.LaunchableUrl("https://evil.example.com/link", Hosted));
            Assert.IsNull(
                HostMessage.LaunchableUrl("https://app.levelmoment.com.evil.example/link", Hosted));
            Assert.IsNull(HostMessage.LaunchableUrl("https://levelmoment.com/link", Hosted));
            Assert.IsNull(HostMessage.LaunchableUrl("https://app.levelmoment.com:8443/link", Hosted));
            // Right host, wrong scheme.
            Assert.IsNull(HostMessage.LaunchableUrl("http://app.levelmoment.com/link", Hosted));
            // Custom schemes never reach the OS.
            Assert.IsNull(HostMessage.LaunchableUrl("javascript:alert(1)", Hosted));
            Assert.IsNull(HostMessage.LaunchableUrl("someapp://pay?amount=100", Hosted));
            Assert.IsNull(HostMessage.LaunchableUrl("file:///etc/passwd", Hosted));
            Assert.IsNull(HostMessage.LaunchableUrl("not a url", Hosted));
            Assert.IsNull(HostMessage.LaunchableUrl("", Hosted));
            Assert.IsNull(HostMessage.LaunchableUrl(null, Hosted));
            Assert.IsNull(HostMessage.LaunchableUrl("https://app.levelmoment.com/link", null));
        }

        // http passes only when the game is configured against an http
        // BreakUrl, which is local development.
        [Test]
        public void LaunchableUrl_AllowsHttpOnlyWhenTheHostedPageIsHttp()
        {
            const string localHosted = "http://localhost:3000/break";
            Assert.AreEqual(
                "http://localhost:3000/link",
                HostMessage.LaunchableUrl("http://localhost:3000/link", localHosted));
            Assert.IsNull(HostMessage.LaunchableUrl("http://localhost:4000/link", localHosted));
        }

        // ----- Malformed / unknown → null (silently dropped by the caller) -----

        [Test]
        public void TryParse_Malformed_ReturnsNull()
        {
            Assert.IsNull(HostMessage.TryParse("{not valid json"));
        }

        [Test]
        public void TryParse_Empty_ReturnsNull()
        {
            Assert.IsNull(HostMessage.TryParse(""));
        }

        [Test]
        public void TryParse_Null_ReturnsNull()
        {
            Assert.IsNull(HostMessage.TryParse(null));
        }

        [Test]
        public void TryParse_UnknownType_ReturnsNull()
        {
            Assert.IsNull(HostMessage.TryParse("{\"type\":\"somethingElse\"}"));
        }

        [Test]
        public void TryParse_NoType_ReturnsNull()
        {
            Assert.IsNull(HostMessage.TryParse("{\"payload\":{\"amount\":1}}"));
        }

        [Test]
        public void TryParse_JsonArray_ReturnsNull()
        {
            Assert.IsNull(HostMessage.TryParse("[1,2,3]"));
        }
    }
}
