// A test run stands in for a debug build, as Metro's development bundle does:
// `__DEV__` is true unless a test stubs it to false to act as a release build.
(globalThis as { __DEV__?: boolean }).__DEV__ = true;
