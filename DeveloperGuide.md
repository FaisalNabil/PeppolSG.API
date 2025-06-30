# PeppolSG.API – Developer Guide

## Overview
PeppolSG.API is an ASP.NET 4.8 Web API that implements a Peppol AS4 Access Point with hardened security, reliability and monitoring capabilities.

Key design goals:
1. Standards-compliance with Peppol AS4 profile.
2. Defence-in-depth security (certificate validation, SSL pinning, attachment filtering, XXE protection).
3. High reliability via circuit-breakers, retry with exponential back-off and graceful degradation.
4. Observability through structured logging, correlation-IDs, health endpoints and in-process metrics.
5. Configuration management with validation, hot-reload and encrypted secrets.

## Project Layout
- `PeppolSG.API/` – Web API project (controllers, services, filters).
- `PeppolSG.API.Tests/` – MSTest test-suite.
- `PlanOfAction.md` – two-week implementation roadmap.
- `docs/` – (this folder) project documentation.

## Running Locally
1. Prerequisites: Visual Studio 2022+, .NET Framework 4.8 SDK, Git.
2. Clone repo and open `PeppolSG.API.sln` in Visual Studio.
3. Copy the required Peppol PKI `.p12` file to `certs/` and set its path in *Web.config* (`PeppolP12FilePath`) and encrypt its password with PowerShell:
   ```powershell
   [Convert]::ToBase64String([System.Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes('YOUR_PASSWORD'), $null, 'LocalMachine'))
   ```
   Add the value to `PeppolP12PasswordEncrypted` appSetting.
4. Press F5 – VS will launch IISExpress and open the swagger page.

## REST Endpoints
| Method | Route | Description |
|--------|-------|-------------|
| POST | `/as4` | Receive and process inbound AS4 UserMessage |
| GET  | `/api/health/liveness` | Liveness probe |
| GET  | `/api/health/readiness` | Readiness probe inc. metrics |
| GET  | `/api/messageId/{id}` | Duplicate-detection status |

All responses include the `X-Correlation-Id` header. Supply the same header when calling the API to propagate the correlation chain.

## Error Model
Errors are returned in JSON:
```json
{
  "errorId": "4c4043d7…",
  "message": "Validation failed",
  "correlationId": "f01a…",
  "timestampUtc": "2025-01-01T12:34:56Z",
  "details": "<optional>"
}
```

## Testing
Run all tests from Test Explorer or command-line:
```powershell
vstest.console.exe PeppolSG.API.Tests\bin\Debug\PeppolSG.API.Tests.dll
```
This executes unit, integration, performance, security and compliance suites.

## Contributing
1. Fork and clone.
2. Create feature branch `feature/<name>`.
3. Follow coding conventions (C# 10, PascalCase, DI via constructors).
4. Ensure `dotnet format` passes and all tests are green.
5. Submit pull-request with description and references to PlanOfAction tasks. 