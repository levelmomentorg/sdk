// SDK-owned chrome: the loading layer over a hosted frame that has not posted
// `ready` yet, and the full-screen "No internet connection" message.
//
// Both sit at the top z-index over whatever the game draws, so they paint
// their own opaque background instead of relying on the game's colours, and
// pick a light or dark palette from the player's system setting.

/** The highest z-index a browser honours. Hosted frames use it too. */
export const TOP_Z_INDEX = 2147483647;

interface Palette {
  background: string;
  text: string;
  muted: string;
  buttonBackground: string;
  buttonText: string;
  closeBackground: string;
}

const DARK: Palette = {
  background: "#0b0618",
  text: "#f8fafc",
  muted: "#cbd5e1",
  buttonBackground: "#f8fafc",
  buttonText: "#0b0618",
  closeBackground: "rgba(248,250,252,0.16)",
};

const LIGHT: Palette = {
  background: "#ffffff",
  text: "#0f172a",
  muted: "#334155",
  buttonBackground: "#0f172a",
  buttonText: "#ffffff",
  closeBackground: "rgba(15,23,42,0.08)",
};

function palette(): Palette {
  try {
    if (
      typeof window !== "undefined" &&
      window.matchMedia?.("(prefers-color-scheme: light)").matches
    ) {
      return LIGHT;
    }
  } catch {
    /* No media query support: keep the dark palette. */
  }
  return DARK;
}

const FONT =
  "font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;";

function layerStyle(p: Palette): string {
  return (
    `position:fixed;inset:0;width:100%;height:100%;z-index:${TOP_Z_INDEX};` +
    `background:${p.background};color:${p.text};${FONT}` +
    "display:flex;flex-direction:column;align-items:center;justify-content:center;" +
    "box-sizing:border-box;text-align:center;" +
    "padding:calc(env(safe-area-inset-top,0px) + 24px) " +
    "calc(env(safe-area-inset-right,0px) + 24px) " +
    "calc(env(safe-area-inset-bottom,0px) + 24px) " +
    "calc(env(safe-area-inset-left,0px) + 24px);"
  );
}

function el(tag: string, cssText: string): HTMLElement {
  const node = document.createElement(tag);
  node.style.cssText = cssText;
  return node;
}

function detach(node: HTMLElement | null): void {
  node?.parentNode?.removeChild(node);
}

/** A mounted piece of chrome. `remove()` is idempotent. */
export interface MountedChrome {
  readonly element: HTMLElement;
  remove(): void;
}

/**
 * Cover a hosted frame while it loads, so the player sees a spinner and a way
 * out instead of a blank frame or the browser's own error page. Mount it after
 * the frame: at the same z-index, the later element paints on top.
 */
export function mountLoadingLayer(onClose: () => void): MountedChrome {
  const p = palette();
  const layer = el("div", layerStyle(p));
  layer.setAttribute("data-levelmoment", "loading");
  layer.setAttribute("role", "status");
  layer.setAttribute("aria-label", "Loading");

  const spinner = el(
    "div",
    "width:40px;height:40px;border-radius:50%;box-sizing:border-box;" +
      `border:4px solid ${p.closeBackground};border-top-color:${p.text};`,
  );
  spinner.setAttribute("aria-hidden", "true");
  // Web Animations rather than a stylesheet: nothing to inject into the
  // game's document, and a browser without it just shows a still ring.
  spinner.animate?.(
    [{ transform: "rotate(0deg)" }, { transform: "rotate(360deg)" }],
    { duration: 900, iterations: Infinity },
  );
  layer.appendChild(spinner);

  const close = el(
    "button",
    "position:absolute;top:calc(env(safe-area-inset-top,0px) + 12px);" +
      "right:calc(env(safe-area-inset-right,0px) + 12px);" +
      "width:44px;height:44px;border:0;border-radius:50%;cursor:pointer;" +
      `background:${p.closeBackground};color:${p.text};${FONT}` +
      "font-size:28px;line-height:44px;padding:0;",
  );
  close.setAttribute("type", "button");
  close.setAttribute("aria-label", "Close");
  close.textContent = "×";
  close.addEventListener("click", onClose);
  layer.appendChild(close);

  document.body.appendChild(layer);
  let removed = false;
  return {
    element: layer,
    remove() {
      if (removed) return;
      removed = true;
      detach(layer);
    },
  };
}

/** The copy on the no-connection message. */
export function noConnectionCopy(gameName?: string): {
  heading: string;
  body: string;
  button: string;
} {
  const name = gameName?.trim() || "This game";
  return {
    heading: "No internet connection",
    body: `${name} needs an internet connection. Turn on Wi-Fi or mobile data, then try again.`,
    button: "Try again",
  };
}

/**
 * The full-screen message shown while the device is offline. It blocks every
 * tap on the game behind it; `onRetry` runs when the player presses Try again.
 */
export function mountNoConnection(
  gameName: string | undefined,
  onRetry: () => void,
): MountedChrome {
  const p = palette();
  const copy = noConnectionCopy(gameName);
  const layer = el("div", layerStyle(p) + "pointer-events:auto;");
  layer.setAttribute("data-levelmoment", "no-connection");
  layer.setAttribute("role", "alertdialog");
  layer.setAttribute("aria-modal", "true");
  layer.setAttribute("aria-labelledby", "levelmoment-no-connection-heading");
  layer.setAttribute("aria-describedby", "levelmoment-no-connection-body");

  const heading = el(
    "h2",
    "margin:0 0 12px;font-size:24px;line-height:1.25;font-weight:700;",
  );
  heading.setAttribute("id", "levelmoment-no-connection-heading");
  heading.textContent = copy.heading;
  layer.appendChild(heading);

  const body = el(
    "p",
    `margin:0 0 24px;max-width:28em;font-size:16px;line-height:1.5;color:${p.muted};`,
  );
  body.setAttribute("id", "levelmoment-no-connection-body");
  body.textContent = copy.body;
  layer.appendChild(body);

  const button = el(
    "button",
    "min-width:160px;min-height:48px;padding:12px 24px;border:0;border-radius:999px;" +
      `background:${p.buttonBackground};color:${p.buttonText};${FONT}` +
      "font-size:16px;font-weight:600;cursor:pointer;",
  );
  button.setAttribute("type", "button");
  button.textContent = copy.button;
  button.addEventListener("click", onRetry);
  layer.appendChild(button);

  (document.body ?? document.documentElement).appendChild(layer);
  button.focus?.();
  let removed = false;
  return {
    element: layer,
    remove() {
      if (removed) return;
      removed = true;
      detach(layer);
    },
  };
}
