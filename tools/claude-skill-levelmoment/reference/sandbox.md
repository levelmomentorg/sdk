# `/levelmoment sandbox` — open the LevelMoment sandbox

A short flow. Use this when the user wants to preview the question UI
without integrating yet.

## Step 1 — pick a URL

If the user passed a placement ID via `$ARGUMENTS` or there's one in
their current source (parse it from any `LevelMomentAds.Initialize` /
`createForAdRequest` / `placementId:` call), build:

```
https://app.levelmoment.com/sandbox?pl=<placementId>
```

Otherwise:

```
https://app.levelmoment.com/sandbox
```

## Step 2 — open it

On macOS:

```bash
open "<url>"
```

On Linux:

```bash
xdg-open "<url>"
```

On Windows (Git Bash / WSL):

```bash
start "" "<url>"
```

If the tool can't detect the OS or `open`/`xdg-open` isn't available,
just print the URL and tell the user to open it manually.

## Step 3 — confirm

```
✓ Opened https://app.levelmoment.com/sandbox<...>

The sandbox runs the SDK against a curated test question set. No real
impressions are recorded. Use this to preview the UI across all 8
question types and 3 break formats (flashcard, quiz, deep_dive).
```

Don't ask any further questions. The user came here for one thing.
