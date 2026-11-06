# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in Runax.Http, please report it responsibly.

**Do not open a public issue.**

Instead, [open a private security advisory](https://github.com/runax-software/runax-http/security/advisories/new) on GitHub.

Please include:

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

## Response

We will acknowledge your report within 48 hours and aim to provide a fix or mitigation within 7 days for critical issues.

## Supported Versions

| Version | Supported |
| ------- | --------- |
| latest  | Yes       |

## Scope

This policy covers the code in the Runax.Http packages. Third-party dependencies are managed through Central Package Management (`Directory.Packages.props`) and updated regularly.

Note that the underlying HTTP library is chosen by you: a vulnerability in RestSharp, or in any
other transport's SDK, is reportable to that project. What is in scope here is how Runax.Http
uses it — for example a request built from your attributes being sent somewhere other than the
host you declared, credentials or headers crossing from one request or transport to another, or
TLS settings being weakened on your behalf.
