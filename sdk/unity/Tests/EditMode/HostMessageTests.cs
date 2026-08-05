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
        public void TryParse_EarnedReward_ZeroAmount()
        {
            var msg = HostMessage.TryParse("{\"type\":\"earnedReward\",\"payload\":{\"amount\":0}}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.EarnedReward, msg.Type);
            Assert.AreEqual(0, msg.Amount);
        }

        [Test]
        public void TryParse_EarnedReward_MissingPayload_DefaultsToZero()
        {
            var msg = HostMessage.TryParse("{\"type\":\"earnedReward\"}");
            Assert.IsNotNull(msg);
            Assert.AreEqual(HostMessageType.EarnedReward, msg.Type);
            Assert.AreEqual(0, msg.Amount);
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
