// EditMode tests for the project's sandbox token file: the editor and a
// development build with no testing options switch to sandbox when
// UserSettings/LevelMomentSandboxToken.txt holds a token; a release build never
// does, and explicit options always win.
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class SandboxTokenFileTests
    {
        private const string Token = "eply_sbx_test_project_token";

        [SetUp]
        public void SetUp()
        {
            LevelMomentAds.ResetForTests();
            LevelMomentRuntime.SkipDriver = true;
        }

        [TearDown]
        public void TearDown()
        {
            LevelMomentAds.ResetForTests();
        }

        private static LevelMomentConfig Initialize(string fileContents, bool development, LevelMomentConfig config = null)
        {
            SandboxTokenFile.SourceForTests = () => fileContents;
            LevelMomentConfig.DevelopmentBuildOverrideForTests = development;
            config = config ?? new LevelMomentConfig();
            LevelMomentAds.Initialize(config);
            return config;
        }

        [Test]
        public void DevelopmentBuildUsesTheFile()
        {
            var config = Initialize(Token + "\n", development: true);

            Assert.AreEqual(Token, config.UnsafeTesting.Token);
            Assert.AreEqual(HostedMode.Sandbox, config.Resolve().Mode);
        }

        [Test]
        public void ReleaseBuildIgnoresTheFile()
        {
            var config = Initialize(Token, development: false);

            Assert.IsNull(config.UnsafeTesting);
            Assert.AreEqual(HostedMode.Production, config.Resolve().Mode);
        }

        [Test]
        public void NoFileMeansProduction()
        {
            Assert.IsNull(Initialize(null, development: true).UnsafeTesting);
        }

        [Test]
        public void ExplicitTestingOptionsWin()
        {
            var config = Initialize(Token, development: true,
                new LevelMomentConfig { UnsafeTesting = new UnsafeTesting { Token = "eply_sbx_explicit" } });

            Assert.AreEqual("eply_sbx_explicit", config.UnsafeTesting.Token);
        }

        [Test]
        public void OptOutKeepsProduction()
        {
            var config = Initialize(Token, development: true, new LevelMomentConfig { UseSandboxTokenFile = false });

            Assert.IsNull(config.UnsafeTesting);
        }

        [Test]
        public void MockKeepsItsOwnContent()
        {
            Assert.IsNull(Initialize(Token, development: true, new LevelMomentConfig { Mock = true }).UnsafeTesting);
        }

        [Test]
        public void AValueWithoutTheSandboxPrefixIsIgnored()
        {
            Assert.IsNull(Initialize("pl_not_a_token", development: true).UnsafeTesting);
        }
    }
}
