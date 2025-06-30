using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PeppolSG.API.Service.Resilience
{
    public class CircuitHttpClient
    {
        private readonly HttpClient _inner;
        private readonly CircuitBreaker _breaker;

        public CircuitHttpClient(HttpClient inner, CircuitBreaker breaker)
        {
            _inner = inner;
            _breaker = breaker;
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken token = default)
        {
            _breaker.ThrowIfOpen();
            try
            {
                var resp = await _inner.SendAsync(req, token);
                if (!resp.IsSuccessStatusCode)
                    _breaker.OnFailure();
                else
                    _breaker.OnSuccess();
                return resp;
            }
            catch (Exception)
            {
                _breaker.OnFailure();
                throw;
            }
        }
    }
} 