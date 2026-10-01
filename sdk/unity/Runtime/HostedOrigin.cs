// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — origins: the one origin helper, and the loopback
// rule for UnsafeTesting.RealPairing.
//
// The credential reply, the native macOS navigation fence, and real-pairing
// validation all need the same answer to "which origin is this URL on", so it
// is computed in exactly one place.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LevelMoment
{
    internal static class HostedOrigin
    {
        /// <summary>
        /// The origin of <paramref name="url"/>, e.g.
        /// <c>https://levelmoment.com</c> or <c>http://localhost:3000</c>: the
        /// scheme, the lower-case host, and the port when it is not the
        /// scheme's default. Null when <paramref name="url"/> is not an
        /// absolute http(s) URL or carries userinfo, which every caller treats
        /// as a refusal.
        /// </summary>
        public static string Of(string url)
        {
            Uri parsed;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out parsed))
                return null;
            if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
                return null;
            if (!string.IsNullOrEmpty(parsed.UserInfo))
                return null;
            if (parsed.IsDefaultPort)
                return parsed.Scheme + "://" + parsed.Host;
            return parsed.Scheme + "://" + parsed.Host + ":" +
                parsed.Port.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The loopback rule for <see cref="UnsafeTesting.RealPairing"/>. Mirrors
    /// <c>REAL_PAIRING_HOSTS</c> and <c>isRealPairingUrl</c> in
    /// <c>sdk/core/src/hosted.ts</c>; every SDK pins the same test vectors.
    /// See docs/decisions/sdk-real-pairing-testing-2026-10-01.md.
    /// </summary>
    internal static class RealPairingUrl
    {
        public static readonly string[] Hosts = { "localhost", "127.0.0.1" };

        // The host as written, before any parser normalises it. System.Uri and
        // the WHATWG parser disagree about inputs such as `127.1`, and every
        // SDK must give the same answer for the same string.
        private static readonly Regex Literal = new Regex(
            "^http://([^/:]+):", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Warned = new HashSet<string>();

        /// <summary>
        /// The re-serialised URL when <paramref name="raw"/> is one real
        /// pairing may load, or null. Allowed: <c>http</c>, a host from
        /// <see cref="Hosts"/> written out literally, an explicit port, and no
        /// userinfo, query or fragment. Refused before parsing: a backslash,
        /// whitespace, <c>@</c>, or any non-ASCII character.
        /// </summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            foreach (var c in raw)
            {
                // Printable ASCII only, which also rules out every whitespace
                // character. `?` and `#` are refused even when nothing follows
                // them; sdk/core accepts an empty query or fragment, and this
                // stricter rule agrees with it on every shared vector.
                if (c < '!' || c > '~' || c == '\\' || c == '@' || c == '?' || c == '#')
                    return null;
            }
            var literal = Literal.Match(raw);
            if (!literal.Success || !IsAllowedHost(literal.Groups[1].Value.ToLowerInvariant()))
                return null;

            Uri uri;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out uri))
                return null;
            if (uri.Scheme != Uri.UriSchemeHttp ||
                !IsAllowedHost(uri.Host) ||
                uri.IsDefaultPort ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
                return null;
            return "http://" + uri.Host + ":" +
                uri.Port.ToString(CultureInfo.InvariantCulture) + uri.AbsolutePath;
        }

        /// <summary>True when <paramref name="raw"/> is a URL real pairing may load.</summary>
        public static bool IsAllowed(string raw)
        {
            return Normalize(raw) != null;
        }

        private static bool IsAllowedHost(string host)
        {
            return Array.IndexOf(Hosts, host) >= 0;
        }

        /// <summary>Log the real-pairing warning, once per origin.</summary>
        internal static void WarnOnce(string origin)
        {
            lock (Warned)
            {
                if (!Warned.Add(origin))
                    return;
            }
            Debug.LogWarning(
                "[LevelMoment] UnsafeTesting.RealPairing is on: pairing against " + origin +
                ", not Level Moment. Remove it before release.");
        }

        /// <summary>How many origins have been warned about. Intended for tests.</summary>
        internal static int WarnedCount
        {
            get
            {
                lock (Warned)
                {
                    return Warned.Count;
                }
            }
        }

        /// <summary>Forget which origins were warned about. Intended for tests.</summary>
        internal static void ResetWarningsForTests()
        {
            lock (Warned)
            {
                Warned.Clear();
            }
        }
    }
}
