# LevelMoment Sample iPhone App

This app is a testing sample. Its editable hosted URL is passed through
`unsafeTesting`; production game integrations use the SDK defaults and omit
that adapter.

**Status: ✅ Scaffolded** — Expo example app for `@levelmoment/sdk-react-native`.

A minimal React Native + Expo app that runs on your iPhone via Expo Go. The SDK opens a fullscreen WebView at the hosted `/break` page; this app proves the SDK contract end-to-end without needing the backend deployed.

---

## Architecture (this branch)

```
┌──────────────────────────────────────────┐
│  iPhone (Expo Go)                        │
│  ┌────────────────────────────────────┐  │
│  │ Sample app (App.tsx)               │  │
│  │  • Configures LevelMomentAd            │  │
│  │  • Mounts <LevelMomentAdModal />       │  │
│  └────────────────────────────────────┘  │
│  ┌────────────────────────────────────┐  │
│  │ <LevelMomentAdModal /> (WebView)       │  │
│  │  loads ↓                           │  │
│  └────────────────────────────────────┘  │
└──────────│───────────────────────────────┘
           ↓ HTTP
  ┌────────────────────────────────────────┐
  │  Laptop                                │
  │  Next.js dev server :3000              │
  │  /break?placementId=...&format=...     │
  │  &mock=true  ← bundled questions       │
  │  hosted service ← default production URL                │
  └────────────────────────────────────────┘
```

The phone and laptop must be on the same Wi-Fi (or use Expo's tunnel mode).

---

## Run on your iPhone (Expo Go — no Mac required)

1. **Install Expo Go** on your iPhone from the App Store.

2. **Build the SDK + start the break page** on your laptop:

   ```bash
   # from the repository checkout
   npm install

   ```

3. **Find your laptop's LAN IP** (the iPhone needs to reach it):

   ```bash
   # macOS / Linux
   ipconfig getifaddr en0
   # or
   hostname -I | awk '{print $1}'
   ```

4. **Start the example app:**

   ```bash
   cd sdk/react-native/example
   npm install
   npm start
   ```

5. **Scan the QR code** with the iPhone Camera app. Expo Go opens the sample.

6. In the app, set **Break page URL** to `http://<your-laptop-ip>:3000/break` (replacing `localhost`). Toggle **Mock mode**, pick a format, tap **Show ad break**.

> If your phone cannot reach the configured Break URL, use a reachable HTTPS deployment or your local network address.

---

## Trying each break format

| Format         | What you'll see                                                                |
| -------------- | ------------------------------------------------------------------------------ |
| Quick Question | One multiple-choice question. Answer it; the WebView dismisses.                |
| Practice Set   | 8 questions back-to-back covering every supported type, then a summary screen. |
| Mastery Round  | Same as Practice Set, preceded by a multi-page lesson.                         |

In **Mock mode**, every answer is graded as correct so you'll always see the success summary.

---

## Connecting to a real service

Toggle **Mock mode** off and enter a placement ID registered in your developer account. The SDK uses the default production service; set a custom Break URL only when testing a separately hosted page.

The hosted `/break` page hits the real API and uses whatever questions the server returns.

---

## Key files

| File              | Purpose                                                                     |
| ----------------- | --------------------------------------------------------------------------- |
| `App.tsx`         | Single-screen UI: mock toggle, URL fields, format selector, ad-show button. |
| `metro.config.js` | Lets Metro find workspace packages from the monorepo root.                  |
| `app.json`        | Expo config (bundle id, name, orientation).                                 |

The SDK's `<LevelMomentAdModal />` (mounted in `App.tsx`) provides the fullscreen WebView and owns the hosted activity UI.

---

## Troubleshooting

**"Unable to resolve `@levelmoment/sdk-react-native`"** — run `npm run build` from the repo root first.

**Mock mode shows "Page not found" / blank screen** — your iPhone can't reach the Break URL. `localhost` only works in the iOS simulator; on a real device you need your laptop's LAN IP, or Expo's `--tunnel` mode.

**WebView opens but stays blank** — open `http://<laptop-ip>:3000/break?mock=true&format=practice_set` in your laptop's browser first. If it works there, it's a network-reachability issue, not the page.

**Expo Go version mismatch** — this example targets Expo SDK 51. Update Expo Go from the App Store, or change the `expo` version in `package.json` to match your installed Expo Go.

**Want to install on your phone without Expo Go?** You'll need a Mac, Xcode, an Apple Developer account, and `npx expo prebuild && npx expo run:ios --device`. The Expo Go path above avoids all of that.
