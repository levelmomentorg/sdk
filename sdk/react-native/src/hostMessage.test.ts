// The bridge message that sends a parent out to the system browser.
//
// The decision — open it, or ignore it — is pure so it can be tested without
// react-native. LevelMomentAdModal does nothing but pass its result to
// Linking.openURL.

import { describe, expect, it } from "vitest";
import { externalUrlToOpen, isTerminal, type HostMessage } from "./hostMessage";

const HOSTED = "https://app.levelmoment.com/break?placementId=game-42";

const open = (url: string): HostMessage => ({
  type: "openExternal",
  payload: { url },
});

describe("externalUrlToOpen", () => {
  it("returns an approval URL on the hosted page's own origin", () => {
    expect(
      externalUrlToOpen(
        open("https://app.levelmoment.com/link?code=AB12CD"),
        HOSTED,
      ),
    ).toBe("https://app.levelmoment.com/link?code=AB12CD");
  });

  it("refuses another origin, however plausible", () => {
    // The bridge belongs to whatever the WebView is showing. A page that took
    // it somewhere else could otherwise full-screen a sign-in prompt in the
    // device's own browser.
    for (const url of [
      "https://evil.example.com/link",
      "https://app.levelmoment.com.evil.example/link",
      "https://levelmoment.com/link", // different host
      "https://app.levelmoment.com:8443/link", // different port
    ]) {
      expect(externalUrlToOpen(open(url), HOSTED)).toBeNull();
    }
  });

  it("refuses http when the game is configured against https", () => {
    expect(
      externalUrlToOpen(open("http://app.levelmoment.com/link"), HOSTED),
    ).toBeNull();
  });

  it("allows http when breakUrl is itself http (local development)", () => {
    const localHosted = "http://localhost:3000/break";
    expect(
      externalUrlToOpen(open("http://localhost:3000/link"), localHosted),
    ).toBe("http://localhost:3000/link");
    expect(
      externalUrlToOpen(open("http://localhost:4000/link"), localHosted),
    ).toBeNull();
  });

  it("refuses anything that is not a URL at all", () => {
    // Linking.openURL hands a custom scheme to whichever app claims it.
    expect(externalUrlToOpen(open("javascript:alert(1)"), HOSTED)).toBeNull();
    expect(
      externalUrlToOpen(open("someapp://pay?amount=1"), HOSTED),
    ).toBeNull();
    expect(externalUrlToOpen(open("file:///etc/passwd"), HOSTED)).toBeNull();
    expect(externalUrlToOpen(open("not a url"), HOSTED)).toBeNull();
    expect(externalUrlToOpen(open(""), HOSTED)).toBeNull();
  });

  it("ignores every other message", () => {
    expect(externalUrlToOpen({ type: "ready" }, HOSTED)).toBeNull();
    expect(externalUrlToOpen({ type: "dismissed" }, HOSTED)).toBeNull();
  });

  it("is not terminal — the break stays open and keeps polling", () => {
    expect(isTerminal(open("https://app.levelmoment.com/link"))).toBe(false);
  });
});
