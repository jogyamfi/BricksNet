---
applyTo: "**/src/BricksNet/Auth/**/*.cs, **/src/BricksNet/Connection/**/*.cs"
---

# Security & Authentication (BricksNet)

These rules apply to authentication and HTTP connection code. They map directly to the OWASP Top 10 (notably A02 Cryptographic Failures and A07 Identification & Authentication Failures).

## Never Log Secrets

- Never log credential values: `token`, `client_secret`, `code_verifier`, `access_token`, `refresh_token`. Use `"***"` placeholders.

  ```csharp
  LogMessages.TokenAcquired(_logger, "***");
  ```

## Handle Secrets Carefully in Memory

- Do not keep credentials in `string` fields longer than necessary. For sensitive byte buffers, clear them explicitly:

  ```csharp
  var verifier = RandomNumberGenerator.GetBytes(96);
  try { /* use verifier */ }
  finally { CryptographicOperations.ZeroMemory(verifier); }
  ```

## PKCE (RFC 7636)

- Generate `code_verifier` with `RandomNumberGenerator.GetBytes(96)` (cryptographic randomness only — never `System.Random`). Compute `code_challenge = Base64Url(SHA256.HashData(verifierBytes))`. Use each pair once per authorization request.

  ```csharp
  byte[] verifierBytes = RandomNumberGenerator.GetBytes(96);
  string codeVerifier = Base64Url(verifierBytes);
  string codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
  ```

## CSRF Protection

- Generate a random `state` for every OAuth redirect and validate it on the callback **before** exchanging the code.

  ```csharp
  string state = RandomNumberGenerator.GetHexString(32);
  // on callback:
  if (!CryptographicOperations.FixedTimeEquals(
          Encoding.ASCII.GetBytes(returnedState), Encoding.ASCII.GetBytes(state)))
      throw new BricksNetException("OAuth state mismatch (possible CSRF).", sqlState: null, errorCode: 0);
  ```

## OAuth Redirect Listener

- Bind the loopback `HttpListener` only to `127.0.0.1` (never `0.0.0.0`), use an ephemeral port (8020 with fallback scan to 8029), and close the listener immediately after receiving a single callback.

## TLS

- Never set `ServerCertificateCustomValidationCallback` to always return `true`. If `AllowSelfSignedCerts` is enabled, log a `Warning`, validate only the certificate chain (not the hostname), and add a comment describing the risk.

  ```csharp
  // SECURITY: self-signed certs disable hostname verification. Development only.
  handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
      errors == SslPolicyErrors.RemoteCertificateNameMismatch; // chain still validated
  ```

- Always use HTTPS. Validate the scheme at connection time and throw if `http://` is supplied.

## Token Persistence

- Never persist tokens to disk in plaintext. Use `ProtectedData.Protect` (Windows DPAPI) or `IDataProtector` (ASP.NET Core Data Protection).

## HTTP Headers

- Use `Authorization: Bearer <token>`, not `Authorization: Token <token>`.
- Prevent header injection: reject `\r` and `\n` in any value written to an HTTP header.

  ```csharp
  if (value.AsSpan().IndexOfAny('\r', '\n') >= 0)
      throw new ArgumentException("Header value contains CR/LF.", nameof(value));
  ```

## Input Validation

- Validate `Host` contains no path components. Validate `HttpPath` starts with `/sql/`.

## Cryptographic Primitives (OWASP A02)

- Use static, allocation-free crypto APIs: `SHA256.HashData(...)` (not `new SHA256Managed()`), `HMACSHA256.HashData(...)` for HMAC.
