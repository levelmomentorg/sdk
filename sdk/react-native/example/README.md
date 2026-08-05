# LevelMoment Sample iPhone App

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
  │  &apiUrl=... ← live API                │
  └────────────────────────────────────────┘
```

The phone and laptop must be on the same Wi-Fi (or use Expo's tunnel mode).

---

## Run on your iPhone (Expo Go — no Mac required)

1. **Install Expo Go** on your iPhone from the App Store.

2. **Build the SDK + start the break page** on your laptop:

   ```bash
   # from the repo root
   npm install
   npm run build

   # in one terminal: serve the hosted /break page
   cd platform/web
   npm run dev          # serves http://localhost:3000
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

> If your phone and laptop can't reach each other on Wi-Fi, run `npm start -- --tunnel` to use Expo's tunnel and set the Break URL to a tunneled `https://...ngrok` style URL — or just deploy `platform/web` somewhere public.

---

## Trying each break format

| Format    | What you'll see                                                                |
| --------- | ------------------------------------------------------------------------------ |
| Flashcard | One multiple-choice question. Answer it; the WebView dismisses.                |
| Quiz      | 8 questions back-to-back covering every supported type, then a summary screen. |
| Deep Dive | Same as Quiz, preceded by a multi-page lesson.                                 |

In **Mock mode**, every answer is graded as correct so you'll always see the success summary.

---

## Connecting to a real backend

Toggle **Mock mode** off and fill in:

- **API URL** — your local API (`http://localhost:8080`) or the deployed Fly URL
- **Student session token** — issue one with `POST /students/{id}/session` (parent JWT required)
- **Placement ID** — must match a placement registered for one of your games

The hosted `/break` page hits the real API and uses whatever questions the server returns.

---

## Key files

| File              | Purpose                                                                     |
| ----------------- | --------------------------------------------------------------------------- |
| `App.tsx`         | Single-screen UI: mock toggle, URL fields, format selector, ad-show button. |
| `metro.config.js` | Lets Metro find workspace packages from the monorepo root.                  |
| `app.json`        | Expo config (bundle id, name, orientation).                                 |

The SDK's `<LevelMomentAdModal />` (mounted in `App.tsx`) provides the fullscreen WebView. There is no question-rendering code in this app — it all lives in `platform/web/app/break`.

---

## Troubleshooting

**"Unable to resolve `@levelmoment/sdk-react-native`"** — run `npm run build` from the repo root first.

**Mock mode shows "Page not found" / blank screen** — your iPhone can't reach the Break URL. `localhost` only works in the iOS simulator; on a real device you need your laptop's LAN IP, or Expo's `--tunnel` mode.

**WebView opens but stays blank** — open `http://<laptop-ip>:3000/break?mock=true&format=quiz` in your laptop's browser first. If it works there, it's a network-reachability issue, not the page.

**Expo Go version mismatch** — this example targets Expo SDK 51. Update Expo Go from the App Store, or change the `expo` version in `package.json` to match your installed Expo Go.

**Want to install on your phone without Expo Go?** You'll need a Mac, Xcode, an Apple Developer account, and `npx expo prebuild && npx expo run:ios --device`. The Expo Go path above avoids all of that.
