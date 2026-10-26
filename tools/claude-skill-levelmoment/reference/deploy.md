# Move a sandbox integration to production

Use this path when a game's Level Moment integration passes its sandbox checks
and the developer wants to ship it. It ends with a release build that carries
no sandbox token, a device test the developer runs, and a review email the
developer sends. Level Moment's reply clears the game for release.

The public docs are the guide; this page adds only what an agent needs on top.
Open each page before its step and follow it, reading it as page text through
a browser (see SKILL.md). Without web access, the one-line summary beside each
link says what the step is; ask the developer to open the page if you need its
detail.

## 1. Ask the developer first

Ask these together, once, before changing anything.

1. **Where it ships:** the App Store, Google Play, the web, or several. This
   decides which builds to check and goes in the review email.
2. **Who uploads:** does the developer upload the test build (TestFlight, a
   Google Play internal track, or a non-public URL) and later submit to the
   stores themselves, or do they want you to? Upload only when they say so
   explicitly. Submitting for store review or publishing waits for Level
   Moment's reply either way.
3. **Release command:** the exact command or script that makes each release
   build, if the repository doesn't make it obvious.

## 2. Pre-flight

Fix anything here before step 3.

- Every slot passed the checks in
  [Testing and sandbox](https://levelmoment.com/docs/testing#verify): pass,
  fail and load failure each grant correctly and resume once. If there's no
  record of that, ask the developer for their test log, or run those checks
  now in a sandbox build before continuing.
- The SDK is pinned to the exact version tested, such as `0.4.0`, with no
  range, in the manifest and the lockfile.
- No file in the repository still describes or drives a mock or placeholder
  break. Grep for the mock's names in source, CSS class names, comments,
  scripts (playtest, screenshot, build), README and docs; check screenshots
  and store images for a mock break too. The README says which command runs
  sandbox and which builds production.

## 3. Follow the docs in order

[Going live](https://levelmoment.com/docs/going-live) holds every step.

1. [Remove the token](https://levelmoment.com/docs/going-live#token): take
   the sandbox token out of every environment a release build reads (CI,
   hosting, EAS production profile, `.env.local`), keep it only in the
   sandbox-only file behind the build guard, run the release build (for
   Capacitor, `npm run build && npx cap sync` before every archive), and run
   the token check from
   [Testing and sandbox](https://levelmoment.com/docs/testing#production-build)
   over `dist` and the Capacitor folders that exist. It runs in bash, zsh and
   sh, and prints "Check failed" on a match or an error. Then build the
   release configuration for a simulator or emulator without signing (for
   Capacitor, the page's `xcodebuild` command, which picks the workspace or
   the project, and `./gradlew assembleRelease`). That native build check is yours; the
   device test isn't. The build passes no `unsafeTesting` and no `mock`.
2. [Store disclosures](https://levelmoment.com/docs/going-live#disclosures):
   list for the developer what the build sends: the rows of
   [Data disclosures](https://levelmoment.com/docs/data-safety) (placement ID
   and SDK version, learner credentials, questions and answers, IP address
   and user-agent, optional `customData`) plus every other SDK in the
   manifests. List the iOS privacy manifests with
   `find ios node_modules -name PrivacyInfo.xcprivacy -not -path "*/build/*"`;
   Level Moment's SDKs ship none. Check the store category as the page
   describes; if you can't see App Store Connect or Google Play Console, ask
   the developer. The developer answers the forms.
3. [Upload a test build](https://levelmoment.com/docs/going-live#testing-track):
   TestFlight, a Google Play internal testing track, or a non-public URL, not
   store review. Its link goes in the email.
4. [Device test](https://levelmoment.com/docs/going-live#device-test): from
   the test build, on a target device with a Level Moment account that has a plan
   (a new account can start a 7-day trial), link the device, approve the
   game, and pass a break for one reward; on an unlinked device, cancel the
   linking card and confirm the game resumes. Trial breaks earn nothing.
5. [Send the build](https://levelmoment.com/docs/going-live#review): the
   developer emails support@levelmoment.com using the page's template (game
   name, placement ID, where it ships, SDK package and version, the test
   build link, and that they won't submit until Level Moment replies), then
   waits for the reply before submitting the same build or publishing. The
   portal has no approval status.
6. [Verify after release](https://levelmoment.com/docs/going-live#verify):
   impressions show on the game's portal page at once; the earnings estimate
   lags about three hours; payouts go out on the 1st of the next month once a
   bank account is connected; a configured webhook receives `reward.earned`.

Also check that a Capacitor shell still allows `https://levelmoment.com` as a
frame ([Web views](https://levelmoment.com/docs/web-views)).

## Agent-only notes

- **Never read or print the token.** Check for it by exit status, not by
  content: `grep -rl eply_sbx_ dist` prints only file names, and
  `grep -c '^VITE_LM_SANDBOX_TOKEN=' .env.local` only a count. Don't `cat` an
  env file, echo a variable or open CI secrets. To take the line out of a
  file a release build reads, first make sure the sandbox-only file holds
  it (`grep -c` on that file). If it doesn't, move the line without
  printing it, for example
  `git check-ignore .env.sandbox.local && grep '^VITE_LM_SANDBOX_TOKEN=' .env.local >> .env.sandbox.local`.
  Then delete it in place, for example
  `sed -i.bak '/^VITE_LM_SANDBOX_TOKEN=/d' .env.local && rm .env.local.bak`,
  and run `grep -c '^VITE_LM_SANDBOX_TOKEN=' .env.local` again, expecting
  `0`. Never commit, log or paste it.
- **CI and hosting variables belong to the developer.** If a release
  environment such as Vercel, Netlify, EAS or a CI provider holds the token,
  tell the developer which variable and where; don't remove it through a
  dashboard or CLI unless they ask you to.
- **Build and verify locally.** Run the release command and the grep; report
  each command and its result. A clean grep is the evidence; never paste
  build contents.
- **Drive the developer's browser only for reading.** Going live has no
  portal action. If you open the portal to read the placement ID or game
  name, find the developer's browser the way `reference/self-serve.md`
  describes, never sign in or switch accounts, and change nothing.
- **Fill the review email template; don't send it.** Copy the template from
  [Going live](https://levelmoment.com/docs/going-live#review) into chat or
  a file the developer chooses, filled with the game name, placement ID,
  channels, and SDK package and version, with `TEST_BUILD_LINK` left for the
  developer when the test build isn't uploaded yet. Leave out the token.

## Stop conditions

Stop and hand to the developer, saying exactly what they need to do, for:

- sending the review email, or any other message, on their behalf;
- uploading a test build to TestFlight, Google Play or a host, unless they
  explicitly asked you to;
- submitting for store review or publishing, unless they explicitly asked
  you to and Level Moment has replied;
- accepting any terms, agreements or store declarations;
- signing in, switching accounts, or entering a password or payment card;
- anything that needs a Level Moment account with a plan or a physical device,
  including the device test;
- changing a CI or hosting secret they haven't asked you to change;
- a grep that still finds `eply_sbx_` after you've fixed the files you can
  see, or a release build that fails.

## 4. Report

Report the release command, the token check and native build result per
platform (never the token), files changed, the SDK version pinned, the
disclosure inventory, the filled email, and what the developer still has to
do, in order: the store disclosures, uploading the test build, the device
test, sending the email, and submitting after Level Moment's reply.
