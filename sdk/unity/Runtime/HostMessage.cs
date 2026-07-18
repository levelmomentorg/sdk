// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — host-message parsing.
//
// The hosted /break page posts JSON messages back over the native bridge:
//   { "type": "ready" }
//   { "type": "earnedReward", "payload": { "amount": 0 | 1 } }
//   { "type": "dismissed" }
//   { "type": "error", "payload": { "code": "...", "message": "..." } }
//
// Mirrors the HostMessage union in sdk/react-native/src/LevelMomentAd.ts and
// HostMessage.tryParse() in sdk/flutter — malformed / unknown payloads parse to
// null and are silently dropped by the caller. Kept as pure logic (JsonUtility
// only) so it is covered by the EditMode tests without a WebView.
// ---------------------------------------------------------------------------

using System;
using UnityEngine;

namespace LevelMoment
{
    internal enum HostMessageType
    {
        Ready,
        EarnedReward,
        Dismissed,
        Error,
    }

    internal class HostMessage
    {
        public HostMessageType Type;

        /// <summary>Reward amount for <see cref="HostMessageType.EarnedReward"/> (0 or 1).</summary>
        public int Amount;

        /// <summary>Error code for <see cref="HostMessageType.Error"/>.</summary>
        public string Code;

        /// <summary>Error message for <see cref="HostMessageType.Error"/>.</summary>
        public string Message;

        /// <summary>
        /// Parse a raw JSON string from the page bridge. Returns null when the
        /// payload is malformed, non-object, or of an unknown type (matching the
        /// RN/Flutter shells, which silently drop unparseable messages).
        /// </summary>
        public static HostMessage TryParse(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;

            Wire wire;
            try
            {
                // JsonUtility throws on malformed JSON and on non-object roots
                // (arrays, bare scalars). All of those become null here.
                wire = JsonUtility.FromJson<Wire>(raw);
            }
            catch (Exception)
            {
                return null;
            }

            if (wire == null || string.IsNullOrEmpty(wire.type))
                return null;

            switch (wire.type)
            {
                case "ready":
                    return new HostMessage { Type = HostMessageType.Ready };
                case "earnedReward":
                    return new HostMessage
                    {
                        Type = HostMessageType.EarnedReward,
                        Amount = wire.payload != null ? wire.payload.amount : 0,
                    };
                case "dismissed":
                    return new HostMessage { Type = HostMessageType.Dismissed };
                case "error":
                    return new HostMessage
                    {
                        Type = HostMessageType.Error,
                        Code = wire.payload != null && !string.IsNullOrEmpty(wire.payload.code)
                            ? wire.payload.code
                            : "unknown",
                        Message = wire.payload != null && !string.IsNullOrEmpty(wire.payload.message)
                            ? wire.payload.message
                            : "Unknown error",
                    };
                default:
                    return null;
            }
        }

        // JsonUtility DTOs. Field names must match the JSON keys exactly.
        [Serializable]
        private class Wire
        {
            public string type;
            public WirePayload payload;
        }

        [Serializable]
        private class WirePayload
        {
            public int amount;
            public string code;
            public string message;
        }
    }
}
