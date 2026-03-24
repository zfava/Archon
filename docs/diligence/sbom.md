# ArchonAI — Software Bill of Materials (SBOM)

**Generated:** 2026-03-24

---

## 1. Backend NuGet Packages (27 unique)

| Package | Version | License | Purpose |
|---------|---------|---------|---------|
| dbup-postgresql | 6.0.3 | MIT | PostgreSQL database migration framework |
| Fido2.Models | 3.0.1 | MIT | WebAuthn/FIDO2 authentication models |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.0 | MIT | JWT bearer token authentication middleware |
| Microsoft.Extensions.Diagnostics.HealthChecks | 10.0.0 | MIT | Health check infrastructure for ASP.NET Core |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.0 | MIT | Hosting abstractions for .NET generic host |
| Microsoft.Extensions.Logging.Abstractions | 10.0.0 | MIT | Logging abstractions for .NET |
| Microsoft.IdentityModel.Protocols.OpenIdConnect | 8.0.2 | MIT | OpenID Connect protocol support |
| NATS.Client | 1.1.8 | Apache 2.0 | NATS messaging client for event bus |
| Npgsql | 9.0.2 | PostgreSQL License | PostgreSQL database driver for .NET |
| OpenTelemetry.Api | 1.12.0 | Apache 2.0 | OpenTelemetry API for distributed tracing |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | 1.9.0-beta.1 | Apache 2.0 | Prometheus metrics exporter for ASP.NET Core |
| OpenTelemetry.Exporter.Prometheus.HttpListener | 1.9.0-beta.1 | Apache 2.0 | Prometheus metrics exporter via HTTP listener |
| OpenTelemetry.Extensions.Hosting | 1.9.0 | Apache 2.0 | OpenTelemetry hosting extensions |
| OpenTelemetry.Instrumentation.AspNetCore | 1.9.0 | Apache 2.0 | ASP.NET Core instrumentation for OpenTelemetry |
| OpenTelemetry.Instrumentation.Http | 1.9.0 | Apache 2.0 | HTTP client instrumentation for OpenTelemetry |
| OpenTelemetry.Instrumentation.Process | 1.10.0-beta.1 | Apache 2.0 | Process metrics instrumentation |
| OpenTelemetry.Instrumentation.Runtime | 1.9.0 | Apache 2.0 | .NET runtime metrics instrumentation |
| Otp.NET | 1.4.0 | MIT | TOTP/HOTP one-time password generation |
| Pgvector | 0.3.0 | MIT | pgvector extension support for vector similarity search |
| Polly | 7.2.4 | BSD 3-Clause | Resilience and transient-fault-handling library |
| Polly.Extensions.Http | 3.0.0 | BSD 3-Clause | HTTP-specific Polly resilience policies |
| Serilog.AspNetCore | 8.0.2 | Apache 2.0 | Serilog integration for ASP.NET Core |
| Serilog.Extensions.Hosting | 8.0.0 | Apache 2.0 | Serilog hosting extensions |
| Serilog.Settings.Configuration | 8.0.2 | Apache 2.0 | Serilog configuration from appsettings.json |
| Serilog.Sinks.Console | 5.0.1 | Apache 2.0 | Serilog console output sink |
| System.IdentityModel.Tokens.Jwt | 8.0.2 | MIT | JWT token creation and validation |
| Yarp.ReverseProxy | 2.2.0 | MIT | Reverse proxy toolkit for API gateway |

## 2. Frontend npm Packages

### Production Dependencies (4)

| Package | Version | License | Purpose |
|---------|---------|---------|---------|
| @microsoft/signalr | ^10.0.0 | MIT | SignalR client for real-time WebSocket communication |
| react | ^19.2.4 | MIT | UI component library |
| react-dom | ^19.2.4 | MIT | React DOM rendering |
| react-router-dom | ^7.13.1 | MIT | Client-side routing for React |

### Dev Dependencies (16)

| Package | Version | License | Purpose |
|---------|---------|---------|---------|
| @eslint/js | ^9.39.4 | MIT | ESLint JavaScript rules |
| @testing-library/jest-dom | ^6.9.1 | MIT | Custom Jest DOM matchers |
| @testing-library/react | ^16.3.2 | MIT | React component testing utilities |
| @types/node | ^24.12.0 | MIT | TypeScript definitions for Node.js |
| @types/react | ^19.2.14 | MIT | TypeScript definitions for React |
| @types/react-dom | ^19.2.3 | MIT | TypeScript definitions for React DOM |
| @vitejs/plugin-react | ^6.0.0 | MIT | Vite plugin for React |
| eslint | ^9.39.4 | MIT | JavaScript linter |
| eslint-plugin-react-hooks | ^7.0.1 | MIT | ESLint rules for React hooks |
| eslint-plugin-react-refresh | ^0.5.2 | MIT | ESLint plugin for React refresh |
| globals | ^17.4.0 | MIT | Global identifiers for ESLint |
| jsdom | ^29.0.1 | MIT | DOM implementation for testing |
| typescript | ~5.9.3 | Apache 2.0 | TypeScript compiler |
| typescript-eslint | ^8.56.1 | MIT | TypeScript ESLint integration |
| vite | ^8.0.0 | MIT | Frontend build tool |
| vitest | ^4.1.0 | MIT | Vite-native test framework |

## 3. Summary

| Metric | Count |
|--------|-------|
| Total backend dependencies | 27 |
| Total frontend dependencies | 20 (4 production + 16 development) |
| **Grand total** | **47** |

### License Breakdown

| License | Backend | Frontend | Total |
|---------|---------|----------|-------|
| MIT | 13 | 19 | 32 |
| Apache 2.0 | 10 | 1 | 11 |
| BSD 3-Clause | 2 | 0 | 2 |
| PostgreSQL License | 1 | 0 | 1 |
| **Total** | **27** | **20** | **47** |

### Compliance Notes

- **No GPL or AGPL dependencies:** Confirmed. All dependencies use permissive licenses compatible with commercial use.
- **Pre-release packages:** Two OpenTelemetry exporters and one instrumentation package are pre-release (beta). These should be tracked for stable releases.
