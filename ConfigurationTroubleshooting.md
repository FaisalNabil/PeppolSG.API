# Configuration & Troubleshooting

## Configuration Matrix
| Setting | Development | Test | Production |
|---------|-------------|------|------------|
| APP_ENV | Development | Test | Production |
| IsTestEnvironment | true | true | false |
| PeppolP12FilePath | certs/test.p12 | certs/test.p12 | E:\\secrets\\prod.p12 |
| PeppolP12PasswordEncrypted | Generated for env | Generated | Generated |

## Common Issues
| Symptom | Cause | Resolution |
|---------|-------|------------|
| Startup fails: "Missing mandatory configuration keys" | Key absent or blank | Add key to Web.config or environment variables. |
| 500 with `errorId`, `correlationId` | Unhandled exception | Search structured logs by correlationId to trace the request. |
| SMP lookup falls back to stale cache | External SMP unreachable | Check network, certificates, DNS; ensure failover domains configured. |
| TLS warning during startup | Outdated protocols enabled | Verify `TlsConfigurationService.ValidateConfiguration()` reports only TLS 1.2+. |

## Encrypting Secrets
Use PowerShell on deployment host:
```powershell
$plain = Read-Host -AsSecureString
$bytes = [System.Text.Encoding]::UTF8.GetBytes(( [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($plain)) ))
$enc = [Convert]::ToBase64String([System.Security.Cryptography.ProtectedData]::Protect($bytes,$null,'LocalMachine'))
Write-Output $enc
```
Paste the output into `PeppolP12PasswordEncrypted` appSetting.

## Hot-Reload
When Web.config changes, `ConfigurationService` reloads settings after ~3 s. Follow IIS best-practice: `app_offline.htm` not needed; edits are non-blocking.

## Support Procedure
1. Collect `errorId`, `correlationId`, request route.
2. Search logs for correlation chain.
3. If bug suspected, reproduce locally with same inputs and enable `DEBUG` to view `details` field in error JSON. 