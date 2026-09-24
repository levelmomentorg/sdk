// LevelMoment Unity SDK — native macOS WebView host.
//
// gree/unity-webview renders a macOS standalone player's WebView in a hidden
// window parked 10,000 points off-screen and paints it into the game as a
// texture, replaying Unity's mouse events into the page. The page's elements
// reach the accessibility API only as children of that hidden window, with
// off-screen frames, while the visible game window exposes nothing. VoiceOver's
// cursor, Voice Control's overlays, and any automation that acts at an
// element's position therefore miss, and pointer input depends on a coordinate
// remap. This host instead adds a real WKWebView as a subview of the player
// window's content view, so the page's elements sit in the visible window with
// their real frames and real clicks and keystrokes reach them.
//
// C ABI only; the C# side is Runtime/MacOS/MacNativeWebView.cs. Page messages
// are queued here and drained by C# once per frame, so no page event ever
// re-enters managed code outside Unity's player loop. Build with ./build.sh.

#import <Cocoa/Cocoa.h>
#import <WebKit/WebKit.h>

static NSString *const kBridgeName = @"unityControl";

// The origin fence, as a plain function so build.sh can test it without a
// WebView. `origin` is scheme://host[:port] with no trailing slash; `url` is
// the absolute string WebKit hands the navigation delegate (already
// normalized: lowercase scheme and host, no tabs or newlines). A URL is
// allowed only when it IS the origin or continues it with a path, query, or
// fragment, so `https://levelmoment.com.evil.tld` and `:30001` never match.
BOOL LevelMomentOriginAllows(NSString *origin, NSString *url)
{
    if (origin.length == 0 || url == nil)
        return NO;
    if ([url isEqualToString:@"about:blank"])
        return YES;
    if ([url isEqualToString:origin])
        return YES;
    if (![url hasPrefix:origin] || url.length <= origin.length)
        return NO;
    unichar next = [url characterAtIndex:origin.length];
    return next == '/' || next == '?' || next == '#';
}

@interface LevelMomentWebViewHost : NSObject <WKScriptMessageHandler, WKNavigationDelegate, WKUIDelegate>
@property(nonatomic, strong) WKWebView *webView;
@property(nonatomic, strong) NSMutableArray<NSString *> *queue;
@property(nonatomic, copy) NSString *origin;
@property(nonatomic, weak) NSResponder *previousResponder;
@property(nonatomic) BOOL tookFocus;
@end

// WKUserContentController retains its script message handlers, and the host
// retains the WebView, so registering the host directly would be a retain
// cycle that outlives Close(). The trampoline holds the host weakly.
@interface LevelMomentWeakMessageHandler : NSObject <WKScriptMessageHandler>
@property(nonatomic, weak) id<WKScriptMessageHandler> target;
@end

@implementation LevelMomentWeakMessageHandler
- (void)userContentController:(WKUserContentController *)controller
      didReceiveScriptMessage:(WKScriptMessage *)message
{
    [self.target userContentController:controller didReceiveScriptMessage:message];
}
@end

@implementation LevelMomentWebViewHost

- (instancetype)initWithOrigin:(NSString *)origin
{
    self = [super init];
    if (self == nil)
        return nil;
    _origin = [origin copy];
    _queue = [NSMutableArray array];

    WKWebViewConfiguration *config = [[WKWebViewConfiguration alloc] init];
    WKUserContentController *content = [[WKUserContentController alloc] init];
    LevelMomentWeakMessageHandler *handler = [[LevelMomentWeakMessageHandler alloc] init];
    handler.target = self;
    [content addScriptMessageHandler:handler name:kBridgeName];
    // The same page-side channel gree exposes, so /break's postToHost needs no
    // macOS branch: window.Unity.call(json).
    NSString *shim =
        @"window.Unity = { call: function (msg) {"
         " window.webkit.messageHandlers.unityControl.postMessage(String(msg)); } };";
    [content addUserScript:[[WKUserScript alloc]
                               initWithSource:shim
                                injectionTime:WKUserScriptInjectionTimeAtDocumentStart
                             forMainFrameOnly:YES]];
    config.userContentController = content;

    _webView = [[WKWebView alloc] initWithFrame:NSZeroRect configuration:config];
    _webView.navigationDelegate = self;
    _webView.UIDelegate = self;
    _webView.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
    _webView.hidden = YES;
    return self;
}

