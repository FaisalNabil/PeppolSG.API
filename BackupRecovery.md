# Backup & Recovery Procedures

## Scope
These procedures cover application data (metadata JSON, payload files) and configuration secrets.

## Data Locations
| Path | Description |
|------|-------------|
| `data/metadata/*.json` | Persisted inbound message metadata |
| `data/payloads/{MessageId}/` | Persisted payload binaries |
| `Web.config` | Application settings + encrypted secrets |
| `logs/` | Rolling structured logs (optional to back up) |

## Backup Schedule
| Item | Frequency | Retention | Tool |
|------|-----------|-----------|------|
| Metadata JSON | Hourly | 30 days | robocopy to NAS |
| Payloads | Hourly | 30 days | robocopy to NAS |
| Web.config | Daily | 90 days | git version control / cron copy |

## Backup Verification
Weekly restore test onto staging environment:
1. Provision new VM.
2. Deploy latest application package.
3. Restore backed-up `data/` and `Web.config`.
4. Run health readiness endpoint and random message lookup.

## Disaster Recovery (DR)
1. In major failure, provision clean server via Terraform.
2. Deploy latest release package from artefact store.
3. Restore last good backup.
4. DNS failover to new instance.

Estimated RPO: 1 hour.  
Estimated RTO: 30 minutes. 