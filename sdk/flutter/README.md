# levelmoment_ads (Flutter)

**Preview:** Validate the WebView provider and hosted break on each target
device before release. The repository version is `0.2.0` and is not published
to `pub.dev`. Use the immutable preview artifact or reference supplied for your
partner integration.

Use `AGENT-INSTRUCTIONS.md` in the complete partner packet supplied with this
preview. Give the agent that packet, the immutable artifact or source reference,
the placement ID, target slot, and reward action.

Flutter SDK for iOS and Android apps. Drop-in replacement for the
`google_mobile_ads` rewarded ad format.

See [`MIGRATION.md`](MIGRATION.md) for a line-by-line swap guide.

---

## What's Done

- **`LevelMomentAds.instance.initialize(...)`** — uses `https://levelmoment.com/break` and `https://levelmoment.com/api` by default. Use `unsafeTesting` for local URLs and `eply_sbx_` sandbox credentials
- **Optional sign-in gate** — call `ensureSignedIn(...)` when your game enables learning breaks at startup. It reports `ready`, `canceled`, or `technicalFailure`; regular gameplay can start without it
- **Optional access gate** — call `checkAccess(...)` for a boolean check (`false` means the access flow is needed; technical failures throw) or `ensureAccess(...)` for the same three outcomes as `ensureSignedIn(...)`. These methods use the hosted `/access` surface and leave identity methods on `/break`
- **`LevelMomentRewardedAd.load(...)`** — static factory mirrors `RewardedAd.load(adUnitId, request, callback)`; synchronous mark-ready (no native preload), supports `format` (`'quick_question'`, `'practice_set'`, `'mastery_round'`, `'intro_lesson'`) and an optional SSV-parity `customData` (stamped onto every impression the hosted page records)
- **`LevelMomentAdLoadCallback`** — mirrors `RewardedAdLoadCallback` (`onAdLoaded`, `onAdFailedToLoad`)
- **`LevelMomentFullScreenContentCallback`** — mirrors `FullScreenContentCallback` (`onAdShowedFullScreenContent`, `onAdFailedToShowFullScreenContent`, `onAdDismissedFullScreenContent`)
- **`LevelMomentRewardItem`** — mirrors `RewardItem` (`type`, `amount`, optional `rewardId`)
- **`ad.show(context:, onUserEarnedReward:)`** — pushes `LevelMomentWebView` as a fullscreen route; the hosted page renders the learning activity
- **`LevelMomentWebView`** — fullscreen `WebView` pointing at the hosted `/break` page, with a JavaScript channel that handles `ready`, `earnedReward`, `signedIn`, `dismissed`, and `error`. Break messages drive the ad callbacks; `signedIn` resolves the sign-in gate. With `hidden: true` the widget renders nothing at all while still loading the page for the headless credential check
- **postMessage bridge** — terminal-once dismiss semantics. `onUserEarnedReward` fires once per graded answer (`amount` 1 correct / 0 otherwise). Grant the chosen bonus on the first correct callback, keep it after a later failure, and resume once on either terminal callback.
- **Pre-`ready` watchdog** — 15 seconds, mirroring `sdk/web`, `sdk/react-native`, and `sdk/unity`. An unreachable or crashed break page resolves as a clean dismissal; a main-frame load failure fires `onAdFailedToShowFullScreenContent` with code `network_error`. After `ready` there is no timeout.
- **Dart unit tests** — `test/rewarded_ad_test.dart` covers URL building, the `not_loaded` guard, `HostMessage` parsing, and terminal-once; `test/gate_test.dart` covers the gate URL (gate/check mode, mock/live, `?`/`&`), the `signedIn` verdict, the `SignInDispatcher` mapping and its settle-exactly-once discipline, mock mode, and the no-navigator paths for both entry points
- **`MIGRATION.md`** — complete line-by-line swap guide
- **Parent approval opens in the system browser** (`url_launcher: ^6.3.0`) — when a parent chooses to approve the game in a browser rather than scan the pairing code, the page posts `{type:"openExternal",payload:{url}}` and the shell launches it with `LaunchMode.externalApplication`. It never loads in the WebView: parent sign-in happens outside the WebView because the game can inspect that surface. Never collect a Level Moment email code or other parent credential inside it (RFC 8252). The browser does not redirect to the WebView. After approval, return to the game; the break resumes polling and completes the connection automatically. Only HTTP and HTTPS URLs on the Level Moment origin the SDK loaded (`breakUrl`) are passed to the OS, which blocks custom-scheme links; verified Universal Links or App Links may still open an associated app (`OpenExternal.launchableFrom`). Break and gate URLs include `caps=openExternal`. The hosted page shows **Approve in your browser** when this capability is present; otherwise it shows only the QR code and typed-code options.
- **Platform secure-store credential storage** — the SDK keeps its own copy of a paired device's credential, scoped per placement. It answers the page's `needCredential` from the secure store, writes what pairing issues, and clears what the server refuses. `studentToken` is optional everywhere: a paired device needs none. The deprecated field is accepted only for `eply_sbx_` testing credentials under `unsafeTesting`.
- **Secure-store availability probe** — the store is probed on first use. When it will not register, the SDK tells the page it is not keeping custody, so the hosted origin keeps ownership of the credential instead of the SDK dropping writes silently.
- **`LevelMomentAds.instance.signOut(context:, placementId:)`** — clears the secure-store copy, then clears the hosted origin's copy. See [Sign out](#sign-out) below.

