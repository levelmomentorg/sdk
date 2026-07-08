// LevelMoment Unity SDK — URL builder that refuses credential-shaped query
// keys, so the student session token (Authorization-header only) cannot
// regress into a URL.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace LevelMoment
{
    internal static class UrlSafety
    {
        private static readonly string[] ForbiddenSubstrings = new[]
        {
            "token",
            "authorization",
            "bearer",
            "secret",
            "password",
            "apikey",
            "api_key",
        };

        public static string BuildUrl(
            string baseUrl,
            string path,
            IReadOnlyDictionary<string, string> query = null)
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new ArgumentException("baseUrl is required", nameof(baseUrl));
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required", nameof(path));

            var sb = new StringBuilder();
            sb.Append(baseUrl.TrimEnd('/'));
            if (!path.StartsWith("/", StringComparison.Ordinal)) sb.Append('/');
            sb.Append(path);

            if (query == null || query.Count == 0) return sb.ToString();

            sb.Append('?');
            var first = true;
            foreach (var kv in query)
            {
                AssertSafeQueryKey(kv.Key);
                if (!first) sb.Append('&');
                first = false;
                sb.Append(UnityWebRequest.EscapeURL(kv.Key));
                sb.Append('=');
                sb.Append(UnityWebRequest.EscapeURL(kv.Value ?? string.Empty));
            }

            return sb.ToString();
        }

        private static void AssertSafeQueryKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException(
                    "Empty query-string key is not allowed.",
                    nameof(key));

            foreach (var forbidden in ForbiddenSubstrings)
            {
                if (key.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new ArgumentException(
                        $"LevelMoment UrlSafety: refusing to place credential-shaped key '{key}' in a URL. " +
                        "Pass credentials via the Authorization: Bearer header instead.",
                        nameof(key));
                }
            }
        }
    }
}
