# Type-level fixtures

Files here are **compiled, never executed**. They are listed in
`tsconfig.json`'s `include` (the rest of `__tests__/` is not), so `pnpm typecheck`
compiles them; `tsconfig.build.json` drops `**/__tests__/**`, so they never reach
`dist`. They must NOT be named `*.test.ts` - that pattern is excluded from the
typecheck program, and vitest would then try to run a file with no tests in it.

## Why they exist

A **widened** type signature cannot regress in a way a runtime test can see. If
`RowAction.onClick` silently went back to `void | Promise<void>`, every existing
test would stay green and the only symptom would appear in a consuming app that
this repo does not compile. The fixture is the guard.

## Mutation verification

Each fixture must be shown to fail when the widening is reverted. As of
2026-08-09, `strict-dto-consumer.vue` was verified against all three:

| Revert | Expected failure |
|---|---|
| `TCrudPage.allColumns: ColumnDefs<NoInfer<T>>` → `ColumnDef[]` | `Type 'ColumnDef<MatterSummaryDto>[]' is not assignable to type 'ColumnDef<Record<string, unknown>>[]'` |
| `RowAction.onClick: (row: T) => unknown` → `void \| Promise<void>` | 3 errors on the `router.push` / assignment-shorthand handlers |
| `TabSection.label` → `string` | 2 errors on the render-function and VNode labels |

Re-run that table if you touch any of the three. A fixture that would still
compile after the revert is testing nothing.
