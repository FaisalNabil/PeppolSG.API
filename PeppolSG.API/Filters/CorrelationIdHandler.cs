using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using System.Linq;

namespace PeppolSG.API.Filters
{
    public class CorrelationIdHandler : DelegatingHandler
    {
        private const string Header = "X-Correlation-Id";
        private static readonly ILog log = LogManager.GetLogger(typeof(CorrelationIdHandler));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Headers.Contains(Header))
                request.Headers.Add(Header, Guid.NewGuid().ToString());
            var corrId = request.Headers.GetValues(Header).First();
            log4net.ThreadContext.Properties["corr"] = corrId;
            var resp = await base.SendAsync(request, cancellationToken);
            if (!resp.Headers.Contains(Header))
                resp.Headers.Add(Header, corrId);
            log4net.ThreadContext.Properties.Remove("corr");
            return resp;
        }
    }
} 