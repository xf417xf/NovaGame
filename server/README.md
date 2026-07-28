# NovaGame Server

The server is a six-layer .NET 8 application with dedicated Deployment and DbMigrator composition projects, built on locally packaged NovaCore framework packages. MySQL 8.4 through Pomelo is the only supported database provider.

## Prepare Dependencies

From the repository root:

```powershell
pwsh -NoProfile -File scripts/bootstrap.ps1 -Scope Database
```

This packages the pinned NovaCore source into the canonical ignored `.novacore/feeds/<commit>` feed, mirrors a compatibility feed at `.novacore/packages`, and creates ignored `deploy/.env` and `server/.env.local` files when they are missing. The server file contains a generated JWT signing key; administrator seed user name and password values remain empty unless explicitly configured together.

## Start MySQL And The API

```powershell
pwsh -NoProfile -File scripts/run-dev.ps1 -Scope Server
```

The script starts MySQL, waits for its health check, loads ignored administrator configuration, runs DbMigrator for migrations and optional seed data, and then runs the API without printing credentials. Liveness is available at `/health/live`; readiness is available at `/health/ready`, `/health`, and `/api/health`.

To create the first administrator, set both seed values in `server/.env.local` before the first development run. Leaving both empty disables administrator seeding. Existing administrators are never overwritten by later seed changes.

## Migrations

Restore the pinned EF tool, then run migration commands from `server/` with `NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING` supplied through the environment:

```powershell
dotnet tool restore --configfile NuGet.Config
dotnet tool run dotnet-ef migrations list --project src/NovaGame.Host.EntityFrameworkCore --startup-project src/NovaGame.Host.EntityFrameworkCore
dotnet tool run dotnet-ef migrations add <MigrationName> --project src/NovaGame.Host.EntityFrameworkCore --startup-project src/NovaGame.Host.EntityFrameworkCore
dotnet run --project src/NovaGame.DbMigrator
```

EF design-time commands use `NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING`. Create a reviewed migration explicitly when the model changes. Runtime migration and configured seeding use DbMigrator; the API never mutates schemas during startup.

Changing credentials in `deploy/.env` does not change an existing MySQL data volume. Stop the runtime and handle that volume explicitly when a credential reset is intended; repository scripts never delete the development volume automatically.

## Transaction Retry Policy

Pomelo execution-strategy retries are intentionally disabled. NovaCore currently opens explicit transactions outside an EF replay delegate, so provider retries cannot safely replay the complete application operation. Transient failures are returned to the caller, and retryable commands must be idempotent. Do not enable `EnableRetryOnFailure` unless the transaction boundary is redesigned and verified without changing this policy silently.

## Verification

```powershell
pwsh -NoProfile -File ../scripts/verify.ps1 -Scope Database
pwsh -NoProfile -File ../scripts/verify.ps1 -Scope Authentication
```
