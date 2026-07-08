// Expose `internal` runtime members (UrlSafety, ImpressionQueue, …) to
// the EditMode test assembly so we can exercise them without making
// security-sensitive helpers part of the public SDK surface.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LevelMomentSDK.Tests.EditMode")]
