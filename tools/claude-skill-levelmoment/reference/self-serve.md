# Set up Level Moment without a handoff

Use this path when a developer integrates their own game and nobody supplied a
handoff manifest. It takes the game from no Level Moment code to verified
sandbox breaks. Going live is a later step the developer approves.

The public docs are the guide; this page adds only what an agent needs on top.
Open each page before its step and follow it, reading it as page text through a browser (see SKILL.md). Without web access, the one-line
summary beside each link says what the step is; ask the developer to open the
page if you need its detail.

## 1. Ask the developer first

Ask these together, once, before editing. Everything else has one right
answer in the docs; don't ask about it.

1. **Slots:** which moments show a break, how long each can hold the player,
   and what each grants. Find candidates first (rewarded-ad calls, "watch to
   continue" buttons, a game's own break function) and propose them.
2. **Reward authority:** does the game grant rewards locally, or does a game
   server hold the balance? A server-held balance needs the verified webhook
   (see "Preserve reward authority" in AGENT-INSTRUCTIONS.md).
3. **Game name** as it should appear in the Level Moment portal.

## 2. Follow the docs in order

1. [Quickstart](https://levelmoment.com/docs/getting-started): register the
   game ("Register game", webhook URL optional), commit the public placement
   ID in game config with no mock fallback, install the SDK at exactly 0.3.0,
   declare a slot per moment (5 to 300 seconds), and call `ensureAccess()`
   from the player's tap (in sandbox it resolves `ready` at once). A
   signed-in non-developer account sees "Use the right account for this
   page" instead of the Games page.
2. [Testing and sandbox](https://levelmoment.com/docs/testing): one token
   per developer, kept in a file only sandbox builds read (Vite
   `.env.sandbox.local` with `--mode sandbox`, Expo `.env.development.local`,
   never `.env.local`) behind a build guard, passed only as
   `unsafeTesting: { token }`; check each slot's pass, fail and load failure,
   then grep a production build for `eply_sbx_`.
3. [Your own break interface](https://levelmoment.com/docs/own-break-interface):
   when the game already has a promise-based break function, keep it and its
   call sites and implement it with `templates/web-promise-seam.ts.tmpl`. Give
   each moment its own `BreakSlotSpec` id and `rewardAmount` (none for a moment
   that grants nothing), adding both to the game's own slot type if it lacks
   them. The await is the pause; grant on `"finished"`. Keep the game's own
   outcome names and map to them in one line where the function returns. Free-break counts and plan
   status aren't available to partner integrations.
4. [Web views and Capacitor](https://levelmoment.com/docs/web-views): for a
   web game in a native shell, allow `https://levelmoment.com` as a frame,
   cover the safe areas, and keep taps on the break.
5. [Troubleshooting](https://levelmoment.com/docs/errors#troubleshooting):
   no sandbox break (placement ID isn't a registered game), a device-linking
   card (missing or revoked token, or another account's game), `no_fill`, the wrong-account page, and tooling that drove
   a removed mock overlay.
6. [Going live](https://levelmoment.com/docs/going-live): what changes from a
   sandbox build to production. Don't start it without the developer's
   approval.

Apply the lifecycle and checks in AGENT-INSTRUCTIONS.md to every slot.

## Agent-only notes

- **Find the developer's browser by checking, not by name.** Before the first
  portal step: if the developer named a browser, use only that one and match
  it by device ID when they give one, since display names change. Otherwise
  open `https://levelmoment.com/developer` in each connected browser and use
  the one that shows the developer dashboard ("Welcome back, ..."). If none
  does, stop and ask the developer. If you can't drive a browser, ask the
  developer to register the game and paste the placement ID; the token goes
  straight into the env file, never into chat.
- **Never sign in or switch accounts.** Don't enter credentials. If a page
  says "Use the right account for this page" (the body reads "You're signed
  in, but this page is for developer accounts"), stop and ask the developer; don't choose **Switch account** yourself.
- **Move the token without reading it.** Don't read the token from the page
  text, accessibility tree or a screenshot. Choose the page's **Copy** button,
  then write the clipboard straight into the env file, for example on macOS:
  `printf 'VITE_LM_SANDBOX_TOKEN=%s\n' "$(pbpaste)" > .env.sandbox.local`. Confirm the
  file is gitignored first.
- **Reuse before regenerating.** If another of the developer's games already
  holds the token, move its line without printing it, for example
  `grep '^VITE_LM_SANDBOX_TOKEN=' ../FIRST_GAME/.env.sandbox.local >> .env.sandbox.local`
  (adjust the variable and file for the platform). A game whose `.env.local`
  already holds the token needs the line moved the same way (`grep -v` it out
  of `.env.local` afterwards). Ask the developer
  before choosing **Regenerate token**: it revokes the token for all their
  games.
- **The confirmation dialog may need the developer.** Regenerating opens a
  browser confirmation that automation may not be able to dismiss, and it can
  freeze the tab. Ask the developer to click it, or reopen the tab afterwards.
- **Never print the token** in chat, a log, a commit, a URL or your report.
- **Drive a sandbox break by its visible text.** Click by what the player
  sees, never by CSS selectors, which aren't a public contract. Every break
  opens on "Begin"; an interstitial break may show "Next" and "Start
  questions" on a lesson panel. A correct answer moves on by itself with no
  button; a wrong answer shows "Got it"; the last screen has "Back to the
  game". To fail a rewarded break, answer every question wrong; to fail an
  interstitial break, keep answering one question wrong until its 3 attempts
  run out.
- **The break is a cross-origin iframe.** DOM selectors and text lookups
  from the game page can't reach inside it. Find the visible text in a
  screenshot and click its coordinates, or in Playwright use
  `page.frameLocator('iframe[src^="https://levelmoment.com/break"]')` with
  `getByRole` or `getByText`.
- **Update the game's own scripts.** If the game's screenshot or playtest
  scripts drove the old mock overlay, update them to the real break page in
  the same change.

## 3. Report

Report the game name and placement ID (never the token), each slot and its
duration, files changed, commands run and their results, and what remains to
check on a real device. Name going live as the next step; it needs the
developer's approval.
