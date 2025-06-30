using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using PeppolSG.API.Filters;
using PeppolSG.API.ErrorHandling;
using System.Web.Http.ExceptionHandling;

namespace PeppolSG.API
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            // Register correlation ID handler/filter
            config.MessageHandlers.Add(new CorrelationIdHandler());
            config.Filters.Add(new CorrelationIdActionFilter());

            // Global exception handler
            config.Services.Replace(typeof(IExceptionHandler), new GlobalExceptionHandler());

            // Metrics handler
            config.MessageHandlers.Add(new MetricsHandler());
        }
    }
}
