// Stands in for `react-native-keychain` under vitest.
//
// The real package ships Flow-annotated JavaScript that a plain Node transform
// cannot parse, and it needs a native module that does not exist off-device.
// The unit tests never exercise it: KeychainTokenStore takes its keychain as a
// constructor argument, so a test builds one over a fake. This stub only has to
// keep the import from exploding.
//
// It deliberately reports "nothing stored" rather than throwing: if a test path
// ever reaches the default store by accident, it should read as an empty
// keychain — the case the SDK is designed to survive — not as a crash.

export function getGenericPassword(): Promise<false> {
  return Promise.resolve(false);
}

export function setGenericPassword(): Promise<false> {
  return Promise.resolve(false);
}

export function resetGenericPassword(): Promise<boolean> {
  return Promise.resolve(false);
}
