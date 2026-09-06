/// LevelMoment Ads SDK for Flutter
///
/// Drop-in replacement for the `google_mobile_ads` rewarded ad format.
/// Replaces ad impressions with educational questions; fires onUserEarnedReward
/// when the student answers correctly — wire your game bonus logic there.
///
/// See MIGRATION.md for a line-by-line swap guide from google_mobile_ads.
library levelmoment_ads;

export 'src/levelmoment_ads.dart';
export 'src/levelmoment_ads.dart' show UnsafeTesting;
export 'src/rewarded_ad.dart';
export 'src/models.dart';
export 'src/gate.dart'
    show EnsureSignedInResult, LevelMomentSignInCheckError, kSignInCheckTimeout;
export 'src/widgets/level_moment_web_view.dart' show kBreakLoadTimeout;
// The secure store the SDK keeps its credential in. Exported so an app that
// signs a household out can drop it; the SDK manages it otherwise.
export 'src/token_store.dart'
    show LevelMomentTokenStore, deviceCredentials, credentialKey;
