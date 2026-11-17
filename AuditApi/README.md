# Audit Report API

This ASP.NET Core API accepts the JSON report emitted by `ServerSecurityAudit.ps1` and serves it back by report ID. It does not expose a SQL query endpoint.

## Network layout

```text
Client -- HTTPS/443 --> API/reverse proxy -- HTTPS --> API host -- private SQL connection --> SQL Server
```

Allow client networks to reach only the API's HTTPS listener. The example uses port 7165 for the API host; a reverse proxy can expose it on port 443. On the SQL Server firewall, allow the API host's fixed address to reach the SQL Server listener (normally TCP 1433); do not allow client subnets or the public Internet. If no host-to-host SQL listener path is allowed at all, run the API alongside SQL Server or arrange an approved private route—the API cannot access a remote SQL instance without some database transport.

Terminate HTTPS at the API or a trusted reverse proxy. Restrict proxy access to known clients and configure the proxy to forward requests only to the API host. The API requires SQL encryption and certificate validation.

## Configure and run

1. Apply `schema.sql` to the target database using a DBA-managed deployment account.
2. Create a dedicated SQL identity with only `SELECT` and `INSERT` on `dbo.AuditReports`; do not use `db_owner` or an administrator account.
3. Install the .NET 10 ASP.NET Core runtime on the API host.
4. Configure `AUDIT_SQL_CONNECTION_STRING` in the API host's secret store. Example:

   ```text
   Server=sql.internal.example,1433;Database=ServerSecurityAudit;User ID=audit_api;Password=<secret>;Encrypt=True;TrustServerCertificate=False
   ```

   Windows integrated authentication is also supported by `Microsoft.Data.SqlClient`; in that case, run the API under a dedicated domain service identity.
5. Configure `AUDIT_API_TOKEN` through the host's secret store with a randomly generated secret of at least 32 characters. Never commit either setting.
6. From `AuditApi/`, run `dotnet run --urls https://0.0.0.0:7165` with a trusted TLS certificate configured for the host. For production, run behind a managed HTTPS reverse proxy or configure Kestrel with the deployment certificate. Do not expose the development certificate or development server publicly.

## Endpoints

- `GET /health/live` — process liveness.
- `GET /health/ready` — returns `200` when SQL is reachable, otherwise `503`.
- `POST /api/audit-reports` — authenticated; accepts a report with `RiskScore` (0-100), `RiskLevel` (`A`-`F`, excluding `E`), and `Findings` array. Maximum body size is 2 MiB. Returns `201` and a report ID.
- `GET /api/audit-reports/{id}` — authenticated; returns the stored report JSON or `404`.

Data endpoints require `Authorization: Bearer <AUDIT_API_TOKEN>`. Use a separate identity provider and per-client authorization for multi-user or Internet-facing deployments; the shared-token handler is intended as a minimal deployment baseline, not a full identity platform. Audit reports may contain sensitive system details, so restrict API access and database access, encrypt backups, and define a retention policy.

Example PowerShell upload:

```powershell
$token = $env:AUDIT_API_TOKEN
$report = Get-Content .\Audit_Data_YYYYMMDD_HHMM.json -Raw
Invoke-RestMethod `
  -Uri "https://api.internal.example/api/audit-reports" `
  -Method Post `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body $report
```
