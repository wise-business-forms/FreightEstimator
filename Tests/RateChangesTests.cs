using AuthenticationServer.Models.Reports;

namespace FreightEstimator.Tests
{
    public class RateChangesTests
    {
        private static readonly DateTime Jan1 = new DateTime(2026, 1, 1, 9, 0, 0);

        private static MatchKey Key(string zip = "90210", string weights = "20,20") =>
            new MatchKey("ALP", zip, weights.Split(',').Length, weights, residentialIndicatorSent: false);

        private static QuoteFacts Quote(int requestId, DateTime requestDate, MatchKey key, params ServiceRate[] services) =>
            new QuoteFacts(requestId, requestDate, key, services);

        private static ServiceRate Ground(decimal? published, decimal? negotiated) =>
            new ServiceRate("03", "UPS Ground", published, negotiated);

        [Test]
        public void QuotesWithTheSameMatchKeyFormOneMatchGroupInDateOrder()
        {
            var later = Quote(2, Jan1.AddDays(7), Key(), Ground(20m, 15m));
            var earlier = Quote(1, Jan1, Key(), Ground(20m, 15m));
            var otherZip = Quote(3, Jan1, Key(zip: "10001"), Ground(20m, 15m));

            var groups = RateChanges.Build(new[] { later, earlier, otherZip }, new CalculationCandidate[0]);

            Assert.That(groups, Has.Count.EqualTo(2));
            var group = groups.Single(g => g.Key.ShipToZip == "90210");
            Assert.That(group.Quotes.Select(q => q.RequestId), Is.EqualTo(new[] { 1, 2 }));
        }

        private static ServiceTimeline GroundTimeline(IEnumerable<QuoteFacts> quotes, IEnumerable<CalculationCandidate>? calculations = null) =>
            RateChanges.Build(quotes, calculations ?? new CalculationCandidate[0]).Single().Services.Single(s => s.ServiceCode == "03");

        [Test]
        public void PublishedRateChangeIsAUpsPriceChange()
        {
            var timeline = GroundTimeline(new[]
            {
                Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)),
                Quote(2, Jan1.AddDays(7), Key(), Ground(21.00m, 15.75m)),
            });

