using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using PeppolSG.API.Service;
using log4net;
using PeppolSG.API.Configuration;

namespace PeppolSG.API
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(WebApiApplication));

        protected void Application_Start()
        {
            log.Info("PeppolSG.API application starting...");

            try
            {
                // Initialize TLS configuration for secure communication
                TlsConfigurationService.InitializeTlsConfiguration();
                
                // Validate TLS configuration
                var tlsStatus = TlsConfigurationService.ValidateConfiguration();
                if (!tlsStatus.IsSecure)
                {
                    log.Warn($"TLS configuration may not be fully secure: {tlsStatus.SupportedProtocols}");
                }

                // Initialize message ID cleanup service
                MessageIdCleanupService.Initialize();
                MemoryMonitoringService.Initialize();

                // Initialize configuration service and validate
                ConfigurationService.Initialize();

                // Initialize Web API routes and configuration
                AreaRegistration.RegisterAllAreas();
                GlobalConfiguration.Configure(WebApiConfig.Register);
                FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
                RouteConfig.RegisterRoutes(RouteTable.Routes);
                BundleConfig.RegisterBundles(BundleTable.Bundles);

                log.Info("PeppolSG.API application started successfully");
                log.Info($"TLS Configuration: {TlsConfigurationService.GetConfigurationSummary()}");
            }
            catch (Exception ex)
            {
                log.Fatal("Failed to start PeppolSG.API application", ex);
                throw;
            }
        }

        protected void Application_Error()
        {
            var lastError = Server.GetLastError();
            if (lastError != null)
            {
                log.Error("Unhandled application error", lastError);
            }
        }

        protected void Application_End()
        {
            log.Info("PeppolSG.API application shutting down");
        }
    }
}
