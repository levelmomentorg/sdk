// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — placement ID shape check.
//
// A placement ID is `pl_` and 22 URL-safe characters, as the developer portal
// shows it. A wrong one (an AppLovin ad unit, a game name, a typo) still opens
// the hosted page, which then reports that the game isn't registered. Warn in
// the console when the ID is called, so the mistake shows up before a device
// test. A warning, not a refusal: the server decides what a placement is.
// ---------------------------------------------------------------------------

using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LevelMoment
{
    internal static class PlacementIdFormat
    {
        private static readonly Regex Pattern = new Regex("^pl_[A-Za-z0-9_-]{22}$");
        private static readonly HashSet<string> Warned = new HashSet<string>();

        /// <summary>True when the ID has the portal's shape.</summary>
        internal static bool IsWellFormed(string placementId)
        {
            return placementId != null && Pattern.IsMatch(placementId);
        }

        /// <summary>
        /// Log one warning per distinct malformed ID. Returns true when it
        /// warned. An empty ID is left to the caller, which fails the call.
        /// </summary>
        internal static bool WarnIfMalformed(string placementId)
        {
            if (string.IsNullOrEmpty(placementId) || IsWellFormed(placementId))
                return false;
            lock (Warned)
            {
                if (!Warned.Add(placementId))
                    return false;
            }
            Debug.LogWarning("[LevelMoment] \"" + placementId + "\" isn't a placement ID. " +
                "Copy the ID that starts with pl_ from the game's page at levelmoment.com/games. " +
                "Level Moment can't serve a break for an ID it didn't issue.");
            return true;
        }

        /// <summary>Tests only: forget which IDs already warned.</summary>
        internal static void ResetForTests()
        {
            lock (Warned)
                Warned.Clear();
        }
    }
}
