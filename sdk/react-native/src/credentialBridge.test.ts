// Tests for the credential handover: the keychain store, the answer to the
// page's `needCredential`, the bookkeeping that keeps the two copies in step,
// and the guarantee that no credential reaches a hosted URL.
//
// react-native-keychain is aliased to a stub under vitest (see
// vitest.config.ts), so every test here builds its own store over a fake
// keychain rather than touching the default instance.

import { afterEach, describe, expect, it, vi } from "vitest";

import { credentialInjection } from "./hostMessage.js";
import { KeychainTokenStore, credentialService } from "./tokenStore.js";
import {
  applyCredentialMessage,
  credentialResponder,
} from "./credentialBridge.js";
import { LevelMomentAd } from "./LevelMomentAd.js";
import { _registerModalHandler, type ModalShowParams } from "./modalHost.js";

// ---------------------------------------------------------------------------
// A keychain that lives in a Map, plus a switch to make every call throw. The
// throwing case is the one that matters most: a locked or missing keychain must
// leave the SDK on its pre-keychain behaviour, never fail a break.
// ---------------------------------------------------------------------------

function fakeKeychain(opts: { failing?: boolean } = {}) {
  const entries = new Map<string, string>();
  const boom = (): never => {
    throw new Error("keychain unavailable");
  };
  return {
    entries,
    getGenericPassword: vi.fn(async ({ service }: { service: string }) => {
      if (opts.failing) boom();
      const password = entries.get(service);
      return password === undefined
        ? (false as const)
        : { username: "levelmoment-device", password, service, storage: "kc" };
    }),
    setGenericPassword: vi.fn(
      async (
        _u: string,
        password: string,
        { service }: { service: string },
      ) => {
        if (opts.failing) boom();
        entries.set(service, password);
        return { service, storage: "kc" };
      },
    ),
    resetGenericPassword: vi.fn(async ({ service }: { service: string }) => {
      if (opts.failing) boom();
      return entries.delete(service);
    }),
  };
}

type FakeKeychain = ReturnType<typeof fakeKeychain>;

function storeOver(keychain: FakeKeychain): KeychainTokenStore {
  // The fake is structurally the slice of the API the store uses; the cast is
  // only about the parts of react-native-keychain's types it never touches.
  return new KeychainTokenStore(
    keychain as unknown as ConstructorParameters<typeof KeychainTokenStore>[0],
  );
}

afterEach(() => {
  _registerModalHandler(null);
  vi.clearAllMocks();
});

describe("KeychainTokenStore", () => {
  it("round-trips a credential under a placement-scoped service", async () => {
    const keychain = fakeKeychain();
    const store = storeOver(keychain);

    await store.set("game-42", "cred-abc");

    expect(keychain.entries.get(credentialService("game-42"))).toBe("cred-abc");
    await expect(store.get("game-42")).resolves.toBe("cred-abc");
  });

  it("keeps two games' credentials apart", async () => {
    // A credential is scoped to one game. Sharing one entry would have the
    // second game overwrite the first's on every launch, re-pairing forever.
    const store = storeOver(fakeKeychain());
    await store.set("game-a", "cred-a");
    await store.set("game-b", "cred-b");

    await expect(store.get("game-a")).resolves.toBe("cred-a");
    await expect(store.get("game-b")).resolves.toBe("cred-b");
  });

  it("reports an empty credential for a placement it has never seen", async () => {
    await expect(storeOver(fakeKeychain()).get("game-42")).resolves.toBe("");
  });

  it("clears one placement's credential", async () => {
    const store = storeOver(fakeKeychain());
    await store.set("game-42", "cred-abc");
    await store.clear("game-42");
    await expect(store.get("game-42")).resolves.toBe("");
  });

  it("reads a failing keychain as an empty one rather than throwing", async () => {
    // Locked keychain, a device without one, a native module that will not
    // load. None of those should cost the player a break: the hosted page's own
    // storage still holds a credential, and pairing still works.
    const store = storeOver(fakeKeychain({ failing: true }));
    await expect(store.get("game-42")).resolves.toBe("");
    await expect(store.set("game-42", "cred")).resolves.toBeUndefined();
    await expect(store.clear("game-42")).resolves.toBeUndefined();
  });

  it("ignores writes with nothing to write", async () => {
    const keychain = fakeKeychain();
    const store = storeOver(keychain);
    await store.set("", "cred");
    await store.set("game-42", "");
    expect(keychain.setGenericPassword).not.toHaveBeenCalled();
  });
});

