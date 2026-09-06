// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — sending a parent out to the device's browser.
//
// The hosted page posts `openExternal` when a parent chooses to approve the
// game from a browser instead of scanning the pairing code on their phone. The
// Parent sign-in happens outside the WebView because the game can inspect that
// surface. Never collect a Level Moment email code or other parent credential
// inside it (RFC 8252, and the "Embedded login" decision in the platform plan).
//
// Nothing comes back. The break stays open behind the browser and keeps
// polling for the approval, which is the same way it learns about a code
// scanned on a second device.
//
// The decision about WHICH addresses may leave is pure and lives in
// HostMessage.LaunchableUrl, so the EditMode tests cover it without a WebView
// or a player.
// ---------------------------------------------------------------------------

using UnityEngine;

namespace LevelMoment
{
    internal static class ExternalBrowser
    {
        /// <summary>
        /// Open <paramref name="rawUrl"/> in the device's browser, if it
        /// belongs to <paramref name="hostedUrl"/>'s origin — the URL this
        /// surface was opened with. Anything else is ignored, so a WebView that
        /// wandered off cannot send a parent to a page of its own choosing.
        /// </summary>
        public static void Open(string rawUrl, string hostedUrl)
        {
            var url = HostMessage.LaunchableUrl(rawUrl, hostedUrl);
            if (url == null)
                return;
            Application.OpenURL(url);
        }
    }
}
