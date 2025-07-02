using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using log4net;
using log4net.Config;

namespace PeppolSG.API
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(WebApiApplication));

        protected void Application_Start()
        {
            try
            {
                // Initialize Log4Net
                XmlConfigurator.Configure();
                log.Info("=== Peppol AS4 Access Point Starting ===");

                // Standard ASP.NET initialization
                AreaRegistration.RegisterAllAreas();
                GlobalConfiguration.Configure(WebApiConfig.Register);
                FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
                RouteConfig.RegisterRoutes(RouteTable.Routes);
                BundleConfig.RegisterBundles(BundleTable.Bundles);

                // Create required directories
                CreateRequiredDirectories();

                // Validate configuration
                ValidateConfiguration();

                log.Info("=== Peppol AS4 Access Point Started Successfully ===");
            }
            catch (Exception ex)
            {
                if (log != null)
                {
                    log.Fatal("Failed to start Peppol AS4 Access Point", ex);
                }
                throw;
            }
        }

        protected void Application_Error()
        {
            var exception = Server.GetLastError();
            if (exception != null)
            {
                log.Error("Unhandled application error", exception);
            }
        }

        protected void Application_End()
        {
            log.Info("=== Peppol AS4 Access Point Shutting Down ===");
        }

        private void CreateRequiredDirectories()
        {
            var directories = new[]
            {
                "~/App_Data/certificates",
                "~/App_Data/messages",
                "~/App_Data/metadata", 
                "~/App_Data/logs"
            };

            foreach (var dir in directories)
            {
                var physicalPath = Server.MapPath(dir);
                if (!Directory.Exists(physicalPath))
                {
                    Directory.CreateDirectory(physicalPath);
                    log.Info($"Created directory: {physicalPath}");
                }
            }
        }

        private void ValidateConfiguration()
        {
            var requiredSettings = new[]
            {
                "PeppolP12FilePath",
                "PeppolP12Password", 
                "PeppolDomain",
                "PeppolAccessPointId",
                "SmpDomain"
            };

            var missingSettings = new List<string>();

            foreach (var setting in requiredSettings)
            {
                var value = System.Configuration.ConfigurationManager.AppSettings[setting];
                if (string.IsNullOrWhiteSpace(value) || value.Contains("YOUR_") || value.Contains("XXXXXXXX"))
                {
                    missingSettings.Add(setting);
                }
            }

            if (missingSettings.Any())
            {
                var message = $"Missing or incomplete configuration settings: {string.Join(", ", missingSettings)}";
                log.Warn(message);
                log.Warn("Please update Web.config with proper Peppol configuration values");
            }
            else
            {
                log.Info("Configuration validation passed");
            }
        }
    }
}
