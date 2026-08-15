# Tnzi.NET

> A modular .NET 10 application framework built on ASP.NET Core and Entity Framework Core.
> Capabilities ship as composable modules, so application code stays business code.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Docs](https://img.shields.io/badge/docs-tnzi.cc-2ea44f.svg)](https://tnzi.cc)

## What this is

Everything a back-office system needs but nobody wants to write twice — authentication,
authorization, file storage, templating, notifications, auditing, chat, payments,
double-entry accounting, e-signature, an AI agent runtime — packaged as modules you opt into,
plus a matching Vue 3 admin component library.

An application declares which modules it wants. The framework handles load order, dependency
resolution, database mapping, API surface and the frontend contract.

```csharp
[DependsOn(
    typeof(IdentityModule), typeof(AuthorizationModule),
    typeof(StorageModule), typeof(NotificationModule), typeof(AuditModule)
)]
public class StartupModule : HostingModule
{
    public override string? TableNamePrefix => "MyApp";
}

// Program.cs
await TnziApp.RunAsync<StartupModule>(args);
```

That gives you a Web API with authentication, a permission matrix, file upload, in-app
notifications and operation auditing — with Swagger grouping, table prefixes and the
permission catalogue wired up automatically.

## Getting started

NuGet consumers don't list packages one by one. `Tnzi.Hosting` ships MSBuild targets that
resolve the modules you name:

```xml
<PropertyGroup>
  <TnziVersion>0.1.1</TnziVersion>
  <TnziModules>Identity;Authorization;Storage;Finance.Payroll</TnziModules>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Tnzi.Hosting" Version="$(TnziVersion)" />
</ItemGroup>
```

Building from source:

```bash
dotnet build Tnzi.NET.slnx
dotnet test

cd src/Tnzi.UI
pnpm install
pnpm build
pnpm test
```

## Modules

45 .NET projects. Modules declare hard dependencies with `[DependsOn]`
and soft ones with `[OptionalDependsOn]` — the latter are used when present and silently
skipped when not.

| Layer | Modules |
|---|---|
| **Core** | `Tnzi` — base types, utilities, caching, event bus, result types, exception hierarchy |
| **Framework** | `AspNetCore` · `EFCore` · `Mapster` · `Localization` · `Swagger` · `SignalR` |
| **Infrastructure** | `Redis` · `RabbitMQ` · `Kafka` · `Hangfire` · `Logging` · `OpenTelemetry` · `Performance` · `HealthChecks` · `Imaging` · `Feature` |
| **Business** | `Identity` (+`Presence`) · `Authorization` · `Storage` · `Template` · `Notification` · `System` · `Audit` · `Chat` · `Payment` · `Signing` · `Documents` |
| **Finance** | `Finance` plus six sub-modules: `Payroll` · `Banking` (statements, cheques, EFT) · `Recurring` · `Ai` (receipt extraction) · `Documents` (cheque rendering) · `Tax.Ca` |
| **AI** | `AI` (agent runtime, tool pipeline, guardrails) plus seven sub-modules: `Skills` · `Workflow` · `Rag` · `Sandbox` · `Mcp` · `Channels` · `Cli` |
| **Host** | `Hosting` — adaptive host that configures itself from whichever modules are loaded |

Tables are prefixed per module (`Identity_User`, `Finance_JournalEntry`). SQL Server,
PostgreSQL, MySQL and SQLite are supported; entity IDs use sequential GUIDs or snowflake
longs, whichever suits the provider.

## Frontend

`src/Tnzi.UI/` is a pnpm monorepo of five packages:

| Package | Contents |
|---|---|
| `@tnzi/core` | API client, auth and state matching the backend contract. No UI dependency |
| `@tnzi/ui` | General components (on Naive UI) |
| `@tnzi/ui-admin` | Admin console: layout, menu permissions, CRUD page scaffolding, settings centre |
| `@tnzi/ui-ai` | AI chat surface: streaming, tool-call display, workflow editor |
| `@tnzi/mobile` | Mobile components (on Vant) |

## Documentation

Reference documentation lives at **[tnzi.cc](https://tnzi.cc)** — architecture overview,
coding standards, per-module guides and frontend docs.

Public API is annotated with `[StableApi]` and `[ExperimentalApi]`. Types marked
`StableApi` do not take breaking changes within a major version.

## Contributing

**Pull requests opened here cannot be merged** — development happens elsewhere and this
repository is published as periodic snapshots, so a merge here would be overwritten. Please
open an issue instead; that is the channel that reaches the maintainers.

## License

[MIT](./LICENSE)
