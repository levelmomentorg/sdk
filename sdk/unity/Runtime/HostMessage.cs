// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — host-message parsing.
//
// The hosted /break page posts JSON messages back over the native bridge:
//   { "type": "ready" }
//   { "type": "earnedReward", "payload": { "amount": 0 | 1, "rewardId"?: "..." } }
//   { "type": "signedIn" }                                  // sign-in gate only
//   { "type": "dismissed" }
//   { "type": "error", "payload": { "code": "...", "message": "..." } }
//   { "type": "needCredential" }                            // asking this host
//   { "type": "credentialIssued", "payload": { "token": "..." } }
//   { "type": "credentialInvalid" }
//   { "type": "openExternal", "payload": { "url": "..." } }
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

        /// <summary>Sign-in gate only: this device holds a valid credential.</summary>
        SignedIn,
        Dismissed,
        Error,

        /// <summary>
        /// The page wants the credential this host holds, instead of reading
        /// one off its URL. Answered by running script in the page — see
        /// CredentialBridge. Not terminal.
        /// </summary>
        NeedCredential,

        /// <summary>
        /// Pairing minted a credential. Unity does not take custody (no secure
        /// store yet), so the page never sends this one here; the type exists
        /// so a message the shells share does not parse as unknown. Not
        /// terminal.
        /// </summary>
        CredentialIssued,

        /// <summary>
        /// The page threw its stored credential away. Nothing for Unity to
        /// forget while it keeps no copy. Not terminal.
        /// </summary>
        CredentialInvalid,

        /// <summary>
        /// Open <see cref="HostMessage.Url"/> in the device's browser, outside
        /// the WebView. The page asks for this when a parent chooses to approve
        /// the game from a browser instead of scanning the code. Parent sign-in
        /// happens outside the WebView because the game can inspect that
        /// surface; never collect a Level Moment email code or other parent
        /// credential inside it (RFC 8252). Not terminal: the break keeps
        /// polling behind the browser, so nothing has to come back.
        /// </summary>
        OpenExternal,
    }

    internal class HostMessage
    {
        public HostMessageType Type;

        /// <summary>Reward amount for <see cref="HostMessageType.EarnedReward"/> (0 or 1).</summary>
        public int Amount;

        /// <summary>Opaque impression identifier for this answer, when supplied.</summary>
        public string RewardId;

        /// <summary>Error code for <see cref="HostMessageType.Error"/>.</summary>
        public string Code;

        /// <summary>Error message for <see cref="HostMessageType.Error"/>.</summary>
        public string Message;

        /// <summary>Credential for <see cref="HostMessageType.CredentialIssued"/>.</summary>
        public string Token;

        /// <summary>Address for <see cref="HostMessageType.OpenExternal"/>.</summary>
        public string Url;

        /// <summary>
        /// The address to hand <c>Application.OpenURL</c>, or null when it does
        /// not belong to <paramref name="hostedUrl"/>'s origin.
        ///
        /// The origin check is the guard, and it has to be the origin rather
        /// than just the scheme. This bridge belongs to whatever the WebView is
        /// currently showing: a redirect that took it elsewhere keeps posting
        /// messages, and OpenURL will full-screen whatever it is handed — a
        /// page asking for a Level Moment sign-in code in the device's own browser
        /// is precisely what a phishing site wants. Pinned to the hosted page's
        /// origin, the worst a wrong URL can do is nothing.
        ///
        /// Comparing origins also settles the scheme: http passes only when the
        /// game is configured against an http BreakUrl, which is local
        /// development.
        /// </summary>
        public static string LaunchableUrl(string raw, string hostedUrl)
        {
            if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(hostedUrl))
                return null;
            Uri parsed;
            Uri hosted;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out parsed))
                return null;
            if (!Uri.TryCreate(hostedUrl, UriKind.Absolute, out hosted))
                return null;
            // Port resolves the scheme default (443/80), so https://a and
            // https://a:443 compare equal while https://a:8443 does not.
            return parsed.Scheme == hosted.Scheme
                && string.Equals(parsed.Host, hosted.Host, StringComparison.OrdinalIgnoreCase)
                && parsed.Port == hosted.Port
                ? raw
                : null;
        }

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
            if (wire.protocolVersion != 0 &&
                wire.protocolVersion != LevelMomentEndpoints.ProtocolVersion)
                return null;

            switch (wire.type)
            {
                case "ready":
                    return new HostMessage { Type = HostMessageType.Ready };
                case "earnedReward":
                    if (wire.payload == null || !raw.Contains("\"payload\""))
                        return null;
                    if (wire.payload.amount != 0 && wire.payload.amount != 1)
                        return null;
                    if (wire.payload.rewardId != null &&
                        (wire.payload.rewardId.Length == 0 ||
                         wire.payload.rewardId.Length > 256))
                        return null;
                    return new HostMessage
                    {
                        Type = HostMessageType.EarnedReward,
                        Amount = wire.payload.amount,
                        RewardId = wire.payload.rewardId,
                    };
                case "signedIn":
                    return new HostMessage { Type = HostMessageType.SignedIn };
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
                case "needCredential":
                    return new HostMessage { Type = HostMessageType.NeedCredential };
                case "credentialIssued":
                    return new HostMessage
                    {
                        Type = HostMessageType.CredentialIssued,
                        Token = wire.payload != null ? wire.payload.token : null,
                    };
                case "credentialInvalid":
                    return new HostMessage { Type = HostMessageType.CredentialInvalid };
                case "openExternal":
                    var url = wire.payload != null ? wire.payload.url : null;
                    if (string.IsNullOrEmpty(url))
                        return null;
                    return new HostMessage
                    {
                        Type = HostMessageType.OpenExternal,
                        Url = url,
                    };
                default:
                    return null;
            }
        }

        // JsonUtility DTOs. Field names must match the JSON keys exactly.
        // Every field is assigned by the deserializer, never by this code, which
        // is what CS0649 ("never assigned to") reports on each one.
#pragma warning disable 0649
        [Serializable]
        private class Wire
        {
            public string type;
            public int protocolVersion;
            public WirePayload payload;
        }

        [Serializable]
        private class WirePayload
        {
            public int amount;
            public string rewardId;
            public string code;
            public string message;
            public string token;
            public string url;
        }
#pragma warning restore 0649
    }
}
