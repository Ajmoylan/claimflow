# Security policy

Please report vulnerabilities privately through
[private vulnerability reporting](https://github.com/Ajmoylan/claimflow/security/advisories/new),
not in a public issue.

## Design

CLAIMFLOW is a public, anonymous demo that holds only synthetic data.

- **No accounts, no secrets.** There is no sign-in and no admin API. In Azure, the app reaches its database
  with a managed identity (Entra ID only), so no password or connection-string secret exists.
- **Bounded writes.** Rate limits per client and in total, a 16 KB request body limit, and a hard cap on
  visitor-created claims. There is no public reset endpoint: the demo data is restored automatically every
  night at 03:00 Oslo time, checked before every API request so it also works after the app has slept.
- **Validated input.** Every request is validated (data annotations plus business rules) before it reaches
  the database. Queries are parameterised by EF Core, and search wildcards are escaped.
- **Safe errors.** Failures return RFC 9457 ProblemDetails without stack traces, SQL or type names.
- **Browser hardening.** A strict Content-Security-Policy (same-origin only), `X-Content-Type-Options`,
  `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and HSTS in production.
- **Logs without personal data.** Structured logs record claim references and status changes, never
  descriptions or client IP addresses.

## Supply chain

GitHub Actions are pinned to full commit SHAs with minimal `permissions`. CodeQL, dependency review,
Dependabot, secret scanning with push protection, and NuGet/npm vulnerability audits run in CI.
