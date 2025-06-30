using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Http;
using PeppolSG.API.Service;
using PeppolSG.API.Service.Resilience;
using PeppolSG.API.Logging;

namespace PeppolSG.API.Controllers
{
    [RoutePrefix("api/health")]
    public class HealthController : ApiController
    {
        private static readonly DateTime _started = DateTime.UtcNow;

        [HttpGet]
        [Route("liveness")]
        public IHttpActionResult Liveness()
        {
            return Ok(new { status = "Alive", uptimeSec = (int)(DateTime.UtcNow - _started).TotalSeconds });
        }

        [HttpGet]
        [Route("readiness")]
        public async Task<IHttpActionResult> Readiness()
        {
            // Example readiness: memory, SMP circuit state
            var proc = Process.GetCurrentProcess();
            var memoryMb = proc.WorkingSet64 / (1024 * 1024);
            var readiness = new
            {
                status = "Ready",
                memoryMb,
                os = RuntimeInformation.OSDescription,
                metrics = MetricsService.GetAll(),
                timestampUtc = DateTime.UtcNow
            };
            return Ok(readiness);
        }
    }
} 