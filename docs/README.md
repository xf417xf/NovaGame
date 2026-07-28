# NovaGame Documentation

This directory separates architectural context, repeatable workflows, roadmap context, and executable milestone state.

## Authority Map

- Repository and subtree `AGENTS.md` files contain durable coding-agent instructions.
- [Architecture](architecture.md) defines system boundaries and long-lived design decisions.
- Files under [workflows](workflows/) define repeatable ways of working.
- [Roadmap](roadmap.md) describes milestone direction and exit criteria. It is not authorization to implement future work.
- Files under [goals](goals/) are executable milestone runbooks. A goal with `in_progress` status may drive only its authorized milestone; a completed goal is a verification record, not continuing authorization.
- The current user request remains the authority for the specific task being performed.

M0.5 is complete, and no repository goal is currently active. M1 remains unauthorized roadmap context.

When sources appear to conflict, stop and resolve the conflict before implementation. Do not silently weaken a repository rule or an active goal acceptance criterion.

## Core Documents

- [Architecture](architecture.md)
- [Development workflow](workflows/development.md)
- [Extraction workflow](workflows/extraction.md)
- [Configuration release workflow](workflows/configuration-release.md)
- [Roadmap](roadmap.md)
- [M0.5 Framework Bootstrap completed goal record](goals/m0.5.md)
