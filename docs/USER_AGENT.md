# User-Agent in the Databricks JDBC Driver

## Overview

The driver produces two distinct user-agent strings depending on context, both built in
`UserAgentManager`. The shared constant values come from `DriverUtil` (driver version) and
`AgentDetector` (AI coding agent detection).

---

## 1. SDK-Managed User Agent

Used for all normal requests. Built via the Databricks SDK `UserAgent` class in `setUserAgent()`,
then retrieved through `getUserAgentString()`, which replaces the `databricks-sdk-java` segment
with `databricks-jdbc-http`.

### Format

```
DatabricksJDBCDriverOSS/<driver-version> databricks-jdbc-http jvm/<jvm-version> os/<os-name> Java/<client-type> [<CustomerApp>/<version>]
```

### Components

| Segment | Source | Example |
|---|---|---|
| Product | `DEFAULT_USER_AGENT` + `DriverUtil.getDriverVersion()` | `DatabricksJDBCDriverOSS/3.4.1` |
| HTTP client | `SDK_USER_AGENT` replaced at output time | `databricks-jdbc-http` |
| JVM info | SDK standard info | `jvm/21.0.3` |
| OS info | SDK standard info | `os/Linux` |
| Client type | `Java/<getClientUserAgent()>` — `SQLExecHttpClient` (SEA) or `THttpClient` (Thrift) | `Java/SQLExecHttpClient` |
| Customer UA | Optional; from `UserAgentEntry` JDBC connection parameter (URL-decoded) | `MyApp/2.1.0` |

---

## 2. Connector Service User Agent

Used specifically for **feature flag requests**, before the client type is resolved (to avoid a
circular dependency). Built manually in `buildUserAgentForConnectorService()`.

### Format

```
DatabricksJDBCDriverOSS/<driver-version> databricks-jdbc-http jvm/<jvm-version> os/<os-name> [<CustomerApp>/<version>] [agent/<agent-name>]
```

### Components

| Segment | Source | Example |
|---|---|---|
| Product | `DEFAULT_USER_AGENT` + `DriverUtil.getDriverVersion()` | `DatabricksJDBCDriverOSS/3.4.1` |
| HTTP client | `JDBC_HTTP_USER_AGENT` literal | `databricks-jdbc-http` |
| JVM version | `System.getProperty("java.version")` | `jvm/21.0.3` |
| OS name | `System.getProperty("os.name")` (spaces → `_`) | `os/Windows_10` |
| Customer UA | Optional; from `UserAgentEntry` JDBC connection parameter (URL-decoded) | `MyApp/2.1.0` |
| AI agent | Optional; from `AgentDetector.detect()` — env var sniffing | `agent/claude-code` |

> **Note:** Client type (`Java/...`) is intentionally omitted from this format.

---

## Customer UserAgent Entry

The `UserAgentEntry` JDBC connection parameter lets callers inject a custom identifier.

- The value may be URL-encoded (decoded with `URLDecoder`).
- Format: `Name/Version` — if no `/` is present, the version defaults to the string `"version"`.
- Invalid values are silently dropped (logged at DEBUG level).

Examples of valid values:

| Parameter value | Resulting segment |
|---|---|
| `MyBITool/4.2` | `MyBITool/4.2` |
| `MyApp%2F3.0` (URL-encoded) | `MyApp/3.0` |
| `MyTool` (no version) | `MyTool/version` |

---

## AI Coding Agent Detection

`AgentDetector` checks well-known environment variables. Detection succeeds only when **exactly
one** matching variable is set (to avoid ambiguous attribution).

| Environment variable | Product string |
|---|---|
| `ANTIGRAVITY_AGENT` | `antigravity` |
| `CLAUDECODE` | `claude-code` |
| `CLINE_ACTIVE` | `cline` |
| `CODEX_CI` | `codex` |
| `CURSOR_AGENT` | `cursor` |
| `GEMINI_CLI` | `gemini-cli` |
| `OPENCODE` | `opencode` |

Agent info is appended as `agent/<product>` and is only included in the **connector service**
user-agent string.

---

## Sample User-Agent Strings

**Minimal — connector service, no extras:**
```
DatabricksJDBCDriverOSS/3.4.1 databricks-jdbc-http jvm/21.0.3 os/Linux
```

**SDK path — SEA client, no customer UA:**
```
DatabricksJDBCDriverOSS/3.4.1 databricks-jdbc-http jvm/21.0.3 os/Linux Java/SQLExecHttpClient
```

**SDK path — Thrift client, no customer UA:**
```
DatabricksJDBCDriverOSS/3.4.1 databricks-jdbc-http jvm/17.0.9 os/Windows_10 Java/THttpClient
```

**SDK path — SEA client with customer UA:**
```
DatabricksJDBCDriverOSS/3.4.1 databricks-jdbc-http jvm/21.0.3 os/Linux Java/SQLExecHttpClient MyBITool/4.2
```

**SDK path — customer UA with no version (defaults to literal `"version"`):**
```
DatabricksJDBCDriverOSS/3.4.1 databricks-jdbc-http jvm/21.0.3 os/Linux Java/SQLExecHttpClient MyTool/version
```

**Connector service — running inside Claude Code:**
```
DatabricksJDBCDriverOSS/3.4.1 databricks-jdbc-http jvm/21.0.3 os/Linux agent/claude-code
```

**Connector service — customer app + Cursor agent:**
```
DatabricksJDBCDriverOSS/3.4.1 databricks-jdbc-http jvm/21.0.3 os/Windows_10 MyBITool/4.2 agent/cursor
```
