// EditMode tests for platform + storefront reporting: the credential-bridge reply JSON, the cache-once
// behaviour at LevelMomentAds.Initialize(), and that a native read never
// happens from the `needCredential` reply path.
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using System;
using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class StorefrontTests
    {
        // ---- Fakes ------------------------------------------------------------

        private class FakeStorefrontProvider : IStorefrontProvider
        {
            private readonly string _platform;
            private readonly string _storefront;

            public int ReadCount;

            public FakeStorefrontProvider(string platform, string storefront)
            {
                _platform = platform;
                _storefront = storefront;
            }

            public string Platform
            {
                get { return _platform; }
            }

            public string ReadStorefront()
            {
                ReadCount++;
                return _storefront;
            }
        }

#pragma warning disable 0067
        private class ScriptableFakeWebView : ILevelMomentScriptableWebView
        {
            public event Action<string> OnMessage;
            public event Action OnClosed;

            public int EvaluateJSCount;
            public string LastJS;

            public void Open(string url) { }
            public void Close() { }

            public void EvaluateJS(string js)
            {
                EvaluateJSCount++;
                LastJS = js;
            }

            public void EmitMessage(string raw)
            {
                if (OnMessage != null)
                    OnMessage(raw);
            }
        }
#pragma warning restore 0067

        [SetUp]
        public void SetUp()
        {
            LevelMomentAds.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            LevelMomentWebViewRegistry.Reset();
            LevelMomentRuntime.ResetForTests();
            LevelMomentAds.ResetForTests();
        }

        // ---- CredentialBridge.BuildInjection ----------------------------------

        [Test]
        public void BuildInjection_NoPlatformOrStorefront_BothNull()
        {
            var js = CredentialBridge.BuildInjection("tok-1");
            StringAssert.Contains("\\\"platform\\\":null", js);
            StringAssert.Contains("\\\"storefront\\\":null", js);
        }

        [Test]
        public void BuildInjection_CarriesPlatformAndStorefront()
        {
            var js = CredentialBridge.BuildInjection(
                "tok-1", platform: "ios", storefront: "USA");
            StringAssert.Contains("\\\"platform\\\":\\\"ios\\\"", js);
            StringAssert.Contains("\\\"storefront\\\":\\\"USA\\\"", js);
        }

        [Test]
        public void BuildInjection_AndroidPlatform_StorefrontStillNullable()
        {
            var js = CredentialBridge.BuildInjection(
                "tok-1", platform: "android", storefront: null);
            StringAssert.Contains("\\\"platform\\\":\\\"android\\\"", js);
            StringAssert.Contains("\\\"storefront\\\":null", js);
        }

        // ---- DeviceStorefrontProvider / NativeStorefront (no Unity iOS/Android
        // define in this harness — the else branch and the non-iOS #else are
        // what run here and on macOS/editor/standalone in the real player) ----

        [Test]
        public void DeviceStorefrontProvider_OutsideIOSOrAndroid_PlatformIsNull()
        {
            var provider = new DeviceStorefrontProvider();
            Assert.IsNull(provider.Platform);
        }

        [Test]
        public void DeviceStorefrontProvider_OutsideIOS_StorefrontIsNull()
        {
            var provider = new DeviceStorefrontProvider();
            Assert.IsNull(provider.ReadStorefront());
        }

        [Test]
        public void NativeStorefront_OutsideADeviceIOSPlayer_ReturnsNull()
        {
            Assert.IsNull(NativeStorefront.ReadCountryCode());
        }

        // ---- LevelMomentAds.Initialize caches once ----------------------------

        [Test]
        public void Initialize_IOS_CachesPlatformAndStorefront()
        {
            LevelMomentAds.StorefrontProvider = new FakeStorefrontProvider("ios", "USA");
            LevelMomentAds.Initialize(new LevelMomentConfig());

            Assert.AreEqual("ios", LevelMomentAds.Platform);
            Assert.AreEqual("USA", LevelMomentAds.Storefront);
        }

        [Test]
        public void Initialize_IOS_NoStorefrontAvailable_CachesNull()
        {
            LevelMomentAds.StorefrontProvider = new FakeStorefrontProvider("ios", null);
            LevelMomentAds.Initialize(new LevelMomentConfig());

            Assert.AreEqual("ios", LevelMomentAds.Platform);
            Assert.IsNull(LevelMomentAds.Storefront);
        }

        // Android never reads a storefront (no Play Billing dependency, by
        // design — every Google Play row resolves the same store policy).
        [Test]
        public void Initialize_Android_NeverReadsStorefront()
        {
            var provider = new FakeStorefrontProvider("android", "should-never-be-cached");
            LevelMomentAds.StorefrontProvider = provider;
            LevelMomentAds.Initialize(new LevelMomentConfig());

            Assert.AreEqual("android", LevelMomentAds.Platform);
            Assert.IsNull(LevelMomentAds.Storefront);
            Assert.AreEqual(0, provider.ReadCount);
        }

        [Test]
        public void Initialize_NeitherIOSNorAndroid_BothNull()
        {
            LevelMomentAds.StorefrontProvider = new FakeStorefrontProvider(null, "unused");
            LevelMomentAds.Initialize(new LevelMomentConfig());

            Assert.IsNull(LevelMomentAds.Platform);
            Assert.IsNull(LevelMomentAds.Storefront);
        }

        [Test]
        public void Initialize_ReadsStorefrontExactlyOnce()
        {
            var provider = new FakeStorefrontProvider("ios", "USA");
            LevelMomentAds.StorefrontProvider = provider;

            LevelMomentAds.Initialize(new LevelMomentConfig());
            Assert.AreEqual(1, provider.ReadCount);

            // A second Initialize() (a game re-bootstrapping) reads again —
            // it is the reply path that must never trigger a read, which the
            // next test proves. This just pins that Initialize() itself always
            // reads through the seam rather than caching across resets.
            LevelMomentAds.Initialize(new LevelMomentConfig());
            Assert.AreEqual(2, provider.ReadCount);
        }

        // ---- The reply path never reads the store again -----------------------

        [Test]
        public void NeedCredential_UsesTheCachedValue_AndNeverReadsTheStoreAgain()
        {
            var provider = new FakeStorefrontProvider("ios", "USA");
            LevelMomentAds.StorefrontProvider = provider;
            LevelMomentAds.Initialize(new LevelMomentConfig
            {
                ApiUrl = "https://api.example.com",
                BreakUrl = "https://app.example.com/break",
                UnsafeTesting = new UnsafeTesting
                {
                    ApiUrl = "https://api.example.com",
                    BreakUrl = "https://app.example.com/break",
                },
            });
            Assert.AreEqual(1, provider.ReadCount, "Initialize must read once");

            var fake = new ScriptableFakeWebView();
            LevelMomentWebViewRegistry.Register(() => fake);
            RewardedAd.SkipRuntimeDriver = true;

            RewardedAd ad = null;
            RewardedAd.Load("p1", new RewardedAdLoadCallbacks { OnAdLoaded = a => ad = a });
            ad.Show(new RewardedAdShowCallbacks());

            // Multiple needCredential asks (the page can post it more than
            // once, e.g. on a pairing-card fallback) must all answer from the
            // cache, never touching the provider again.
            fake.EmitMessage("{\"type\":\"needCredential\"}");
            fake.EmitMessage("{\"type\":\"needCredential\"}");

            Assert.AreEqual(2, fake.EvaluateJSCount);
            StringAssert.Contains("\\\"platform\\\":\\\"ios\\\"", fake.LastJS);
            StringAssert.Contains("\\\"storefront\\\":\\\"USA\\\"", fake.LastJS);
            Assert.AreEqual(1, provider.ReadCount, "the reply path must never read the store");

            RewardedAd.SkipRuntimeDriver = false;
        }
    }
}
