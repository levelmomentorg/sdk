// Tests for LevelMomentOriginAllows, the navigation fence in
// LevelMomentWebView.m. build.sh compiles and runs this before it builds the
// bundle, and stops on any failure. It includes the source file so the fence
// under test is the one that ships.

#include "LevelMomentWebView.m"

static int failures = 0;

static void expect(BOOL allowed, NSString *origin, NSString *url)
{
    if (LevelMomentOriginAllows(origin, url) != allowed) {
        failures++;
        fprintf(stderr, "FAIL: %s should be %s for origin %s\n", url.UTF8String,
                allowed ? "allowed" : "refused", origin.UTF8String);
    }
}

int main(void)
{
    @autoreleasepool {
        NSString *prod = @"https://levelmoment.com";
        NSString *local = @"http://localhost:3000";

        expect(YES, prod, @"https://levelmoment.com");
        expect(YES, prod, @"https://levelmoment.com/break?placementId=pl_1");
        expect(YES, prod, @"https://levelmoment.com?x=1");
        expect(YES, prod, @"https://levelmoment.com#frag");
        expect(YES, prod, @"about:blank");
        expect(YES, local, @"http://localhost:3000/break");

        // Hosts and ports that merely start with the origin.
        expect(NO, prod, @"https://levelmoment.com.evil.tld/break");
        expect(NO, prod, @"https://levelmoment.comevil.tld/");
        expect(NO, local, @"http://localhost:30001/break");
        expect(NO, prod, @"https://levelmoment.com:8443/break");
        // Userinfo that puts the real host after an @.
        expect(NO, prod, @"https://levelmoment.com@evil.tld/");
        // Other schemes and pseudo-URLs.
        expect(NO, prod, @"http://levelmoment.com/break");
        expect(NO, prod, @"about:srcdoc");
        expect(NO, prod, @"data:text/html,<p>x</p>");
        expect(NO, prod, @"blob:https://levelmoment.com/1234");
        expect(NO, prod, @"javascript:alert(1)");
        expect(NO, prod, @"https://evil.tld/https://levelmoment.com/");
        // Degenerate inputs.
        expect(NO, @"", @"https://levelmoment.com/");
        expect(NO, prod, nil);
    }
    if (failures > 0) {
        fprintf(stderr, "%d origin fence case(s) failed\n", failures);
        return 1;
    }
    printf("origin fence: all cases pass\n");
    return 0;
}
