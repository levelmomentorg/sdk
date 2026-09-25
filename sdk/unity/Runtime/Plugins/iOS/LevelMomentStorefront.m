// ---------------------------------------------------------------------------
// LevelMoment Unity SDK — iOS storefront plugin.
//
// Reads SKPaymentQueue.defaultQueue.storefront.countryCode (iOS 13+)
// synchronously, for LevelMoment.NativeStorefront to call once at SDK
// initialize and cache. Never called from the `needCredential` reply path —
// the hosted page waits only 500ms for that reply.
//
// See sdk/unity/Runtime/NativeStorefront.cs.
//
// Links against StoreKit.framework — added to the generated Xcode project by
// Editor/LevelMomentIOSPostProcessBuild.cs, since a plain .m plugin file does
// not pull in a non-default framework on its own.
// ---------------------------------------------------------------------------

#import <StoreKit/StoreKit.h>

const char *_levelMomentStorefrontCountryCode(void) {
  if (@available(iOS 13.0, *)) {
    // SKPaymentQueue/SKStorefront are deprecated in favor of StoreKit 2's
    // Storefront.current, which is async-only and cannot serve this
    // synchronous, cache-at-Initialize() read. StoreKit 1 stays correct here.
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    SKStorefront *storefront = [SKPaymentQueue defaultQueue].storefront;
#pragma clang diagnostic pop
    NSString *countryCode = storefront.countryCode;
    if (countryCode == nil) {
      return NULL;
    }

    // A static buffer, not strdup: the caller takes no ownership and frees
    // nothing, matching the read-once-and-cache contract on the C# side. A
    // storefront code is at most a handful of ASCII characters, so this
    // never truncates a real value.
    static char buffer[16];
    const char *utf8 = [countryCode UTF8String];
    strncpy(buffer, utf8, sizeof(buffer) - 1);
    buffer[sizeof(buffer) - 1] = '\0';
    return buffer;
  }
  return NULL;
}
