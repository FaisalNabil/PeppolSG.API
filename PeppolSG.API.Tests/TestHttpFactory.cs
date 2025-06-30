using System;
using System.Net.Http;
using System.Web.Http;

namespace PeppolSG.API.Tests
{
    internal static class TestHttpFactory
    {
        public static HttpRequestMessage CreateRequest()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "http://localhost/as4");
            req.SetConfiguration(new HttpConfiguration());
            return req;
        }
    }
} 