using System.Configuration;
using System.Data.SqlClient;
using AuthenticationServer.CostPlus;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The heartbeat table (#50) in the FreightEstimator test database: each CostPlus Poller instance writes one row about
    /// every 10 s and the Poller Monitor reads them back as ages on the database's clock. Instance ids are unique per test and
    /// removed afterwards, so tests never meet each other's (or a real poller's) rows.
    /// </summary>
    public class CostPlusHeartbeatStoreTests
    {
        private readonly SqlPollerHeartbeatStore _store = new SqlPollerHeartbeatStore();
        private string _run;

        private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

        [SetUp]
        public void SetUp()
        {
            SandboxGuard.Require();
            _run = "TEST-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            using (var conn = Open())
            using (var cmd = new SqlCommand("SELECT OBJECT_ID('dbo.CostPlusPollerHeartbeat', 'U')", conn))
                if (cmd.ExecuteScalar() is DBNull)
                    Assert.Fail("The FreightEstimator test database has no dbo.CostPlusPollerHeartbeat yet. Run Database/FreightEstimator.sql against it.");
        }

        [TearDown]
        public void TearDown()
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("DELETE FROM dbo.CostPlusPollerHeartbeat WHERE InstanceId LIKE 'TEST-%'", conn))
                cmd.ExecuteNonQuery();
        }

        private static SqlConnection Open()
        {
            var conn = new SqlConnection(ConfigurationManager.ConnectionStrings["FreightEstimator"].ConnectionString);
            conn.Open();
            return conn;
        }

        private PollerBeat Beat(string suffix, string mode = "Live") =>
            new PollerBeat { InstanceId = _run + suffix, Machine = "TESTBOX", Mode = mode, Uptime = S(120) };

        private PollerInstanceBeat Read(string suffix) => _store.ReadRecent().SingleOrDefault(b => b.InstanceId == _run + suffix);

        [Test]
        public void AWrittenHeartbeatIsReadBackAsAgesOnTheDatabaseClock()
        {
            PollerBeat beat = Beat("-a", "Shadow");
            beat.SinceLastSuccessfulPoll = S(2);
            beat.IdlePolls = 41; beat.RequestsHandled = 7; beat.DroppedEvents = 3;
            beat.LastAlertSentAgo = S(40);
            _store.Write(beat);

            PollerInstanceBeat read = Read("-a");

            Assert.That(read, Is.Not.Null);
            Assert.That(read.Machine, Is.EqualTo("TESTBOX"));
            Assert.That(read.Mode, Is.EqualTo("Shadow"));
            Assert.That(read.HeartbeatAge.TotalSeconds, Is.InRange(0, 10));
            Assert.That(read.SinceLastSuccessfulPoll.Value.TotalSeconds, Is.InRange(2, 12));
            Assert.That(read.StartedAgo.TotalSeconds, Is.InRange(120, 130));
            Assert.That(read.LastAlertSentAgo.Value.TotalSeconds, Is.InRange(40, 50));
            Assert.That(read.LastProbeInsertedAgo, Is.Null);
            Assert.That((read.IdlePolls, read.RequestsHandled, read.DroppedEvents), Is.EqualTo((41L, 7L, 3L)));
        }

        [Test]
        public void AnInstanceThatNeverPolledHasNoPollAge()
        {
            _store.Write(Beat("-a"));

            Assert.That(Read("-a").SinceLastSuccessfulPoll, Is.Null);
        }

        [Test]
        public void WritingAgainUpdatesTheSameInstanceRatherThanAddingAnother()
        {
            PollerBeat first = Beat("-a"); first.RequestsHandled = 1;
            _store.Write(first);
            PollerBeat second = Beat("-a"); second.RequestsHandled = 5; second.LastProbeInsertedAgo = S(10);
            _store.Write(second);

            Assert.That(_store.ReadRecent().Count(b => b.InstanceId == _run + "-a"), Is.EqualTo(1));
            Assert.That(Read("-a").RequestsHandled, Is.EqualTo(5));
            Assert.That(Read("-a").LastProbeInsertedAgo, Is.Not.Null);
        }

        [Test]
        public void TwoInstancesAreBothReturned()
        {
            _store.Write(Beat("-a"));
            _store.Write(Beat("-b"));

            Assert.That(Read("-a"), Is.Not.Null);
            Assert.That(Read("-b"), Is.Not.Null);
        }

        [Test]
        public void AnInstanceNotHeardFromForAnHourIsNoLongerShown()
        {
            _store.Write(Beat("-old"));
            _store.Write(Beat("-new"));
            using (var conn = Open())
            using (var cmd = new SqlCommand("UPDATE dbo.CostPlusPollerHeartbeat SET HeartbeatAt = DATEADD(MINUTE, -90, SYSDATETIME()) WHERE InstanceId = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", _run + "-old");
                cmd.ExecuteNonQuery();
            }

            Assert.That(Read("-old"), Is.Null);
            Assert.That(Read("-new"), Is.Not.Null);
        }

        [Test]
        public void HeartbeatsOverADayOldAreRemovedByTheNextWrite()
        {
            _store.Write(Beat("-old"));
            using (var conn = Open())
            using (var cmd = new SqlCommand("UPDATE dbo.CostPlusPollerHeartbeat SET HeartbeatAt = DATEADD(DAY, -2, SYSDATETIME()) WHERE InstanceId = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", _run + "-old");
                cmd.ExecuteNonQuery();
            }

            _store.Write(Beat("-new"));

            // Retention is about what is kept, and nothing reads rows that old, so this one looks at the table itself.
            using (var conn = Open())
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.CostPlusPollerHeartbeat WHERE InstanceId = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", _run + "-old");
                Assert.That((int)cmd.ExecuteScalar(), Is.EqualTo(0));
            }
        }
    }
}
