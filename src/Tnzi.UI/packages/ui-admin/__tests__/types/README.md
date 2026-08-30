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

The same goes for anything that only a **consumer's** compiler settings can
check. This repo does not set `vueCompilerOptions.strictTemplates`, so a prop
that gets deleted and passed as a fallthrough attribute instead keeps compiling
here and only breaks at a consumer that does set it. Asserting the props type
directly (an object literal assigned to `InstanceType<typeof X>['$props']`, where
excess property checking applies) is the equivalent check under our settings.

## Mutation verification

Each fixture must be shown to fail when the widening is reverted. As of
2026-08-09, `strict-dto-consumer.vue` was verified against all three:

| Revert | Expected failure |
|---|---|
| `TCrudPage.allColumns: ColumnDefs<NoInfer<T>>` → `ColumnDef[]` | `Type 'ColumnDef<MatterSummaryDto>[]' is not assignable to type 'ColumnDef<Record<string, unknown>>[]'` |
| `RowAction.onClick: (row: T) => unknown` → `void \| Promise<void>` | 3 errors on the `router.push` / assignment-shorthand handlers |
| `TabSection.label` → `string` | 2 errors on the render-function and VNode labels |

As of 2026-08-27, `strict-file-image.ts` was verified against:

| Revert | Expected failure |
|---|---|
| drop `objectFit` from `TFileImage`'s `defineProps` | 2 × `TS2353: Object literal may only specify known properties, and 'objectFit' does not exist in type ...` |

Re-run the matching table if you touch any of these. A fixture that would still
compile after the revert is testing nothing.
