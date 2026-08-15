# Tnzi.UI

Frontend component monorepo for Tnzi.NET framework.

## Packages

| Package | Description | Status |
|---------|-------------|--------|
| @tnzi/core | Framework-agnostic business logic (services, state, headless controllers) | Stable |
| @tnzi/ui | Base UI components (Naive UI + UnoCSS + headless logic + Pinia stores) | Stable |
| @tnzi/ui-admin | Admin extension (shell, CRUD engine, 90+ built-in admin pages) | Stable |
| @tnzi/ui-ai | AI components (chat, workflow, agent, streaming) | Stable |
| @tnzi/mobile | Mobile components (Vant 4, standalone) | Stable |

## Architecture

```
@tnzi/core          (foundation)
    ├── @tnzi/ui           (base UI + stores)
    │     ├── @tnzi/ui-admin   (admin extension)
    │     └── @tnzi/ui-ai         (AI extension)
    └── @tnzi/mobile       (standalone mobile)
```

## Quick Start

### Web App
```bash
pnpm add @tnzi/core @tnzi/ui
```

### Admin Dashboard
```bash
pnpm add @tnzi/core @tnzi/ui @tnzi/ui-admin
```

### AI Features
```bash
pnpm add @tnzi/core @tnzi/ui @tnzi/ui-ai
```

### Mobile App
```bash
pnpm add @tnzi/core @tnzi/mobile
```

### Core Only (BYO UI)
```bash
pnpm add @tnzi/core
```

## Development

```bash
# Install dependencies
pnpm install

# Build all packages (in dependency order)
pnpm build

# Type check
pnpm typecheck

# Run tests
pnpm test

# Lint (CI runs with --max-warnings 0)
pnpm lint

# Publish a package to npm (maintainers; requires npm auth)
pnpm release <package> [patch|minor|major]
```

## Component Prefix

All Tnzi components use the `T` prefix:

```vue
<template>
    <TLoginForm />
    <TTable />
    <TUserCard />
    <TCrudPage />
</template>
```

## Service Layer

`@tnzi/core` service types are **hand-written**, not generated from an OpenAPI spec. Drift against
the backend is caught by `FrontendBackendContractTests`, which reflects over every controller and
reconciles it with the paths declared in each `api.ts`.

## Documentation

Per-package reference lives on the docs site:

- [@tnzi/core](https://tnzi.cc/docs/modules/ui-core) — HTTP, state, adapters, service contracts
- [@tnzi/ui](https://tnzi.cc/docs/modules/ui) — base components, theming, headless controllers
- [@tnzi/ui-admin](https://tnzi.cc/docs/modules/ui-admin) — shell, CRUD engine, built-in admin pages
- [@tnzi/ui-ai](https://tnzi.cc/docs/modules/ui-ai) — chat, workflow, agent, streaming
- [@tnzi/mobile](https://tnzi.cc/docs/modules/ui-mobile) — Vant 4 components

New to the framework? Start with [Getting started](https://tnzi.cc/docs/getting-started).

In this directory:

- [MIGRATION.md](./MIGRATION.md) · [CHANGELOG.md](./CHANGELOG.md) — breaking changes and
  per-release history; read these when upgrading

## License

[MIT](../../LICENSE) (c) Tnzi.NET
