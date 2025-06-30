using System;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using System.Linq;
using log4net;

namespace PeppolSG.API.Filters
{
    public class CorrelationIdActionFilter : ActionFilterAttribute
    {
        private const string Header = "X-Correlation-Id";
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (!actionContext.Request.Headers.Contains(Header))
                actionContext.Request.Headers.Add(Header, Guid.NewGuid().ToString());
            var corrId = actionContext.Request.Headers.GetValues(Header).First();
            actionContext.Request.Properties[Header] = corrId;
            log4net.ThreadContext.Properties["corr"] = corrId;
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            if (actionExecutedContext.Request.Properties.TryGetValue(Header, out var idObj))
            {
                var id = idObj as string;
                if (!actionExecutedContext.Response.Headers.Contains(Header))
                    actionExecutedContext.Response.Headers.Add(Header, id);
            }
            log4net.ThreadContext.Properties.Remove("corr");
        }
    }
} 