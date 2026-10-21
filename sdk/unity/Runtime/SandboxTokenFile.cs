// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — the sandbox token a development build finds by itself.
//
// A game's sandbox token lives in UserSettings/LevelMomentSandboxToken.txt, a
// folder Unity's standard .gitignore already excludes. The editor reads it from
// there; Editor/LevelMomentSandboxBuild.cs copies it into a development build for
// the length of that build and keeps it out of every release build. A release
// build never looks, so the game writes no sandbox code and no build guard.
// ---------------------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;

namespace LevelMoment
{
    internal static class SandboxTokenFile
    {
        /// <summary>The resource name a development build carries the token under.</summary>
        internal const string ResourceName = "LevelMomentSandboxToken";

        internal const string TokenPrefix = "eply_sbx_";

        /// <summary>The token file, relative to the Unity project folder.</summary>
        internal const string ProjectRelativePath = "UserSettings/LevelMomentSandboxToken.txt";

        /// <summary>
        /// Test seam: replaces the lookup below. Internal, and
        /// <see cref="LevelMomentAds.ResetForTests"/> clears it.
        /// </summary>
        internal static Func<string> SourceForTests;

        /// <summary>
        /// The development build's sandbox token, or null when there is none.
        /// Never call it from a release build; <see cref="LevelMomentAds.Initialize"/>
        /// checks first. A value without the sandbox prefix is ignored with a
        /// warning that never includes it.
        /// </summary>
        internal static string Find()
        {
            var raw = SourceForTests != null ? SourceForTests() : ReadProjectFile() ?? ReadResource();
            if (string.IsNullOrEmpty(raw)) return null;
            var token = raw.Trim();
            if (token.Length == 0) return null;
            if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
            {
                Debug.LogWarning("[LevelMoment] The sandbox token file doesn't hold a sandbox token (they start with " +
                    TokenPrefix + "), so this build runs without sandbox.");
                return null;
            }
            return token;
        }

        static string ReadProjectFile()
        {
#if UNITY_EDITOR
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), ProjectRelativePath);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
#else
            return null;
#endif
        }

        static string ReadResource()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            return asset != null ? asset.text : null;
        }
    }
}