- (NSWindow *)playerWindow
{
    NSWindow *window = NSApp.mainWindow ?: NSApp.keyWindow;
    if (window != nil)
        return window;
    for (NSWindow *candidate in NSApp.windows) {
        if (candidate.isVisible && candidate.contentView != nil)
            return candidate;
    }
    return NSApp.windows.firstObject;
}

- (BOOL)attach
{
    NSWindow *window = [self playerWindow];
    NSView *host = window.contentView;
    if (host == nil)
        return NO;
    self.webView.frame = host.bounds;
    [host addSubview:self.webView positioned:NSWindowAbove relativeTo:nil];
    return YES;
}

- (void)setVisible:(BOOL)visible
{
    NSWindow *window = self.webView.window;
    if (visible && self.webView.hidden) {
        self.previousResponder = window.firstResponder;
        self.tookFocus = YES;
        self.webView.hidden = NO;
        // Keyboard focus follows the page while it is on screen, so Tab,
        // Space, Return, and typed answers reach it rather than the game.
        [window makeFirstResponder:self.webView];
    } else if (!visible && !self.webView.hidden) {
        self.webView.hidden = YES;
        [self restoreResponder];
    }
}

- (void)restoreResponder
{
    // A view loaded hidden (the headless credential check) never took focus,
    // so it has nothing to give back and must not move the game's.
    if (!self.tookFocus)
        return;
    self.tookFocus = NO;
    NSWindow *window = self.webView.window;
    if (window == nil)
        return;
    NSResponder *previous = self.previousResponder;
    [window makeFirstResponder:previous ?: window.contentView];
    self.previousResponder = nil;
}

- (void)destroy
{
    [self restoreResponder];
    [self.webView stopLoading];
    self.webView.navigationDelegate = nil;
    self.webView.UIDelegate = nil;
    [self.webView.configuration.userContentController removeScriptMessageHandlerForName:kBridgeName];
    [self.webView removeFromSuperview];
    self.webView = nil;
    [self.queue removeAllObjects];
}

- (void)enqueue:(NSString *)kind text:(NSString *)text
{
    [self.queue addObject:[kind stringByAppendingString:text ?: @""]];
}

// Only the hosted origin may load, including redirects and subframes. Parent
// approval links leave through the page's explicit openExternal message, never
// through a navigation, so everything else is refused.
- (BOOL)isAllowed:(NSURL *)url
{
    return LevelMomentOriginAllows(self.origin, url.absoluteString);
}

- (void)userContentController:(WKUserContentController *)controller
      didReceiveScriptMessage:(WKScriptMessage *)message
{
    if (![message.name isEqualToString:kBridgeName])
        return;
    // A subframe on the hosted origin could call the same handler; only the
    // top-level page speaks for the break.
    if (!message.frameInfo.isMainFrame)
        return;
    if ([message.body isKindOfClass:[NSString class]])
        [self enqueue:@"M" text:(NSString *)message.body];
}

- (void)webView:(WKWebView *)webView
    decidePolicyForNavigationAction:(WKNavigationAction *)action
                    decisionHandler:(void (^)(WKNavigationActionPolicy))decisionHandler
{
    BOOL allowed = [self isAllowed:action.request.URL];
    // A refused top-level load (an off-origin redirect, say) would otherwise
    // leave a blank view over the game until the load watchdog fires. The
    // cancellation error itself is suppressed below, so report it here.
    if (!allowed && action.targetFrame.isMainFrame)
        [self enqueue:@"E" text:@"The break page tried to leave its allowed origin."];
    decisionHandler(allowed ? WKNavigationActionPolicyAllow : WKNavigationActionPolicyCancel);
}

