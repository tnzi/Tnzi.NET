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

# OpenAPI Codegen
pnpm codegen:url
pnpm codegen
pnpm codegen:check
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

## OpenAPI Codegen (Optional)

当前 `@tnzi/core` 的 service 层类型为**手写维护**。`Tnzi.Cli` 提供了从 OpenAPI spec 自动生成 DTO/API/Schema 的能力，适用于应用项目快速对齐后端 API。

**前提**: 安装 `tnzi` CLI（`dotnet tool install -g Tnzi.Cli`），并在项目根目录有 `tnzi.json` 配置文件（通过 `tnzi init` 创建）。

```bash
# 从运行中的后端生成
pnpm codegen:url

# 从本地 openapi.json 生成
pnpm codegen

# CI 检查（不写入，仅检测漂移）
pnpm codegen:check
```

生成文件输出到 `packages/core/src/services/{module}/generated/` 目录。

## Documentation

**Consumer-facing docs live in [`docs/frontend/`](../../docs/frontend/index.md)** — that tree is
registered in `docs/doc-manifest.yaml`, drift-checked by `/sync-docs`, and served by `Tnzi.Mcp`.
Start there:

- [快速开始](../../docs/frontend/getting-started.md) — install, Vite + UnoCSS setup, first component
- [架构](../../docs/frontend/architecture.md) — five-package layering, dependency direction
- [@tnzi/core 指南](../../docs/frontend/core-packages.md) — HTTP, state, adapters, service contracts
- [CRUD 组件](../../docs/frontend/crud-components.md) — `TCrudPage` / `TCardPage` / `TListShell`
- [组件覆盖](../../docs/frontend/component-override.md) · [排错](../../docs/frontend/troubleshooting.md)

The authoritative source for each package is its own `packages/{name}/CLAUDE.md`; `docs/frontend/`
is synced from those.

Repo-local docs (contributors, not consumers):

- [UI-PACKAGE-GUIDE.md](./UI-PACKAGE-GUIDE.md) — package development conventions
- [PUBLISHING.md](./PUBLISHING.md) — npm publishing guide
- [MIGRATION.md](./MIGRATION.md) · [CHANGELOG.md](./CHANGELOG.md) — historical, per-release

> `USAGE.md` was deleted on 2026-08-01. It documented shadcn-vue + Tailwind (both replaced in
> 2026-04) and referenced ten components that no longer exist, so following it produced code that
> did not compile. `docs/frontend/` supersedes it.

## License

[MIT](LICENSE) (c) Tnzi.NET
