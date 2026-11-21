# Security Policy

## Supported versions

Only the latest release on the `main` branch receives security fixes.

| Version | Supported |
|---|---|
| 1.x (latest) | ✅ |
| older | ❌ |

## Reporting a vulnerability

**Please don't report security problems in public issues.**

Use GitHub's private reporting instead: go to the repository's **Security** tab and click
**Report a vulnerability** (or open https://github.com/zkarolyi/zCast/security/advisories/new).

Please include:

- a description of the problem and its impact
- steps to reproduce, or a proof of concept
- the affected version or commit

You should get a first response within 7 days. Once a fix is available, the advisory is published
and the reporter is credited, unless you'd rather stay anonymous.

## Deployment notes

zCast is designed to run **inside your home network** and must not be exposed to the internet:

- The service listens on `127.0.0.1:8010` only; nginx forwards just the vTuner host names to it.
- AVRs use plain HTTP, so the vTuner endpoints can't be protected with HTTPS or authentication.
- Don't forward port 80 from your router to the zCast server.
- The Swagger UI (`/swagger`) is reachable only on the server itself in the default deployment.