describe("credentialResponder", () => {
  it("leads with the token the game configured", async () => {
    // An explicit token is the game's intent for THIS launch — a sandbox
    // token, or one read from a parent-portal link — and may name a different
    // learner than the stored one. The page applies its own known-bad demotion
    // to it, so leading with it here is safe.
    const keychain = fakeKeychain();
    const store = storeOver(keychain);
    await store.set("game-42", "stored-cred");

    const reply = await credentialResponder(
      "game-42",
      "explicit-cred",
      store,
    )();

    expect(reply).toEqual({ token: "explicit-cred", custody: true });
  });

  it("falls back to the keychain when the game configured nothing", async () => {
    const store = storeOver(fakeKeychain());
    await store.set("game-42", "stored-cred");

    const reply = await credentialResponder("game-42", undefined, store)();

    expect(reply).toEqual({ token: "stored-cred", custody: true });
  });

  it("answers with an empty token rather than staying silent", async () => {
    // The page waits a beat for an answer before falling back to its own
    // storage. A host holding nothing still has to say so, or every launch on
    // an unpaired device spends that whole window for nothing.
    const reply = await credentialResponder(
      "game-42",
      undefined,
      storeOver(fakeKeychain()),
    )();

    expect(reply).toEqual({ token: "", custody: true });
  });

  it("claims custody when there is a keychain to keep a copy in", async () => {
    const reply = await credentialResponder(
      "game-42",
      "cred",
      storeOver(fakeKeychain()),
    )();
    expect(reply.custody).toBe(true);
  });

  it("declines custody when the native keychain module is absent", async () => {
    // Expo Go: installing react-native-keychain adds only JavaScript, so every
    // call lands on an undefined native module. Claiming custody there would
    // have the page hand over credentials that go nowhere, while it stopped
    // treating its own storage as the record. Answering false degrades to the
    // pre-keychain model, which still works.
    const store = storeOver(fakeKeychain({ failing: true }));

    const reply = await credentialResponder("game-42", "cred", store)();

    expect(reply.custody).toBe(false);
    // The explicit token still travels — the page can use it for this launch
    // even though nothing durable will be written.
    expect(reply.token).toBe("cred");
  });

  it("probes the keychain once and reuses the answer", async () => {
    const keychain = fakeKeychain();
    const store = storeOver(keychain);
    const respond = credentialResponder("game-42", undefined, store);

    await respond();
    await respond();

    // Two responder calls read the placement twice; the availability probe is
    // the third read and must not repeat per launch.
    const probes = keychain.getGenericPassword.mock.calls.filter(([opts]) =>
      (opts as { service: string }).service.endsWith("probe"),
    );
    expect(probes).toHaveLength(1);
  });
});

describe("pairing still works without a keychain", () => {
  it("leaves the page owning the credential when custody is declined", async () => {
    // The end-to-end promise of the Expo Go fallback: the shell answers, the
    // page keeps ownership, and nothing claims a durable copy that is not there.
    const store = storeOver(fakeKeychain({ failing: true }));

    const reply = await credentialResponder("game-42", undefined, store)();
    expect(reply).toEqual({ token: "", custody: false });

    // A credentialIssued that arrives anyway is still handled without throwing;
    // it simply does not persist.
    expect(
      applyCredentialMessage(
        { type: "credentialIssued", payload: { token: "fresh" } },
        "game-42",
        store,
      ),
    ).toBe(true);
    await Promise.resolve();
    await expect(store.get("game-42")).resolves.toBe("");
  });
});

describe("applyCredentialMessage", () => {
  it("stores the credential pairing minted", async () => {
    const store = storeOver(fakeKeychain());

    expect(
      applyCredentialMessage(
        { type: "credentialIssued", payload: { token: "fresh-cred" } },
        "game-42",
        store,
      ),
    ).toBe(true);

    // The write is fire-and-forget; let the microtask queue drain.
    await Promise.resolve();
    await expect(store.get("game-42")).resolves.toBe("fresh-cred");
  });

  it("clears the credential the server refused", async () => {
    const store = storeOver(fakeKeychain());
    await store.set("game-42", "dead-cred");

    expect(
      applyCredentialMessage({ type: "credentialInvalid" }, "game-42", store),
    ).toBe(true);

    await Promise.resolve();
    await expect(store.get("game-42")).resolves.toBe("");
  });

  it("leaves other messages alone", () => {
    const keychain = fakeKeychain();
    const store = storeOver(keychain);

    expect(applyCredentialMessage({ type: "ready" }, "game-42", store)).toBe(
      false,
    );
    expect(
      applyCredentialMessage({ type: "dismissed" }, "game-42", store),
    ).toBe(false);
    expect(keychain.setGenericPassword).not.toHaveBeenCalled();
    expect(keychain.resetGenericPassword).not.toHaveBeenCalled();
  });
});

