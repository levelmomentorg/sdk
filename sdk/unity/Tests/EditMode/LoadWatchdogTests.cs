// EditMode tests for LoadWatchdog — the pre-`ready` load-timeout state machine.
// Pure C# with an injected clock; no WebView, no Unity time. Mirrors the
// load-timeout watchdog behaviour verified in sdk/web (client.test.ts).
//
// Run from Unity: Window → General → Test Runner → EditMode → Run All.

using System;
using NUnit.Framework;
using LevelMoment;

namespace LevelMoment.Tests.EditMode
{
    public class LoadWatchdogTests
    {
        private double _now;

        private LoadWatchdog Make(double timeoutSeconds)
        {
            return new LoadWatchdog(timeoutSeconds, () => _now);
        }

        [SetUp]
        public void Reset()
        {
            _now = 0;
        }

        [Test]
        public void Tick_BeforeStart_DoesNotFire()
        {
            var wd = Make(15);
            _now = 100;
            Assert.IsFalse(wd.Tick());
        }

        [Test]
        public void Tick_BeforeDeadline_DoesNotFire()
        {
            var wd = Make(15);
            wd.Start();
            _now = 14.999;
            Assert.IsFalse(wd.Tick());
            Assert.IsFalse(wd.HasFired);
        }

        [Test]
        public void Tick_AtDeadline_FiresExactlyOnce()
        {
            var wd = Make(15);
            wd.Start();

            _now = 14.999;
            Assert.IsFalse(wd.Tick());

            _now = 15.0;
            Assert.IsTrue(wd.Tick(), "should fire at the deadline");
            Assert.IsTrue(wd.HasFired);

            _now = 30;
            Assert.IsFalse(wd.Tick(), "must not fire a second time");
        }

        [Test]
        public void NotifyReady_BeforeDeadline_CancelsWatchdog()
        {
            var wd = Make(15);
            wd.Start();
            wd.NotifyReady();

            _now = 1000;
            Assert.IsFalse(wd.Tick());
            Assert.IsFalse(wd.HasFired);
            Assert.IsFalse(wd.IsRunning);
        }

        [Test]
        public void Cancel_BeforeDeadline_PreventsFiring()
        {
            var wd = Make(15);
            wd.Start();
            wd.Cancel();

            _now = 1000;
            Assert.IsFalse(wd.Tick());
        }

        [Test]
        public void ZeroTimeout_DisablesWatchdog()
        {
            var wd = Make(0);
            wd.Start();
            Assert.IsFalse(wd.IsRunning);

            _now = 1000;
            Assert.IsFalse(wd.Tick());
        }

        [Test]
        public void NegativeTimeout_DisablesWatchdog()
        {
            var wd = Make(-1);
            wd.Start();
            Assert.IsFalse(wd.IsRunning);

            _now = 1000;
            Assert.IsFalse(wd.Tick());
        }

        [Test]
        public void Constructor_NullClock_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new LoadWatchdog(15, null));
        }
    }
}
