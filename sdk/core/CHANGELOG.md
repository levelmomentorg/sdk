# Changelog

All notable changes to `@levelmoment/sdk-core` are documented here. Format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning follows [Semver](https://semver.org).

## [0.3.0] — unreleased

### Changed

- `resolveHostedOptions(options, env)` takes a required `HostEnvironment`
  (`{ kind: "native", debugBuild }` or `{ kind: "web", pageUrl }`) and returns
  the resolved options with a `mode`: `production`, `sandbox`, or
  `realPairing`. Resolving a resolved value returns it unchanged.
- `addBridgeVersion(params, mode)` takes the mode instead of a boolean and adds
  `sandbox=true` only in `sandbox` mode.

### Added

- `unsafeTesting.realPairing`: run the real pairing path against a hosted page
  on `http://localhost:<port>` or `http://127.0.0.1:<port>`, in a debug build
  or on a local web dev server only. It sends no `apiUrl` and accepts no token.
  See `docs/decisions/sdk-real-pairing-testing-2026-10-01.md`.
- `credentialStoreKey(origin, placementId)`: the native credential-store key.
  The production origin keeps its existing key; any other origin uses a
  separate `com.levelmoment.test-credential.` prefix.
- `isRealPairingUrl` and `REAL_PAIRING_HOSTS`.

- `HostPlatform` and `HostStoreClaims`: a host's credential reply now says
  which platform it runs on (`ios`, `android`, or `web`) and, on iOS, the App
  Store storefront (`storefront`, null until read). Level Moment uses them to
  follow each store's rules. A native shell never claims `web`.
- `toPublicAdErrorCode`, which maps a code the hosted page posts to one of the
  five public codes. A store-policy refusal reads as `no_fill`, never as
  `invalid_token`.

- `SlotDeclaration`, `adTypeForBreakFormat`, `normalizeSlotDeclaration`, and
  `addSlotParams` — the contract a game uses to declare one ad slot: the ad
  type it replaces, a target duration in seconds, and the reward amount it
  grants. `addSlotParams` puts the declaration on the hosted break URL under
  the names the hosted page and the API read, so every platform SDK spells it
  the same way.

### Changed — BREAKING

- `BreakFormat` is now `quick_question` | `practice_set` | `mastery_round` |
  `intro_lesson`. The old members `flashcard`, `quiz` and `deep_dive` are gone,
  so a game that names one no longer compiles: `flashcard` becomes
  `quick_question`, `quiz` becomes `practice_set`, `deep_dive` becomes
  `mastery_round`. `intro_lesson` is new — a mastery round that opens on an
  instruction panel.
- The service still accepts the three old names on the wire, so an already
  shipped build keeps working. That alias is a short safety net, not a
  contract; move to the new names.

## [0.2.0] — 2026-09-05

### Removed — BREAKING

- `subscription_required` is gone from `LevelMomentAdErrorCode`, along with
  every other entitlement code. A household's subscription state is Level
  Moment's, not the publisher's: a game that could read it could show its own
  upsell, and a break refused for billing reasons now surfaces the same way any
  other refusal does. A game branching on the literal no longer compiles.

### Changed

- Every request that carries a credential refuses redirects. The rule is
  applied once, in `_withTimeout`, so a redirect fails the request rather than
  carrying the credential to a host nobody validated.
- `LevelMomentConfig.studentToken` is optional. A paired device holds its
  credential on the Level Moment origin, and the shells ask the hosted surface
  for it. Set it only for a sandbox token or one read from a parent-portal
  link.
- `buildAuthHeader()` accepts `string | undefined` and returns no header at all
  when there is no credential, rather than sending `Bearer undefined`. A load
  with no credential gets the 401 that routes to the ask-a-parent pairing
  screen, and skips the device→session exchange it has nothing to send to.

### Added

- `requestTimeoutMs` option on `LevelMomentConfig` (default 10 000 ms). Every
  API call (`/questions`, `/break-sessions`, `/impressions`, `/answers`,
  `PATCH /break-sessions/:id`) is now bounded by an `AbortSignal.timeout`;
  a stalled connection no longer hangs the SDK at a game pause point. Abort
  errors surface through the existing `network_error` code path on load and
  show callbacks.

### Fixed

- `ImpressionQueue` now implements **real** exponential back-off on flush
  failure: after each consecutive failure the next scheduled attempt is delayed
  by `min(baseInterval × 2^(consecutiveFailures − 1), 5 min)`. The first retry
  waits one normal interval; each further consecutive failure doubles the delay
  (1×, 2×, 4×, … up to 5 min). The back-off resets to zero on the first
  successful flush. Back-off applies **only** to the automatic timer ticks
  started by `start()`; a direct `tryFlush()` (used by platform SDKs on app
  background/close) always attempts immediately and never creates a back-off
  window. Previous behaviour retried on every tick at a fixed 10 s interval
  regardless of failure count; this release is the first version where back-off
  is actually implemented (the 0.1.0 CHANGELOG entry that described
  "exponential backoff" was incorrect).

## [0.1.2] — 2026-07-08

### Changed

- Publish pipeline migrated to OIDC trusted publishing (no long-lived
  `NPM_TOKEN`). No functional changes to the package.

## [0.1.1] — 2026-07-08

### Changed

- Version bump across all SDK packages to verify the OIDC trusted-publishing
  pipeline end-to-end. No functional changes to the package.

## [0.1.0] — 2026-05-10

First public release. The package is platform-agnostic; consumers should
typically install one of the platform SDKs (`@levelmoment/sdk-web`,
`@levelmoment/sdk-react-native`) which depend on this package.

### Added

- `LevelMomentAd.load(config, queue, callbacks, options?, fetchFn?)` — preload
  a question break in the background.
- `LevelMomentAd.show(callbacks)` — display the cached break.
- `ad.advanceSession(answer)` — submit an answer and step to the next
  question in a multi-question session.
- `ad.isThrottled()` — check whether the impression would count toward
  payouts.
- `ad.getLesson()` — retrieve the concept lesson for `deep_dive` breaks.
- `ad.getSessionSummary()` — post-session stats (correct count, duration).
- `ad.notifyCorrectAnswer()` / `ad.notifyDismissed()` — manual lifecycle
  for flashcard format consumers that own their own UI.
- `ImpressionQueue` — batches and retries impressions with a fixed 10 s retry
  interval. Backed by a `QueueStorage` interface so platform SDKs can plug
  in their own persistence (localStorage / AsyncStorage / SharedPreferences /
  PlayerPrefs).
- `TokenStore` interface plus `MemoryTokenStore` reference implementation.
- All 8 question meta types: `MultipleChoiceMeta`, `TextInputMeta`,
  `OrderingMeta`, `MatchingMeta`, `CategorizingMeta`, `FillInBlankMeta`,
  `MultipleChoiceImageMeta`, `ImagePromptMeta`.
- Session types: `BreakSession`, `SessionSummary`, `SessionAnswer`,
  `BreakFormat`.
