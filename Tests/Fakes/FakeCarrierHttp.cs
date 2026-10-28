using System.Net;
using AuthenticationServer.Models.Services;

namespace FreightEstimator.Tests.Fakes
{
    /// <summary>
    /// Test adapter for ICarrierHttp (see Models/Services/CarrierHttp.cs): replays recorded carrier
    /// responses, or simulates a timeout or an error status, and remembers every request it was given.
    /// A request nothing was set up for throws instead of falling through, so a quote run with this
    /// installed can never reach the network.
    /// </summary>
    public class FakeCarrierHttp : ICarrierHttp
    {
        private readonly List<(string UrlContains, Func<CarrierHttpResponse> Reply)> _rules = new();

        public List<CarrierHttpRequest> Requests { get; } = new();

        /// <summary>Answers requests whose URL contains the text with the contents of a file under Tests/Recorded.</summary>
        public FakeCarrierHttp Replay(string urlContains, string recordedFile, HttpStatusCode status = HttpStatusCode.OK)
        {
            string body = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", recordedFile));
            return RespondWith(urlContains, status, body);
        }

        /// <summary>Answers requests whose URL contains the text with the given status and body.</summary>
        public FakeCarrierHttp RespondWith(string urlContains, HttpStatusCode status, string body)
        {
            _rules.Add((urlContains, () => new CarrierHttpResponse
            {
                StatusCode = status,
                StatusDescription = status.ToString(),
                Body = body
            }));
            return this;
        }

        /// <summary>Makes requests whose URL contains the text fail the way a timeout does: no reply, WebException.</summary>
        public FakeCarrierHttp TimeOut(string urlContains)
        {
            _rules.Add((urlContains, () => throw new WebException("The operation has timed out.", WebExceptionStatus.Timeout)));
            return this;
        }

        public CarrierHttpResponse Post(CarrierHttpRequest request)
        {
            Requests.Add(request);
            foreach (var (urlContains, reply) in _rules)
            {
                if (request.Url.Contains(urlContains)) return reply();
            }
            throw new InvalidOperationException("FakeCarrierHttp has no response set up for " + request.Url + "; the real carrier would have been called.");
        }

        /// <summary>Makes this the app's carrier HTTP until the returned scope is disposed.</summary>
        public IDisposable Install()
        {
            ICarrierHttp previous = CarrierHttp.Current;
            SetCurrent(this);
            return new Restore(previous);
        }

        private sealed class Restore : IDisposable
        {
            private readonly ICarrierHttp _previous;
            public Restore(ICarrierHttp previous) { _previous = previous; }
            public void Dispose() { SetCurrent(_previous); }
        }

        // CarrierHttp.Current has an internal setter, visible to the net8 Tests assembly only. Going through
        // reflection lets the net472 QuoteTests project share this fake without another InternalsVisibleTo
        // (#15 allows no production code changes).
        private static void SetCurrent(ICarrierHttp value)
        {
            typeof(CarrierHttp).GetProperty(nameof(CarrierHttp.Current)).GetSetMethod(nonPublic: true).Invoke(null, new object[] { value });
        }
    }
}
