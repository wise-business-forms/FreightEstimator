using AuthenticationServer.Admin;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// Who may open the admin area (ADR 0006, #47): only the users named in Admin:AllowedUsers. Nobody when none are named,
    /// except in Development, where the check is skipped.
    /// </summary>
    public class AdminAccessTests
    {
        private static readonly string[] Allowed = { "WBF\\pmenefee", "WBF\\jdoe" };

        [TestCase("WBF\\pmenefee")]
        [TestCase("wbf\\PMENEFEE")]   // Windows account names are not case-sensitive
        [TestCase("WBF\\jdoe")]
        public void AListedUserIsAllowed(string user) =>
            Assert.That(AdminAccess.IsAllowed(user, Allowed, isDevelopment: false), Is.True);

        [TestCase("WBF\\someoneelse")]
        [TestCase("OTHER\\pmenefee")]   // same account name, different domain
        [TestCase("pmenefee")]          // not in DOMAIN\user form
        public void AnUnlistedUserIsDenied(string user) =>
            Assert.That(AdminAccess.IsAllowed(user, Allowed, isDevelopment: false), Is.False);

        [TestCase(null)]
        [TestCase("")]
        public void NoSignedInUserIsDenied(string user) =>
            Assert.That(AdminAccess.IsAllowed(user, Allowed, isDevelopment: false), Is.False);

        [Test]
        public void NobodyIsAllowedWhenNoUsersAreNamed()
        {
            Assert.That(AdminAccess.IsAllowed("WBF\\pmenefee", null, isDevelopment: false), Is.False);
            Assert.That(AdminAccess.IsAllowed("WBF\\pmenefee", new string[0], isDevelopment: false), Is.False);
        }

        [Test]
        public void BlankEntriesInTheSettingNeverMatchAnyone()
        {
            var blanks = new[] { "", "  ", null };

            Assert.That(AdminAccess.IsAllowed("", blanks, isDevelopment: false), Is.False);
            Assert.That(AdminAccess.IsAllowed(null, blanks, isDevelopment: false), Is.False);
            Assert.That(AdminAccess.IsAllowed("WBF\\pmenefee", blanks, isDevelopment: false), Is.False);
        }

        [Test]
        public void EntriesAreTrimmed() =>
            Assert.That(AdminAccess.IsAllowed("WBF\\pmenefee", new[] { "  WBF\\pmenefee " }, isDevelopment: false), Is.True);

        [Test]
        public void DevelopmentSkipsTheCheck()
        {
            Assert.That(AdminAccess.IsAllowed(null, null, isDevelopment: true), Is.True);
            Assert.That(AdminAccess.IsAllowed("WBF\\someoneelse", Allowed, isDevelopment: true), Is.True);
        }
    }
}
