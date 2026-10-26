// A minimal DOM for the SDK's tests (vitest runs with environment `node`).
// Shared by every test that mounts SDK chrome: an element records its
// children, attributes, text, and click listeners, and that is all the SDK's
// chrome code touches.

export interface FakeElement {
  tagName: string;
  src: string;
  style: { cssText: string };
  parentNode: FakeElement | null;
  attrs: Record<string, string>;
  children: FakeElement[];
  textContent: string;
  focused: boolean;
  listeners: Record<string, Array<() => void>>;
  contentWindow: { frame: symbol; postMessage: (...args: unknown[]) => void };
  setAttribute: (k: string, v: string) => void;
  appendChild: (child: FakeElement) => void;
  removeChild: (child: FakeElement) => void;
  addEventListener: (type: string, fn: () => void) => void;
  focus: () => void;
}

export function fakeElement(
  tag: string,
  postMessage: (...args: unknown[]) => void = () => {},
): FakeElement {
  const node: FakeElement = {
    tagName: tag.toUpperCase(),
    src: "",
    style: { cssText: "" },
    parentNode: null,
    attrs: {},
    children: [],
    textContent: "",
    focused: false,
    listeners: {},
    contentWindow: { frame: Symbol("contentWindow"), postMessage },
    setAttribute(k, v) {
      node.attrs[k] = v;
    },
    appendChild(child) {
      child.parentNode = node;
      node.children.push(child);
    },
    removeChild(child) {
      node.children = node.children.filter((c) => c !== child);
      child.parentNode = null;
    },
    addEventListener(type, fn) {
      (node.listeners[type] ??= []).push(fn);
    },
    focus() {
      node.focused = true;
    },
  };
  return node;
}

/** Every descendant of `root` (depth first), `root` excluded. */
export function descendants(root: FakeElement): FakeElement[] {
  return root.children.flatMap((c) => [c, ...descendants(c)]);
}

/** Fire the click listeners on the first `button` under `root`. */
export function clickButton(root: FakeElement): void {
  const button = descendants(root).find((n) => n.tagName === "BUTTON");
  if (!button) throw new Error("no button");
  for (const fn of button.listeners.click ?? []) fn();
}