describe("credentialInjection", () => {
  it("calls the page's delivery hook with the credential", () => {
    const js = credentialInjection({ token: "cred-abc", custody: true });
    expect(js).toContain("window.__levelMomentDeliverCredential");
    expect(js).toContain("cred-abc");
    expect(js).toContain("custody");
    // iOS warns on a non-primitive injectJavaScript result.
    expect(js.trimEnd().endsWith("true;")).toBe(true);
  });

  it("cannot be broken out of by a token full of quotes and backslashes", () => {
    // A token is an opaque server string, but it arrives from outside this
    // function. Splicing it raw would make a quote in it executable code inside
    // the hosted page. Evaluating the argument the way the page's runtime would
    // is the only assertion that actually proves the escaping.
    const hostile = '");alert(1);//\\"\n ';
    const js = credentialInjection({ token: hostile, custody: false });

    const deliver = vi.fn();
    new Function("window", js)({
      location: { origin: "https://levelmoment.com" },
      __levelMomentDeliverCredential: deliver,
    });
    expect(JSON.parse(deliver.mock.calls[0]![0] as string)).toEqual({
      token: hostile,
      custody: false,
      protocolVersion: 1,
      sdkVersion: "0.2.0",
    });

    const wrongOrigin = vi.fn();
    new Function("window", js)({
      location: { origin: "https://evil.example" },
      __levelMomentDeliverCredential: wrongOrigin,
    });
    expect(wrongOrigin).not.toHaveBeenCalled();
  });
});

describe("LevelMomentAd — credential handover", () => {
  const OPTIONS = {
    breakUrl: "https://app.levelmoment.com/break",
    apiUrl: "https://api.levelmoment.com",
    unsafeTesting: { token: "sandbox-cred" },
  };

  function mountFakeHost(): { current: ModalShowParams | null } {
    const captured: { current: ModalShowParams | null } = { current: null };
    _registerModalHandler((params) => {
      captured.current = params;
      return { close: () => {} };
    });
    return captured;
  }

  it("puts no credential on the break URL", () => {
    const host = mountFakeHost();
    const ad = LevelMomentAd.createForAdRequest("game-42", OPTIONS);
    ad.load();
    ad.show();

    const url = new URL(host.current!.url);
    expect(url.searchParams.get("placementId")).toBe("game-42");
    expect(url.searchParams.get("apiUrl")).toBe("https://api.levelmoment.com");
    expect(url.searchParams.has("token")).toBe(false);
    expect(host.current!.url).not.toContain("sandbox-cred");
  });

  it("announces openExternal support — an old shell that cannot open a browser must not be offered the button", () => {
    const host = mountFakeHost();
    const ad = LevelMomentAd.createForAdRequest("game-42", OPTIONS);
    ad.load();
    ad.show();

    const url = new URL(host.current!.url);
    expect(url.searchParams.get("caps")).toBe("openExternal");
  });

  it("offers the host an answer to needCredential", async () => {
    const host = mountFakeHost();
    const ad = LevelMomentAd.createForAdRequest("game-42", OPTIONS);
    ad.load();
    ad.show();

    await expect(host.current!.onNeedCredential!()).resolves.toEqual({
      token: "sandbox-cred",
      custody: false,
      customData: undefined,
    });
  });

  it("does not close the break on a credential message", () => {
    // credentialIssued arrives mid-break, right after pairing. Treating it as
    // terminal would drop the player out of the break they just unlocked.
    const host = mountFakeHost();
    const ad = LevelMomentAd.createForAdRequest("game-42", OPTIONS);
    const closed = vi.fn();
    ad.addAdEventListener("closed", closed);
    ad.load();
    ad.show();

    host.current!.onMessage({
      type: "credentialIssued",
      payload: { token: "fresh" },
    });
    host.current!.onMessage({ type: "credentialInvalid" });
    host.current!.onMessage({ type: "needCredential" });

    expect(closed).not.toHaveBeenCalled();
  });
});
