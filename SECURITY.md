# Security Policy

Meridian is a local static-analysis tool. It should not execute analyzed application code to build a graph.

## Supported versions

Meridian is currently in alpha. Security fixes should target the latest prerelease until a stable version exists.

| Version | Supported |
| --- | --- |
| `0.x-alpha` | Best effort |
| Stable | Not released yet |

## Reporting a vulnerability

Please report security issues privately through GitHub Security Advisories if available for the repository.

Do not publish exploit details before maintainers have had time to investigate.

## Security expectations

Meridian should:

- treat analyzed repositories as untrusted input,
- avoid executing project code during analysis,
- avoid loading arbitrary plugins from analyzed repositories by default,
- avoid sending source code to external services without explicit user action,
- avoid writing outside the configured output directory,
- redact or skip sensitive files when future file scanning features are added.

## Out of scope

The following are usually not security vulnerabilities by themselves:

- inaccurate graph edges in alpha versions,
- unsupported project types,
- unsupported reflection patterns,
- performance issues without denial-of-service impact.
