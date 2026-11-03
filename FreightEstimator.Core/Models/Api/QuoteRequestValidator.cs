using System;
using System.Collections.Generic;
using System.Linq;
using AuthenticationServer.Models.Services;

namespace AuthenticationServer.Models.Api
{
    /// <summary>
    /// The 400 rules for a Quote request. Reports every problem at once, each naming the field at fault, so a
    /// Consumer developer can fix a request in one pass. Pure: the valid plant codes are passed in.
    /// </summary>
    public static class QuoteRequestValidator
    {
        public const int MaxReferenceNumberLength = 100;

        public static List<ApiError> Validate(QuoteRequest request, IEnumerable<string> validPlantCodes)
        {
            var errors = new List<ApiError>();

            if (request == null)
            {
                errors.Add(Error("body", "The request body must be a JSON Quote request."));
                return errors;
            }

            if (string.IsNullOrWhiteSpace(request.PlantCode))
            {
                errors.Add(Error("plantCode", "plantCode is required."));
            }
            else if (!validPlantCodes.Contains(request.PlantCode.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(Error("plantCode", "plantCode '" + request.PlantCode + "' is not a plant. GET /api/v1/plants lists the valid codes."));
            }

            if (string.IsNullOrWhiteSpace(request.ReferenceNumber))
            {
                errors.Add(Error("referenceNumber", "referenceNumber is required."));
            }
            else if (request.ReferenceNumber.Trim().Length > MaxReferenceNumberLength)
            {
                errors.Add(Error("referenceNumber", "referenceNumber must be at most " + MaxReferenceNumberLength + " characters."));
            }

            if (request.Destination == null)
            {
                errors.Add(Error("destination", "destination is required."));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Destination.State))
                    errors.Add(Error("destination.state", "destination.state is required."));
                if (string.IsNullOrWhiteSpace(request.Destination.PostalCode))
                    errors.Add(Error("destination.postalCode", "destination.postalCode is required."));
            }

            if (request.Packages == null || request.Packages.Count == 0)
            {
                errors.Add(Error("packages", "packages must list at least one package."));
            }
            else
            {
                for (int i = 0; i < request.Packages.Count; i++)
                {
                    decimal? weight = request.Packages[i]?.Weight;
                    if (weight == null || weight <= 0)
                        errors.Add(Error("packages[" + i + "].weight", "packages[" + i + "].weight is required and must be greater than zero."));
                }
            }

            if (request.Services != null)
            {
                for (int i = 0; i < request.Services.Count; i++)
                {
                    if (ServiceCatalog.Find(request.Services[i]) == null)
                        errors.Add(Error("services[" + i + "]", "services[" + i + "] '" + request.Services[i] + "' is not a service. Known services: " + string.Join(", ", ServiceCatalog.All.Select(s => s.Name)) + "."));
                }
            }

            return errors;
        }

        private static ApiError Error(string field, string message) => new ApiError { Field = field, Message = message };
    }
}
