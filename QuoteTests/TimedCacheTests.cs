using AuthenticationServer.CostPlus;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The Poller Monitor re-reads every 2 s per open browser; the cache keeps several open browsers from multiplying the load
    /// on the request table (#49).
    /// </summary>
    public class TimedCacheTests
    {
        private DateTime _now;
        private int _reads;

        [SetUp]
        public void SetUp()
        {
            _now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
            _reads = 0;
        }

        private TimedCache<int> Cache(Func<int> read = null) =>
            new TimedCache<int>(read ?? (() => ++_reads), TimeSpan.FromSeconds(2), () => _now);

        [Test]
        public void TwoReadsWithinTwoSecondsHitTheSourceOnce()
        {
            TimedCache<int> cache = Cache();

            int first = cache.Get();
            _now += TimeSpan.FromMilliseconds(1900);
            int second = cache.Get();

            Assert.That(_reads, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void ALaterReadGoesBackToTheSource()
        {
            TimedCache<int> cache = Cache();

            cache.Get();
            _now += TimeSpan.FromSeconds(2);

            Assert.That(cache.Get(), Is.EqualTo(2));
            Assert.That(_reads, Is.EqualTo(2));
        }

        [Test]
        public void AFailedReadIsNotCached_AndTheNextReadTriesAgain()
        {
            bool fail = true;
            int attempts = 0;
            TimedCache<int> cache = Cache(() => { attempts++; if (fail) throw new InvalidOperationException("down"); return 7; });

            Assert.Throws<InvalidOperationException>(() => cache.Get());
            fail = false;

            Assert.That(cache.Get(), Is.EqualTo(7));
            Assert.That(attempts, Is.EqualTo(2));
        }

        [Test]
        public void ManyReadersAtOnceHitTheSourceOnce()
        {
            TimedCache<int> cache = Cache(() => { Thread.Sleep(100); return ++_reads; });

            Parallel.For(0, 8, _ => cache.Get());

            Assert.That(_reads, Is.EqualTo(1));
        }
    }
}
