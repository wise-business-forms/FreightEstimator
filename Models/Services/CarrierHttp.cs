using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// The one place the app sends a request to a carrier (UPS, Logistics Partner) and reads the
    /// reply, so the quote flow can be exercised offline: tests replace <see cref="CarrierHttp.Current"/>
    /// with a fake. Every carrier call is a POST of a text body that returns a text body.
    /// </summary>
    public interface ICarrierHttp
    {
        /// <summary>
        /// Sends the request. A reply with any HTTP status, error statuses included, comes back as a
        /// response; a failure with no reply at all (timeout, connection refused) throws WebException.
        /// </summary>
        CarrierHttpResponse Post(CarrierHttpRequest request);
    }

    public class CarrierHttpRequest
    {
        public string Url { get; set; }
        public string ContentType { get; set; }
        public string Body { get; set; }
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();
    }

    public class CarrierHttpResponse
    {
        public HttpStatusCode StatusCode { get; set; }
        public string StatusDescription { get; set; }
        public string Body { get; set; }

        /// <summary>True for 4xx and 5xx, the statuses HttpWebRequest.GetResponse throws for.</summary>
        public bool IsError
        {
            get { return (int)StatusCode >= 400; }
        }

        /// <summary>
        /// For call sites that treated an error status as an exception (HttpWebRequest.GetResponse threw
        /// one): throws the WebException with the framework's message. Its Response is null, so a handler
        /// that reads the error body should look at this response instead.
        /// </summary>
        public void EnsureSuccessStatus()
        {
            if (IsError)
            {
                throw new WebException(
                    "The remote server returned an error: (" + (int)StatusCode + ") " + StatusDescription + ".",
                    WebExceptionStatus.ProtocolError);
            }
        }
    }

    /// <summary>
    /// Where carrier calls go. Production code never changes it; tests replace it (it is internal,
    /// visible to the Tests project only) and must put the real one back.
    /// </summary>
    public static class CarrierHttp
    {
        public static ICarrierHttp Current { get; internal set; } = new WebRequestCarrierHttp();
    }

    /// <summary>The production implementation: the same HttpWebRequest POST the carrier call sites made themselves.</summary>
    public class WebRequestCarrierHttp : ICarrierHttp
    {
        public CarrierHttpResponse Post(CarrierHttpRequest request)
        {
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(request.Url);
            webRequest.Method = "POST";
            webRequest.ContentType = request.ContentType;
            foreach (KeyValuePair<string, string> header in request.Headers)
            {
                webRequest.Headers.Add(header.Key, header.Value);
            }

            byte[] bytes = Encoding.UTF8.GetBytes(request.Body ?? string.Empty);
            webRequest.ContentLength = bytes.Length;
            using (Stream stream = webRequest.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse())
                {
                    return Read(response);
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse errorResponse)
            {
                using (errorResponse)
                {
                    return Read(errorResponse);
                }
            }
        }

        private static CarrierHttpResponse Read(HttpWebResponse response)
        {
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                return new CarrierHttpResponse
                {
                    StatusCode = response.StatusCode,
                    StatusDescription = response.StatusDescription,
                    Body = reader.ReadToEnd()
                };
            }
        }
    }
}
