// EditMode tests for MacWebViewEntry — the pure half of the native macOS
// WebView provider: which origin the native host fences navigation to, and how
// a drained native queue entry becomes the bridge JSON the SDK parses.
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class MacWebViewEntryTests
    {
        [Test]
        public void OriginOf_KeepsSchemeHostAndPort()
        {
            Assert.AreEqual(
                "http://localhost:3000",
                MacWebViewEntry.OriginOf("http://localhost:3000/break?placementId=pl_1"));
            Assert.AreEqual(
                "https://app.levelmoment.com",
                MacWebViewEntry.OriginOf("https://app.levelmoment.com/break"));
        }

        [Test]
        public void OriginOf_RefusesNonHttpAndRelativeUrls()
        {
            Assert.IsNull(MacWebViewEntry.OriginOf("file:///tmp/break.html"));
            Assert.IsNull(MacWebViewEntry.OriginOf("javascript:alert(1)"));
            Assert.IsNull(MacWebViewEntry.OriginOf("/break"));
            Assert.IsNull(MacWebViewEntry.OriginOf(null));
        }

        [Test]
        public void ToBridgeMessage_PassesPageMessagesThroughUnchanged()
        {
            Assert.AreEqual(
                "{\"type\":\"ready\"}",
                MacWebViewEntry.ToBridgeMessage("M{\"type\":\"ready\"}"));
        }

        [Test]
        public void ToBridgeMessage_WrapsNativeFailuresAsParseableErrors()
        {
            var raw = MacWebViewEntry.ToBridgeMessage("EThe \"server\" hung up\nearly");
            var msg = HostMessage.TryParse(raw);

            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.Error, msg.Type);
            Assert.AreEqual("webview_error", msg.Code);
            Assert.AreEqual("The \"server\" hung up\nearly", msg.Message);
        }

        [Test]
        public void ToBridgeMessage_IgnoresEmptyAndUnknownEntries()
        {
            Assert.IsNull(MacWebViewEntry.ToBridgeMessage(null));
            Assert.IsNull(MacWebViewEntry.ToBridgeMessage(""));
            Assert.IsNull(MacWebViewEntry.ToBridgeMessage("X{}"));
        }
    }
}