The SDK opens the hosted break in a fullscreen WebView and reports answers and
completion through the callbacks. `load()` prepares the shell; it does not
preload question content. Resume regular gameplay from both dismissal and load
failure callbacks.

## What's Not Done

- [ ] No example app in `sdk/flutter/example/`
- [ ] Repository version is `0.2.0` — the preview is not yet published to `pub.dev`
- [ ] No native pre-warm — `load()` resolves immediately; `show()` triggers the WebView fetch, adding a small visible delay when the ad slot opens

---

## Setup

```bash
# Requires Flutter SDK installed (https://docs.flutter.dev/get-started/install)
cd sdk/flutter
flutter pub get
flutter analyze
```

Install the immutable `0.2.0` preview artifact or reference supplied for your
partner integration; do not use `flutter pub add levelmoment_ads`, because the
preview is not on `pub.dev`. Its package manifest already installs
`flutter_secure_storage` and `url_launcher` as transitive dependencies.

**Note:** The SDK keeps a second copy of a paired device's credential in the
platform secure store (Keychain on iOS, EncryptedSharedPreferences on
Android), so the credential survives the WebView's site data being cleared.
Without it, an eviction costs a household a re-pair, with a parent's phone
involved.

**Note:** On a device whose secure store will not register, the SDK probes the
store on first use, tells the break page it is not keeping custody, and leaves
the credential with the hosted origin. Pairing still works; the credential
lasts only as long as the WebView's site data.

---

## Usage

```dart
import 'package:levelmoment_ads/levelmoment_ads.dart';

// 1. Initialize once at app start (in main() or initState)
await LevelMomentAds.instance.initialize();

// 2. Opt in to the sign-in gate when enabling learning breaks at startup.
switch (await LevelMomentAds.instance.ensureSignedIn(
  context: context,
  placementId: 'YOUR_PLACEMENT_ID',
)) {
  case EnsureSignedInResult.ready:
    startGame();
  case EnsureSignedInResult.canceled:
    showBreaksAreOffScreen();
  case EnsureSignedInResult.technicalFailure:
    showTryAgainLaterScreen();
}

// Use the access surface separately when your app enables that flow later.
final accessReady = await LevelMomentAds.instance.checkAccess(
  context: context,
  placementId: 'YOUR_PLACEMENT_ID',
);
if (!accessReady) showAccessSetup();

// 3. Create a fresh handle whenever this existing rewarded slot opens.
void showBreak(BuildContext context) {
  pauseGame();
  var granted = false;
  var finished = false;
  void finish() {
    if (finished) return;
    finished = true;
    resumeGame();
    prepareNextBreak();
  }

  LevelMomentRewardedAd.load(
    placementId: 'YOUR_PLACEMENT_ID',
    adLoadCallback: LevelMomentAdLoadCallback(
      onAdLoaded: (ad) {
        ad.fullScreenContentCallback = LevelMomentFullScreenContentCallback(
          onAdDismissedFullScreenContent: (_) => finish(),
          onAdFailedToShowFullScreenContent: (_, __) => finish(),
        );
        ad.show(
          context: context,
          onUserEarnedReward: (_, reward) {
            if (!finished && reward.amount == 1 && !granted) {
              granted = true;
              grantBonus();
            }
          },
        );
      },
      onAdFailedToLoad: (_) => finish(),
    ),
  );
}
```

No `studentToken` above — a paired device's credential lives in the SDK's
secure store. Use `unsafeTesting` with an `eply_sbx_` token for sandbox work;
the SDK never reads or writes the production secure store in that mode.

That's it. The SDK pushes a fullscreen route hosting a WebView that loads the
hosted `/break` page and calls `onAdDismissedFullScreenContent` when the
student finishes.

### Sign out

```dart
await LevelMomentAds.instance.signOut(
  context: context,
  placementId: 'YOUR_PLACEMENT_ID',
);
```

The credential lives in two places. Clearing only the secure store leaves the hosted origin's copy, which the next `ensureSignedIn()` validates and signs the previous learner back in with. On a shared device that is the failure sign-out exists to prevent. `signOut()` clears the secure store first, then clears the hosted copy.

**Warning:** `signOut()` is not atomic. If the hosted clear cannot run it throws `LevelMomentSignInCheckError` with this app's credential already gone and the household still signed in. Call it again when there is a network.

Do not call the token store's `clear()` to sign a device out. It empties the secure store and leaves the hosted copy in place.

**Note:** the secure store receives exactly one thing from the page:
`credentialIssued` carrying a token the in-page pairing flow just minted. A
credential this SDK handed over is never announced back, and neither is one the
page found in its own storage. Browser hosts receive no credential at all. See
See the hosted integration documentation for credential handling.

---

## Related

See [MIGRATION.md](MIGRATION.md) for the integration steps.

The portal example in `test/portal_example.dart` is checked by Flutter analysis. It matches the generated setup guide.
