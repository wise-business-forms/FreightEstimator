using System;

namespace AuthenticationServer.Models
{
    /// <summary>
    /// A Calculated Rate (and the markup that produced it) for one service returned within a Quote.
    /// See CONTEXT.md for the Quote / Carrier Rate / Calculated Rate / Quote ID vocabulary.
    /// </summary>
    public class QuoteServiceCalculation
    {
        public Guid QuoteId { get; set; }
        public string Carrier { get; set; }
        public string PlantCode { get; set; }
        public string UserName { get; set; }
        public string ServiceCode { get; set; }
        public string ServiceName { get; set; }
        public decimal? CarrierRate { get; set; }
        public bool? IsNegotiatedRate { get; set; }
        public decimal? PerPackageCharge { get; set; }
        public decimal? PerShipmentCharge { get; set; }
        public decimal? MarkupPercent { get; set; }
        public decimal CalculatedRate { get; set; }

        /// <summary>
        /// Builds a calculation record from a UPS service exactly as PlantController already
        /// computed it (GRID1 and GRID2). Returns null if the final rate can't be read, rather
        /// than persisting a bad row.
        /// </summary>
        public static QuoteServiceCalculation FromUps(Shipment shipment, UPSService service)
        {
            if (shipment == null || service == null) return null;
            if (!TryToDecimal(service.CustomerRate, out var calculatedRate)) return null;

            bool? isNegotiatedRate = bool.TryParse(service.CWT, out var cwt) ? cwt : (bool?)null;

            // UPSService only ever carries the service as a name string (e.g. "UPSGround");
            // the numeric code lives on the UPSService.ServiceCode enum, so recover it from
            // the name rather than duplicating the name into a "code" column.
            string serviceCode = Enum.TryParse<UPSService.ServiceCode>(service.ServiceName, out var code)
                ? ((int)code).ToString("00")
                : null;

            decimal? carrierRate = null;
            if (isNegotiatedRate == true && decimal.TryParse(service.RatedShipment_NegotiatedRateCharges_TotalCharge, out var negotiated))
            {
                carrierRate = negotiated;
            }
            else if (decimal.TryParse(service.RatedShipment_PublishedRateCharges_MonetaryValue, out var published))
            {
                carrierRate = published;
            }

            return new QuoteServiceCalculation
            {
                QuoteId = shipment.QuoteId,
                Carrier = "UPS",
                PlantCode = shipment.PlantId,
                UserName = shipment.user_name,
                ServiceCode = serviceCode,
                ServiceName = service.ServiceName,
                CarrierRate = carrierRate,
                IsNegotiatedRate = isNegotiatedRate,
                PerPackageCharge = decimal.TryParse(service.Plant_PerPackageCharge, out var perPackageCharge) ? perPackageCharge : (decimal?)null,
                PerShipmentCharge = decimal.TryParse(service.Plant_ShipmentCharge, out var perShipmentCharge) ? perShipmentCharge : (decimal?)null,
                MarkupPercent = decimal.TryParse(service.Plant_Surcharge, out var markupPercent) ? markupPercent : (decimal?)null,
                CalculatedRate = calculatedRate
            };
        }

        /// <summary>
        /// Builds a calculation record from the unified Calculated Rate module (ADR 0003), as the Quote API logs it.
        /// The Calculated Rate is the rounded amount a Consumer is given; the components are as the module applied them.
        /// </summary>
        public static QuoteServiceCalculation FromCalculatedRate(
            Guid quoteId, string carrier, string plantCode, string userName, string serviceName, Services.CalculatedRate rate)
        {
            if (rate == null) return null;

            return new QuoteServiceCalculation
            {
                QuoteId = quoteId,
                Carrier = carrier,
                PlantCode = plantCode,
                UserName = userName,
                ServiceCode = Enum.TryParse<UPSService.ServiceCode>(serviceName, out var code) ? ((int)code).ToString("00") : null,
                ServiceName = serviceName,
                CarrierRate = rate.CarrierRate,
                IsNegotiatedRate = null,
                PerPackageCharge = rate.PerPackageCharge,
                PerShipmentCharge = rate.PerShipmentCharge,
                MarkupPercent = rate.MarkupPercent,
                CalculatedRate = rate.RoundedAmount
            };
        }

        /// <summary>
        /// Builds a calculation record from the Logistics Partner (LTL/TransportInsight) markup
        /// math exactly as PlantController.GetLessThanTruckloadRates_TI already computes it.
        /// Returns null if the final rate can't be read, rather than persisting a bad row.
        /// </summary>
        public static QuoteServiceCalculation FromLogisticsPartner(
            Shipment shipment, UPSService service,
            double carrierRate, double perPackageCharge, double perShipmentCharge, double markupFraction,
            double calculatedRate)
        {
            if (shipment == null || service == null) return null;
            if (!TryToDecimal(calculatedRate, out var calculatedRateDecimal)) return null;

            return new QuoteServiceCalculation
            {
                QuoteId = shipment.QuoteId,
                Carrier = "LogisticsPartner",
                PlantCode = shipment.PlantId,
                UserName = shipment.user_name,
                ServiceCode = null, // no numeric-code concept for Logistics Partner services
                ServiceName = service.ServiceName,
                CarrierRate = TryToDecimal(carrierRate, out var carrierRateDecimal) ? carrierRateDecimal : (decimal?)null,
                IsNegotiatedRate = null,
                PerPackageCharge = TryToDecimal(perPackageCharge, out var perPackageChargeDecimal) ? perPackageChargeDecimal : (decimal?)null,
                PerShipmentCharge = TryToDecimal(perShipmentCharge, out var perShipmentChargeDecimal) ? perShipmentChargeDecimal : (decimal?)null,
                MarkupPercent = TryToDecimal(markupFraction * 100, out var markupPercentDecimal) ? markupPercentDecimal : (decimal?)null,
                CalculatedRate = calculatedRateDecimal
            };
        }

        private static bool TryToDecimal(double value, out decimal result)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < (double)decimal.MinValue || value > (double)decimal.MaxValue)
            {
                result = 0;
                return false;
            }
            result = (decimal)value;
            return true;
        }
    }
}
