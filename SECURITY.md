# Security policy

BricksNet handles credentials (personal access tokens, OAuth tokens) and talks to your data
platform, so security reports are taken seriously.

## Supported versions

BricksNet is in preview. Security fixes go into the latest released version only.

## Reporting a vulnerability

**Do not open a public issue for a security problem.**

Report it privately through GitHub: open the repository's **Security** tab and choose
**Report a vulnerability**
(<https://github.com/jogyamfi/BricksNet/security/advisories/new>).

Please include:

- the BricksNet version (or commit) and the .NET version
- which surface is affected (fluent API, ADO.NET provider, EF Core provider) and which backend
  (REST or Thrift)
- steps to reproduce, or a proof of concept
- what an attacker could do with it

Remove real tokens, hostnames and data from anything you send.

## What to expect

This is a volunteer-maintained project. You should get an acknowledgement within 7 days. Once the
report is confirmed, a fix is prepared privately, released, and then disclosed in a GitHub security
advisory that credits you unless you ask otherwise.

## Scope

In scope: flaws in BricksNet itself, such as credential leaks in logs or exceptions, weakened TLS
validation, SQL injection through the provider's own SQL generation or parameter handling, and
flaws in the OAuth flow.

Out of scope: vulnerabilities in Databricks services (report those to Databricks) and in
third-party dependencies that BricksNet does not make exploitable (report those upstream).