            Assert.That(timeline.Entries[0].Change, Is.Null);
            Assert.That(timeline.Entries[1].Change.Causes, Is.EquivalentTo(new[] { RateChangeCause.UpsPriceChange, RateChangeCause.Unattributable }));
        }

        [Test]
        public void NegotiatedRateChangeWithoutPublishedChangeIsAUpsNegotiatedChange()
        {
            var timeline = GroundTimeline(new[]
            {
                Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)),
                Quote(2, Jan1.AddDays(7), Key(), Ground(20.00m, 14.00m)),
            });

            Assert.That(timeline.Entries[1].Change.Causes, Is.EquivalentTo(new[] { RateChangeCause.UpsNegotiatedChange, RateChangeCause.Unattributable }));
            Assert.That(new[] { timeline.Entries[1].Change.PublishedChanged, timeline.Entries[1].Change.NegotiatedChanged, timeline.Entries[1].Change.CalculatedChanged },
                Is.EqualTo(new[] { false, true, false }));
        }

        [Test]
        public void AChangeIsAtLeastOneCent()
        {
            var timeline = GroundTimeline(new[]
            {
                Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)),
                Quote(2, Jan1.AddDays(1), Key(), Ground(20.00m, 15.00m)),
                Quote(3, Jan1.AddDays(2), Key(), Ground(20.01m, 15.00m)),
            });

            Assert.That(timeline.Entries[1].Change, Is.Null);
            Assert.That(timeline.Entries[2].Change, Is.Not.Null);
        }

        [Test]
        public void AQuoteMissingTheServiceIsSkippedRatherThanReportedAsAChange()
        {
            var timeline = GroundTimeline(new[]
            {
                Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)),
                Quote(2, Jan1.AddDays(1), Key(), new ServiceRate("02", "UPS 2nd Day Air", 40m, 30m)),
                Quote(3, Jan1.AddDays(2), Key(), Ground(20.00m, 15.00m)),
            });

            Assert.That(timeline.Entries.Select(e => e.Quote.RequestId), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(timeline.Entries[1].Change, Is.Null);
        }

        [Test]
        public void ASingleQuoteGroupHasNoRateChanges()
        {
            var group = RateChanges.Build(new[] { Quote(1, Jan1, Key(), Ground(20m, 15m)) }, new CalculationCandidate[0]).Single();

            Assert.That(group.HasRateChanges, Is.False);
        }

        private static CalculationCandidate Calc(int id, DateTime createdDate, decimal carrierRate, decimal calculatedRate,
            bool isNegotiatedRate = true, decimal markupPercent = 10m, decimal perPackageCharge = 1m, decimal perShipmentCharge = 2m,
            string plantCode = "ALP", string serviceCode = "03") =>
            new CalculationCandidate
            {
                QuoteServiceCalculationId = id,
                PlantCode = plantCode,
                ServiceCode = serviceCode,
                CarrierRate = carrierRate,
                IsNegotiatedRate = isNegotiatedRate,
                MarkupPercent = markupPercent,
                PerPackageCharge = perPackageCharge,
                PerShipmentCharge = perShipmentCharge,
                CalculatedRate = calculatedRate,
                CreatedDate = createdDate
            };

        [Test]
        public void AttachesTheOneCalculationLoggedWithinThirtySecondsAfterTheQuote()
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)) },
                new[] { Calc(100, Jan1.AddSeconds(3), carrierRate: 15.00m, calculatedRate: 20.50m) });

            Assert.That(timeline.Entries[0].Calculation?.QuoteServiceCalculationId, Is.EqualTo(100));
        }

        [TestCase(-1, TestName = "LoggedBeforeTheQuote")]
        [TestCase(31, TestName = "LoggedMoreThanThirtySecondsAfter")]
        public void RejectsACalculationOutsideTheWindow(int secondsAfterQuote)
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)) },
                new[] { Calc(100, Jan1.AddSeconds(secondsAfterQuote), carrierRate: 15.00m, calculatedRate: 20.50m) });

            Assert.That(timeline.Entries[0].Calculation, Is.Null);
        }

        [Test]
        public void LeavesTheCalculatedRateBlankWhenTwoCalculationsQualify()
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)) },
                new[]
                {
                    Calc(100, Jan1.AddSeconds(2), carrierRate: 15.00m, calculatedRate: 20.50m),
                    Calc(101, Jan1.AddSeconds(4), carrierRate: 15.00m, calculatedRate: 20.50m),
                });

            Assert.That(timeline.Entries[0].Calculation, Is.Null);
        }

        [Test]
        public void LeavesTheCalculatedRateBlankWhenOneCalculationQualifiesForTwoQuotes()
        {
            // Two people quoting the same plant and service at the same moment, to zips in the same zone.
            var groups = RateChanges.Build(
                new[]
                {
                    Quote(1, Jan1, Key(zip: "90210"), Ground(20.00m, 15.00m)),
                    Quote(2, Jan1.AddSeconds(1), Key(zip: "90211"), Ground(20.00m, 15.00m)),
                },
                new[] { Calc(100, Jan1.AddSeconds(3), carrierRate: 15.00m, calculatedRate: 20.50m) });

            Assert.That(groups.SelectMany(g => g.Services).SelectMany(s => s.Entries).Select(e => e.Calculation), Is.All.Null);
        }

        [Test]
        public void ACompetingQuoteOutsideTheGroupsAlsoMakesACalculationAmbiguous()
        {
            var groups = RateChanges.Build(
                new[] { Quote(1, Jan1, Key(zip: "90210"), Ground(20.00m, 15.00m)) },
                new[] { Calc(100, Jan1.AddSeconds(3), carrierRate: 15.00m, calculatedRate: 20.50m) },
                competingQuotes: new[] { Quote(2, Jan1.AddSeconds(1), Key(zip: "90211"), Ground(20.00m, 15.00m)) });

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups.Single().Services.Single().Entries.Single().Calculation, Is.Null);
        }

        [Test]
        public void ComparesTheCalculationsCarrierRateWithPublishedWhenItWasNotNegotiated()
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)) },
                new[]
                {
                    Calc(100, Jan1.AddSeconds(2), carrierRate: 20.00m, calculatedRate: 25.00m, isNegotiatedRate: false),
                    Calc(101, Jan1.AddSeconds(2), carrierRate: 20.00m, calculatedRate: 25.00m, isNegotiatedRate: true),
                });

            Assert.That(timeline.Entries[0].Calculation?.QuoteServiceCalculationId, Is.EqualTo(100));
        }

        private static readonly DateTime Jan8 = Jan1.AddDays(7);

        [Test]
        public void AChargeChangeWithTheSameCarrierRateIsOurChargesChangedNamingTheComponent()
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)), Quote(2, Jan8, Key(), Ground(20.00m, 15.00m)) },
                new[]
                {
                    Calc(100, Jan1.AddSeconds(2), carrierRate: 15.00m, calculatedRate: 20.50m, markupPercent: 10m),
                    Calc(101, Jan8.AddSeconds(2), carrierRate: 15.00m, calculatedRate: 22.00m, markupPercent: 20m),
                });

            var change = timeline.Entries[1].Change;
            Assert.That(change.Causes, Is.EquivalentTo(new[] { RateChangeCause.OurChargesChanged }));
            Assert.That(change.ChangedCharges, Is.EquivalentTo(new[] { ChargeComponent.MarkupPercent }));
        }

        [Test]
        public void ADifferentCalculatedRateFromTheSameInputsIsOurCalculationChanged()
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)), Quote(2, Jan8, Key(), Ground(20.00m, 15.00m)) },
                new[]
                {
                    Calc(100, Jan1.AddSeconds(2), carrierRate: 15.00m, calculatedRate: 20.50m),
                    Calc(101, Jan8.AddSeconds(2), carrierRate: 15.00m, calculatedRate: 19.50m),
                });

            Assert.That(timeline.Entries[1].Change.Causes, Is.EquivalentTo(new[] { RateChangeCause.OurCalculationChanged }));
        }

        [Test]
        public void SwitchingBetweenNegotiatedAndPublishedBaseIsOurCalculationChanged()
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 20.00m)), Quote(2, Jan8, Key(), Ground(20.00m, 20.00m)) },
                new[]
                {
                    Calc(100, Jan1.AddSeconds(2), carrierRate: 20.00m, calculatedRate: 25.00m, isNegotiatedRate: true),
                    Calc(101, Jan8.AddSeconds(2), carrierRate: 20.00m, calculatedRate: 26.00m, isNegotiatedRate: false),
                });

            Assert.That(timeline.Entries[1].Change.Causes, Is.EquivalentTo(new[] { RateChangeCause.OurCalculationChanged }));
        }

        [Test]
        public void ABaseSwitchThatLeavesTheCalculatedRateUnchangedIsNotAChange()
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 20.00m)), Quote(2, Jan8, Key(), Ground(20.00m, 20.00m)) },
                new[]
                {
                    Calc(100, Jan1.AddSeconds(2), carrierRate: 20.00m, calculatedRate: 25.00m, isNegotiatedRate: true),
                    Calc(101, Jan8.AddSeconds(2), carrierRate: 20.00m, calculatedRate: 25.00m, isNegotiatedRate: false),
                });

            Assert.That(timeline.Entries[1].Change, Is.Null);
        }

        [Test]
        public void AUpsChangeWithACalculatedRateOnOnlyOneSideIsUnattributable()
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)), Quote(2, Jan8, Key(), Ground(21.00m, 16.00m)) },
                new[] { Calc(101, Jan8.AddSeconds(2), carrierRate: 16.00m, calculatedRate: 21.60m) });

            Assert.That(timeline.Entries[1].Calculation, Is.Not.Null);
            Assert.That(timeline.Entries[1].Change.Causes, Is.EquivalentTo(new[] { RateChangeCause.UpsPriceChange, RateChangeCause.Unattributable }));
        }

        [Test]
        public void AUpsChangeAndAChargeChangeTogetherGetBothLabels()
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)), Quote(2, Jan8, Key(), Ground(21.00m, 16.00m)) },
                new[]
                {
                    Calc(100, Jan1.AddSeconds(2), carrierRate: 15.00m, calculatedRate: 20.50m, perPackageCharge: 1m, perShipmentCharge: 2m),
                    Calc(101, Jan8.AddSeconds(2), carrierRate: 16.00m, calculatedRate: 23.60m, perPackageCharge: 1.5m, perShipmentCharge: 3m),
                });

            var change = timeline.Entries[1].Change;
            Assert.That(change.Causes, Is.EquivalentTo(new[] { RateChangeCause.UpsPriceChange, RateChangeCause.OurChargesChanged }));
            Assert.That(new[] { change.PublishedChanged, change.NegotiatedChanged, change.CalculatedChanged }, Is.EqualTo(new[] { true, true, true }));
            Assert.That(change.ChangedCharges, Is.EquivalentTo(new[] { ChargeComponent.PerPackageCharge, ChargeComponent.PerShipmentCharge }));
        }

        [Test]
        public void AUpsChangeCarriedThroughByTheSameChargesIsOnlyTheUpsChange()
        {
            var timeline = GroundTimeline(
                new[] { Quote(1, Jan1, Key(), Ground(20.00m, 15.00m)), Quote(2, Jan8, Key(), Ground(20.00m, 16.00m)) },
                new[]
                {
                    Calc(100, Jan1.AddSeconds(2), carrierRate: 15.00m, calculatedRate: 20.50m),
                    Calc(101, Jan8.AddSeconds(2), carrierRate: 16.00m, calculatedRate: 21.60m),
                });

            Assert.That(timeline.Entries[1].Change.Causes, Is.EquivalentTo(new[] { RateChangeCause.UpsNegotiatedChange }));
        }
    }
}
