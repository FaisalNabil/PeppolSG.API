using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.ExceptionHandling;
using System.Web.Http.Results;
using PeppolSG.API.Logging;

namespace PeppolSG.API.ErrorHandling
{
    public class StructuredError
    {
        public string ErrorId { get; set; }
        public string Message { get; set; }
        public string CorrelationId { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string Details { get; set; }
    }

    /// <summary>
    /// Converts unhandled exceptions into structured JSON with correlation ID.
    /// </summary>
    public class GlobalExceptionHandler : ExceptionHandler
    {
        public override Task HandleAsync(ExceptionHandlerContext context, CancellationToken cancellationToken)
        {
            var request = context.ExceptionContext.Request;
            var corrId = request.Headers.Contains("X-Correlation-Id") ? string.Join(",", request.Headers.GetValues("X-Correlation-Id")) : Guid.NewGuid().ToString();
            var errorId = Guid.NewGuid().ToString();
            var error = new StructuredError
            {
                ErrorId = errorId,
                Message = "An unexpected error occurred",
                CorrelationId = corrId,
#if DEBUG
                Details = context.Exception.ToString()
#else
                Details = null
#endif
            };
            var response = request.CreateResponse(HttpStatusCode.InternalServerError, error);
            response.Headers.Add("X-Correlation-Id", corrId);
            context.Result = new ResponseMessageResult(response);
            StructuredLogger.Error("Unhandled exception", null, context.Exception);
            AlertingService.SendCritical("Unhandled exception", context.Exception.ToString());
            return Task.CompletedTask;
        }
    }
} 