- (WKWebView *)webView:(WKWebView *)webView
    createWebViewWithConfiguration:(WKWebViewConfiguration *)configuration
               forNavigationAction:(WKNavigationAction *)action
                    windowFeatures:(WKWindowFeatures *)features
{
    // No popups: a new window would escape the origin fence above.
    return nil;
}

- (void)reportFailure:(NSError *)error
{
    // Cancelled loads are our own policy refusals or a superseded load.
    if ([error.domain isEqualToString:NSURLErrorDomain] && error.code == NSURLErrorCancelled)
        return;
    if ([error.domain isEqualToString:@"WebKitErrorDomain"] && error.code == 102)
        return;
    [self enqueue:@"E" text:error.localizedDescription];
}

- (void)webView:(WKWebView *)webView
    didFailProvisionalNavigation:(WKNavigation *)navigation
                       withError:(NSError *)error
{
    [self reportFailure:error];
}

- (void)webView:(WKWebView *)webView
    didFailNavigation:(WKNavigation *)navigation
            withError:(NSError *)error
{
    [self reportFailure:error];
}

- (void)webViewWebContentProcessDidTerminate:(WKWebView *)webView
{
    [self enqueue:@"E" text:@"The WebView content process terminated."];
}

@end

// --- C ABI -----------------------------------------------------------------

static NSString *LMString(const char *utf8)
{
    return utf8 != NULL ? [NSString stringWithUTF8String:utf8] : @"";
}

__attribute__((visibility("default")))
int LevelMomentWebView_Version(void)
{
    return 1;
}

__attribute__((visibility("default")))
void *LevelMomentWebView_Create(const char *origin)
{
    LevelMomentWebViewHost *host = [[LevelMomentWebViewHost alloc] initWithOrigin:LMString(origin)];
    if (host == nil || ![host attach])
        return NULL;
    return (__bridge_retained void *)host;
}

__attribute__((visibility("default")))
void LevelMomentWebView_Load(void *instance, const char *url)
{
    if (instance == NULL)
        return;
    LevelMomentWebViewHost *host = (__bridge LevelMomentWebViewHost *)instance;
    NSURL *target = [NSURL URLWithString:LMString(url)];
    if (target == nil || ![host isAllowed:target]) {
        [host enqueue:@"E" text:@"The break URL is not on the allowed origin."];
        return;
    }
    [host.webView loadRequest:[NSURLRequest requestWithURL:target]];
}

__attribute__((visibility("default")))
void LevelMomentWebView_SetVisible(void *instance, int visible)
{
    if (instance == NULL)
        return;
    [(__bridge LevelMomentWebViewHost *)instance setVisible:visible != 0];
}

__attribute__((visibility("default")))
void LevelMomentWebView_EvaluateJS(void *instance, const char *js)
{
    if (instance == NULL)
        return;
    LevelMomentWebViewHost *host = (__bridge LevelMomentWebViewHost *)instance;
    [host.webView evaluateJavaScript:LMString(js) completionHandler:nil];
}

// Returns the next queued entry as a malloc'd UTF-8 string ("M" + message JSON
// or "E" + error text), or NULL when the queue is empty. The caller frees it
// with LevelMomentWebView_Free.
__attribute__((visibility("default")))
char *LevelMomentWebView_Poll(void *instance)
{
    if (instance == NULL)
        return NULL;
    LevelMomentWebViewHost *host = (__bridge LevelMomentWebViewHost *)instance;
    if (host.queue.count == 0)
        return NULL;
    NSString *next = host.queue.firstObject;
    [host.queue removeObjectAtIndex:0];
    return strdup(next.UTF8String ?: "");
}

__attribute__((visibility("default")))
void LevelMomentWebView_Free(char *text)
{
    free(text);
}

__attribute__((visibility("default")))
void LevelMomentWebView_Destroy(void *instance)
{
    if (instance == NULL)
        return;
    LevelMomentWebViewHost *host = (__bridge_transfer LevelMomentWebViewHost *)instance;
    [host destroy];
}
