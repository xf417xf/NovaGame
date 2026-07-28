# NovaGame Admin Instructions

These instructions extend the repository-level `AGENTS.md` for files under `admin/`.

## Required Reading

Before admin work, read:

- `docs/architecture.md`;
- `docs/workflows/development.md`;
- `docs/workflows/configuration-release.md` for configuration operations;
- `docs/goals/m0.5.md` when working on the active milestone.

## Technology Baseline

- Vben Admin 5
- `web-ele` application variant
- Vue 3
- Element Plus
- TypeScript with strict type checking

Do not introduce the `web-antd` variant. Initialize from the fixed local template identified by `VBEN_TEMPLATE_ROOT`, preserve its license, and preserve its declared package-manager and runtime constraints.

## Product Boundary

The admin is an operational client of NovaGame APIs. It must not contain domain rules, access the database directly, or become the source of truth for validation.

Client-side validation improves operator feedback but never replaces server validation. Use typed API modules and generated contracts where practical.

## Interface Rules

- Build the working operations surface as the first authenticated screen.
- Use a compact, quiet, task-focused layout suitable for repeated operations.
- Prefer filters, tables, drawers, dialogs, status indicators, and explicit commands over marketing sections or decorative dashboards.
- Do not build card mosaics, hero sections, or feature-tour copy for routine admin workflows.
- Keep one clear accent color and restrained surface hierarchy.
- Provide loading, empty, error, unauthorized, forbidden, conflict, and retry states.
- Keep controls stable and ensure tables and forms remain usable at supported desktop and mobile widths.
- Do not expose unfinished Mail, Ranking, Shop, Activity, or gameplay navigation entries.

## Authentication And Permissions

- Use the server-issued access token for API authorization.
- Use the server-managed secure refresh-token flow; do not invent a second authentication system.
- Clear local authenticated state after logout or unrecoverable refresh failure.
- Route visibility improves navigation but does not replace server authorization.
- Use permission codes returned by the server to filter static routes and actions.

## Configuration Operations

Reflect the server lifecycle exactly: Draft, Validate, Review, Publish, Export, Activate, Monitor, and Rollback. Do not let the UI edit a published immutable version in place.

JSON editing must provide syntax validation, formatting, clear server validation feedback, and version-conflict handling.

## Verification

Run the template's formatter, lint, type-check, unit-test, and production-build commands. Add browser coverage for login, permission filtering, configuration publication, activation, rollback, conflict handling, and logout as those capabilities are implemented.

Do not claim an integration is verified when it was exercised only with the Vben mock backend.
