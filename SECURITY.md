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

- The service listens on `127.0.0.1` only (8010 AVR API, 8011 admin API); nginx is the only way in.
- Both nginx sites only accept the networks in `/etc/nginx/zcast-allow.conf` (the LAN detected by `install.sh`),
  even if port 80 is forwarded to the server for other services.
- AVRs use plain HTTP, so the vTuner endpoints can't be protected with HTTPS or authentication.
- The Swagger UI (`/swagger`) is reachable only on the server itself in the default deployment.

### Admin page

- Own nginx site on port 8090: allowlist + basic auth (`/etc/nginx/zcast-admin.htpasswd`, APR1 hash) + HTTPS with a
  self-signed certificate. **Never forward port 8090 on the router.**
- The admin API answers only on its own port (404 elsewhere, and `/admin` is blocked on the AVR site).
- Cross-site request protection: state-changing requests need the `X-Zcast-Admin: 1` header and a matching `Origin`.
- Responses carry `Content-Security-Policy: default-src 'self'`, `nosniff`, `no-referrer` and `no-store`.
- The data file is written by the unprivileged service user only (`/var/lib/zcast`, systemd `DynamicUser`).
- "Test stream" makes the server request a URL entered by a logged-in admin (http/https only, 8 s timeout; for
  https:// also the http:// variant). Certificates are validated normally; TLS errors are only reported.
