# Day 9 – Error Handling & Resilience – Detailed Implementation Plan

> Objective: introduce robust error-handling, end-to-end correlation IDs, health endpoints and a circuit-breaker to prevent cascade failures, delivering full system resilience for external calls and internal processing.

## 📅 Work-breakdown (6 subtasks)
| Sub-Task | Code Artefact(s) | Success Criteria |
|----------|-----------------|------------------|
| **ST-9.1** — Circuit-breaker core library | `Resilience/CircuitBreaker.cs` | • Thread-safe half-open implementation with configurable failure window / reset timeout. <br/>• Unit tests covering open→half-open→closed transitions. |
| **ST-9.2** — HTTP client wrapper | `Resilience/CircuitHttpClient.cs` | • Wraps `HttpClient` sending through circuit-breaker. <br/>• Retries delegated to Day-8 logic; breaker trips after N consecutive failures. |
| **ST-9.3** — Correlation-ID propagation | `Filters/CorrelationIdHandler.cs`, `Filters/CorrelationIdActionFilter.cs` | • Incoming requests tagged with GUID (`X-Correlation-Id`). <br/>• Propagated to outbound HTTP requests & log4net MDC. |
| **ST-9.4** — Structured API error filter | `Filters/ApiExceptionFilter.cs` | • Converts unhandled exceptions to JSON `{code,message,correlationId}` (HTTP 500/4xx). <br/>• Maps validation exceptions to 400. |
| **ST-9.5** — Health endpoints | `Controllers/HealthController.cs` | • `GET /api/health/live` returns `200` when app responsive. <br/>• `GET /api/health/ready` adds circuit-breaker + memory + SMP cache checks. |
| **ST-9.6** — Graceful-degradation helpers | `Resilience/FallbackPolicy.cs` + integration | • Optional fallback values supplied to SMP lookup & external calls when circuit open. <br/>• Logged as WARN, not fatal. |

## 🔧 Configuration Keys
```
<add key="CircuitBreakerFailureThreshold" value="5"/>
<add key="CircuitBreakerResetSec" value="60"/>
<add key="ExposeProblemDetails" value="true"/>
```

## 🗂️ Deliverables
1. 6 new C# source files in `Service/Resilience` & `Filters` namespaces.  
2. Health controller with two endpoints & integration in `WebApiConfig`.  
3. Unit test classes: `CircuitBreakerTests`, `CorrelationIdTests`, `HealthControllerTests`.  
4. Documentation update in **PlanOfAction.md** (TASK-033 → 038) and a Day-9 implementation summary once coding completes.

## ⏱️ Estimated effort
| Sub-Task | Est. Hours |
|----------|------------|
| ST-9.1 | 2.0 |
| ST-9.2 | 1.5 |
| ST-9.3 | 1.0 |
| ST-9.4 | 1.0 |
| ST-9.5 | 0.8 |
| ST-9.6 | 1.0 |
| **Total** | **7.3 h** |

## 🚨 Risks & Mitigations
* **Logger correlation contamination** – isolate MDC per request (dispose scope in filter).  
* **Circuit-breaker false opens** – tune thresholds; metric counters.  
* **Health endpoint exposure** – restrict to internal network / auth header in future Day-10 logging tasks.

## ✅ Acceptance Criteria
* All new unit tests green; overall test coverage ≥ 85 %.  
* Health endpoints return expected JSON schema.  
* Correlation-ID echoed in every response header and error body.  
* SMP lookup switches to fallback when circuit open without app crash. 