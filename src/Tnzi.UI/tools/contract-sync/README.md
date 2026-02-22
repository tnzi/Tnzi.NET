# Contract Sync

`contract-sync` is a guardrail for backend/frontend contract drift.

## Commands

1. `pnpm -C src/Tnzi.UI contract:inventory`
- Scans backend DTO/enums and core module contracts.
- Writes report to `tools/contract-sync/reports/contract-drift.json`.

2. `pnpm -C src/Tnzi.UI contract:check`
- Compares current drift with approved baseline (`baseline.json`).
- Fails when new drift is introduced.

3. `pnpm -C src/Tnzi.UI contract:approve`
- Approves current drift as the new baseline.
- Only run after code review confirms expected changes.

## Config

`config.json` controls:

1. Module mapping (`backend` folders to `frontend` files)
2. Alias mapping (`backendSymbol -> frontendSymbol`)
3. Intentional exclusions (`ignoreBackend`, `ignoreFrontend`)

Use this file when adding modules or when backend naming differs from core naming.
