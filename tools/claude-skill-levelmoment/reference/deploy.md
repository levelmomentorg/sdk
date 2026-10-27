# Move a sandbox integration to production

Use this path when a game's Level Moment integration passes its sandbox checks
and the developer wants to ship it. It ends with a release build that carries
no sandbox token, a device test the developer runs, and a review email the
developer sends. Level Moment's reply clears the game for release.

The public docs are the guide; this page adds only what an agent needs on top.
Open each page before its step and follow it, reading the full page text
through a browser tool (see SKILL.md). Without web access, the summary beside each link says which step comes next, not how to do it.
Ask the developer to open or paste the page before you run that step.

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
   build, if the repository doesn't make it obvious. Read a ship or release
   script before running it: some build and upload in the same run. If it
   uploads, ask before running it, or run only its build step.

## 2. Pre-flight

Fix anything here before step 3.

- Every slot passed the checks in
  [Testing and sandbox](https://levelmoment.com/docs/testing#verify): pass,
  fail and load failure each grant correctly and resume once. If there's no
  record of that, ask the developer for their test log, or run those checks
  now in a sandbox build before continuing.
- The SDK is pinned to the exact version tested, with no range, in the
  manifest and the lockfile (for Flutter and Unity, a release tag such as
  `sdk-v0.4.3`). Any tested 0.4.x is accepted for review; don't upgrade it
  unasked. Before a newer patch release, read the
  [changelog](https://levelmoment.com/docs/changelog) and rerun the sandbox
  checks if a change touches a call the game makes.
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
   sandbox-only file, and run the release build in the project that holds
   that file (for Capacitor, `npm run build && npx cap sync` before every
   archive). Then run the token check from
   [Testing and sandbox](https://levelmoment.com/docs/testing#production-build):
   it reads the token from the sandbox-only file with
   `grep -oE 'eply_sbx_[A-Za-z0-9_-]{32}'`, pipes it to
   `env LC_ALL=C grep -rlaF -f -` over the build folders, prints only file
   names, and prints "Check failed" on a match, an error or a missing token
   file. Run its positive control on a build that carries the token first.
   Then build the release configuration once without signing: for
   Capacitor, the page's simulator `xcodebuild` and
   `./gradlew assembleRelease`; for a Unity iOS export,
   `xcodebuild -sdk iphoneos CODE_SIGNING_ALLOWED=NO` on the exported
   project, because a device export doesn't link for the simulator; for an
   exported Unity Android project, `./gradlew assembleRelease`. A Unity APK
   or AAB build and the Flutter release builds need no extra native build.
   The native build check is yours; the device test isn't. The build passes no `unsafeTesting` and no `mock`.

   **Unity without a release script:** add a batch build method. Copy
   [`../templates/unity-ReleaseBuild.cs.tmpl`](../templates/unity-ReleaseBuild.cs.tmpl)
   to `Assets/Editor/LevelMomentReleaseBuild.cs` when the project has no
   build method of its own, and run it with `-buildTarget iOS` or
   `-buildTarget Android` and `-output` set outside the repository, such as
   `../builds/ios` or `../builds/game.aab` (`.aab` makes a Play bundle,
   `.apk` an installable file). It builds with Development Build off. Build
   Android with the il2cpp backend, which the token check needs. Without
   `-buildTarget`, SDK 0.4.2 and earlier leave StoreKit out of an iOS export
   from a fresh checkout. If the game's own build script sets
   Active Input Handling to Input Manager (Old), tell the developer: it
   undoes the SDK's Both setting.

2. [Store disclosures](https://levelmoment.com/docs/going-live#disclosures):
   list for the developer what the build sends: the rows of
   [Data disclosures](https://levelmoment.com/docs/data-safety) (placement ID
   and SDK version, learner credentials, questions and answers, IP address
   and user-agent, optional `customData`) plus every other SDK in the
   manifests. List the iOS privacy manifests with
   `find ios node_modules -name PrivacyInfo.xcprivacy -not -path "*/build/*"`
   (for Unity, `find ../builds/ios -name PrivacyInfo.xcprivacy` over the
   Xcode export);
   Level Moment's SDKs ship none. Check the store category: in App Store
   Connect, the app's App Information category isn't Kids; in Google Play
   Console, Target audience and content includes no ages under 13. If you
   can't see either console, ask the developer. The developer answers the
   forms.
3. [Upload a test build](https://levelmoment.com/docs/going-live#testing-track):
   TestFlight, a Google Play internal testing track, or a non-public URL, not
   store review. Its link goes in the email.
4. [Device test](https://levelmoment.com/docs/going-live#device-test): from
   the test build, on a target device with a Level Moment account that has a plan
   (a new account can start a 7-day trial), link the device, approve the
   game, and pass a break for one reward; on a second device that has never
   linked the game (a reinstall on iOS can keep the link), cancel the
   linking card and confirm the game resumes. Trial breaks earn nothing.
5. [Send the build](https://levelmoment.com/docs/going-live#review): the
   developer emails support@levelmoment.com using the page's template (To:
   line, game name, placement ID, one channel per "Ships on" line, SDK
   package and version, the test build link, and that they won't submit
   until Level Moment replies), then
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
  content: the page's token check pipes the token from its file straight
  into `grep` and prints only file names, and
  `grep -c '^VITE_LM_SANDBOX_TOKEN=' .env.local` prints only a count. Don't `cat` an
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
  developer when they upload the test build. For `SDK`, write the package
  and the pinned version: `@levelmoment/sdk-web 0.4.3`,
  `@levelmoment/sdk-react-native 0.4.3`, `levelmoment_ads sdk-v0.4.3` for
  Flutter, or `com.levelmoment.sdk sdk-v0.4.3` for Unity. Leave out the token.

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
- a token check that still prints a file name after you've fixed the files
  you can see, a positive control that finds nothing, or a release build
  that fails;
- a release script that uploads, unless they asked you to upload.

## 4. Report

Report the release command, the token check and native build result per
platform (never the token), files changed, the SDK version pinned, the
disclosure inventory, the filled email, and what the developer still has to
do, in order: the store disclosures, uploading the test build, the device
test, sending the email, and submitting after Level Moment's reply.
