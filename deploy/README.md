# Deployment

This directory owns local infrastructure and deployment artifacts introduced by an active milestone.

M05-03 provides the supported Docker MySQL 8.4 development composition. Bootstrap creates ignored local credentials, `run-dev.ps1` starts the persistent development database, and database verification uses an isolated disposable Compose project.

Do not add speculative production orchestration during M0.5. Repository scripts never remove the persistent `novagame-dev` volume automatically.
