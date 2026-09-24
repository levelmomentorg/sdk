// Expose `internal` runtime members (BreakUrl, HostMessage, LoadWatchdog,
// NoWebViewFallback, the RewardedAd test seams, …) to the EditMode test
// assembly so we can exercise the shell's pure logic without a WebView or the
// public SDK surface.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LevelMomentSDK.Tests.EditMode")]
// The gree adapter defers to the native macOS view (MacNativeWebView).
[assembly: InternalsVisibleTo("LevelMomentSDK.Gree")]
