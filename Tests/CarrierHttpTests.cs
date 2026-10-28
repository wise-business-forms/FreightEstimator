using System.Net;
using System.Net.Sockets;
using AuthenticationServer.Models.Services;

namespace FreightEstimator.Tests
{
    /// <summary>
    /// The production ICarrierHttp implementation, exercised against a localhost listener so nothing
    /// leaves the machine. These pin "the default implementation makes the same calls as today":
    /// a POST with the caller's content type, headers and body.
    /// </summary>
    public class CarrierHttpTests
    {
        private HttpListener _listener = null!;
        private string _url = null!;

        [SetUp]
        public void StartListener()
        {
            int port;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _url = $"http://localhost:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(_url);
            _listener.Start();
        }

        [TearDown]
        public void StopListener()
        {
            _listener.Close();
        }

        [Test]
        public void PostsTheBodyWithTheCallersContentTypeAndHeaders()
        {
            string? seenMethod = null, seenContentType = null, seenAuthorization = null, seenBody = null;
            var served = Task.Run(() =>
            {
                HttpListenerContext context = _listener.GetContext();
                seenMethod = context.Request.HttpMethod;
                seenContentType = context.Request.ContentType;
                seenAuthorization = context.Request.Headers["Authorization"];
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    seenBody = reader.ReadToEnd();
                byte[] reply = System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}");
                context.Response.OutputStream.Write(reply, 0, reply.Length);
                context.Response.Close();
            });

            ICarrierHttp http = new WebRequestCarrierHttp();
            CarrierHttpResponse response = http.Post(new CarrierHttpRequest
            {
                Url = _url,
                ContentType = "application/json",
                Body = "{\"RateRequest\":{}}",
                Headers = { ["Authorization"] = "Bearer abc" }
            });
            served.Wait();

            Assert.That(seenMethod, Is.EqualTo("POST"));
            Assert.That(seenContentType, Does.StartWith("application/json"));
            Assert.That(seenAuthorization, Is.EqualTo("Bearer abc"));
            Assert.That(seenBody, Is.EqualTo("{\"RateRequest\":{}}"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Body, Is.EqualTo("{\"ok\":true}"));
        }

        [Test]
        public void ReturnsAnErrorStatusWithItsBodyInsteadOfThrowing()
        {
            var served = Task.Run(() =>
            {
                HttpListenerContext context = _listener.GetContext();
                context.Response.StatusCode = 401;
                byte[] reply = System.Text.Encoding.UTF8.GetBytes("{\"response\":{\"errors\":[{\"code\":\"250002\"}]}}");
                context.Response.OutputStream.Write(reply, 0, reply.Length);
                context.Response.Close();
            });

            CarrierHttpResponse response = new WebRequestCarrierHttp().Post(
                new CarrierHttpRequest { Url = _url, ContentType = "application/json", Body = "{}" });
            served.Wait();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(response.IsError, Is.True);
            Assert.That(response.Body, Does.Contain("250002"));
        }

        [Test]
        public void ThrowsWebExceptionWhenNothingAnswers()
        {
            _listener.Close();   // nothing is listening on the port any more

            Assert.Throws<WebException>(() => new WebRequestCarrierHttp().Post(
                new CarrierHttpRequest { Url = _url, ContentType = "application/json", Body = "{}" }));
        }
    }
}
