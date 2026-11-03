using AuthenticationServer.Models.Api;

namespace FreightEstimator.Tests
{
    /// <summary>
    /// The 400 rules for a Quote API request (#18), without a database or a web host. The same rules are also
    /// covered end to end at the HTTP seam in the QuoteTests project.
    /// </summary>
    public class QuoteRequestValidatorTests
    {
        private static readonly string[] Plants = { "ALP", "BUT" };

        private static QuoteRequest Valid() => new QuoteRequest
        {
            PlantCode = "ALP",
            ReferenceNumber = "JOB-1001",
            Destination = new Destination { State = "PA", PostalCode = "15201" },
            Packages = new List<PackageRequest> { new PackageRequest { Weight = 12.5m }, new PackageRequest { Weight = 8m } },
            Services = new List<string> { "UPSGround" }
        };

        private static string[] FieldsOf(QuoteRequest request) =>
            QuoteRequestValidator.Validate(request, Plants).Select(e => e.Field).ToArray();

        [Test]
        public void AValidRequestHasNoErrors() => Assert.That(FieldsOf(Valid()), Is.Empty);

        [Test]
        public void ABodyThatIsNotAQuoteRequestIsReportedAsTheBody() =>
            Assert.That(FieldsOf(null), Is.EqualTo(new[] { "body" }));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void AMissingReferenceNumberNamesTheField(string? reference)
        {
            QuoteRequest request = Valid();
            request.ReferenceNumber = reference;
            Assert.That(FieldsOf(request), Is.EqualTo(new[] { "referenceNumber" }));
        }

        [Test]
        public void AReferenceNumberTooLongForItsColumnNamesTheField()
        {
            QuoteRequest request = Valid();
            request.ReferenceNumber = new string('x', QuoteRequestValidator.MaxReferenceNumberLength + 1);
            Assert.That(FieldsOf(request), Is.EqualTo(new[] { "referenceNumber" }));
        }

        [Test]
        public void NoPackagesNamesThePackagesField([Values(true, false)] bool listIsNull)
        {
            QuoteRequest request = Valid();
            request.Packages = listIsNull ? null : new List<PackageRequest>();
            Assert.That(FieldsOf(request), Is.EqualTo(new[] { "packages" }));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void AZeroOrNegativeWeightNamesThePackage(int weight)
        {
            QuoteRequest request = Valid();
            request.Packages[1].Weight = weight;
            Assert.That(FieldsOf(request), Is.EqualTo(new[] { "packages[1].weight" }));
        }

        [Test]
        public void AMissingWeightNamesThePackage()
        {
            QuoteRequest request = Valid();
            request.Packages[0].Weight = null;
            Assert.That(FieldsOf(request), Is.EqualTo(new[] { "packages[0].weight" }));
        }

        [Test]
        public void AnUnknownPlantNamesTheField_AndSoDoesAMissingOne()
        {
            QuoteRequest unknown = Valid();
            unknown.PlantCode = "ZZZ";
            Assert.That(FieldsOf(unknown), Is.EqualTo(new[] { "plantCode" }));

            QuoteRequest missing = Valid();
            missing.PlantCode = null;
            Assert.That(FieldsOf(missing), Is.EqualTo(new[] { "plantCode" }));
        }

        [Test]
        public void APlantCodeIsMatchedIgnoringCase()
        {
            QuoteRequest request = Valid();
            request.PlantCode = " alp ";
            Assert.That(FieldsOf(request), Is.Empty);
        }

        [Test]
        public void AnUnknownServiceNamesItsPosition()
        {
            QuoteRequest request = Valid();
            request.Services = new List<string> { "UPSGround", "Teleport" };
            Assert.That(FieldsOf(request), Is.EqualTo(new[] { "services[1]" }));
        }

        [Test]
        public void ServicesMayBeOmitted()
        {
            QuoteRequest request = Valid();
            request.Services = null;
            Assert.That(FieldsOf(request), Is.Empty);
        }

        [Test]
        public void ADestinationNeedsAStateAndAPostalCode()
        {
            QuoteRequest request = Valid();
            request.Destination = new Destination();
            Assert.That(FieldsOf(request), Is.EquivalentTo(new[] { "destination.state", "destination.postalCode" }));

            request.Destination = null;
            Assert.That(FieldsOf(request), Is.EqualTo(new[] { "destination" }));
        }

        [Test]
        public void EveryProblemIsReportedAtOnce()
        {
            QuoteRequest request = Valid();
            request.ReferenceNumber = null;
            request.Packages = new List<PackageRequest>();
            request.PlantCode = "ZZZ";
            Assert.That(FieldsOf(request), Is.EquivalentTo(new[] { "plantCode", "referenceNumber", "packages" }));
        }
    }
}
