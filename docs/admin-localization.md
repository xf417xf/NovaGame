# Admin language selection

NovaGame's generic admin supports English and Simplified Chinese. The login
toolbar has a language menu. After signing in, use the visible **Language**
button in the top-right header and select the Simplified Chinese option; its label then becomes
the localized equivalent. Select **English** to switch back. The selected
language is stored in the current browser's existing application preferences
and restored after reload. English remains the default for a fresh browser.

The authenticated language control belongs to the NovaGame application and is
available even when the Vben preference drawer is disabled. It uses the existing
locale loader, including Element Plus labels and pagination. Route metadata
stores translation keys so menus, tabs, and breadcrumbs follow language changes.

The localized operations surface includes administrator accounts, custom roles
and registered permissions, session families, audit history, and configuration
drafts, review, publication, versions, activation, and rollback. The locale
catalogs are `admin/apps/web-ele/src/locales/langs/en-US/operations.json` and
`admin/apps/web-ele/src/locales/langs/zh-CN/operations.json`. Keep their keys and
interpolation parameters identical when adding user-facing messages.

Framework permission labels, API errors, configuration validation issues, and
JSON syntax diagnostics use stable codes for Chinese presentation in
`zh-CN/framework.json`. Preserve the original API error, status, code, and field
details. Unknown diagnostics fall back to the original message. Custom names,
identifiers, permission codes, environment names, configuration values, and
release notes remain user data and are displayed as supplied.

Language changes do not reload the page or reset editor contents. Transient
notifications retain the language used when the operation occurred; new
notifications use the selected language. Retained command feedback uses deferred
translation where applicable. Modal drawers block the header while open, as they
do for other navigation controls.

Use the repository's pinned admin toolchain. Run `scripts/verify.ps1 -Scope Admin`
for the cumulative generic admin checks. The direct type-check/build commands
inside that verifier deliberately avoid Turbo's unsupported external-consumer
workspace discovery. `operations.test.ts` verifies both catalogs, the independent
language control, reactive navigation/feedback, editor preservation, known code
translations, and unknown-diagnostic fallback. `preferences.test.ts` covers
startup locale restoration, storage isolation, and corrupt-value fallback.
For a local browser smoke check, sign in to the
running preview, switch to Chinese, visit all five workspaces, open creation
drawers without submitting a valid mutation, search the permission catalog using
a translated label, switch back to English, and reload to check persistence.

The 2026-10-02 maintenance verification passed `scripts/verify.ps1 -Scope Admin`,
including documentation and repository checks, server builds, 210 real-MySQL
tests, authentication and GameConfig prerequisites, formatting, lint, application
and E2E type checks, all 529 unit tests, the production build, and all five
real-server browser tests with zero retries. Disposable test resources were
removed by the verifier. Earlier attempts were interrupted by a preview API
build lock and the unchanged host capacity guard; the successful run followed
preview suspension and resource release.

A separate real Edge browser smoke check against the restored HTTP preview
verified all five Chinese workspaces, creation-form validation, translated
permission search, reload persistence, switching back to English, and the visible
language control at 390px width, with no page errors. The first cold-preview
attempt encountered a Vite dependency-optimization reload; the warmed run passed.
