# Phase 3 Page Conventions

Every preset management page in `@tnzi/ui-admin/pages/{module}/*.vue` follows this shape:

1. **`{page}-config.ts`** sibling file exports `columns` (ColumnDef[]) and `formSchema` (FormSchemaItem[]).
2. **`.vue` file** is ~25 lines: imports `TCrudPage`, calls `useCrudPage` with bridge callbacks, renders `TCrudPage` with a single `#form` slot that delegates to `TFormSchemaRenderer`.
3. **Permission gating**: if the page has restricted actions, guard via `const { can } = usePermissionGuard()` and conditionally render via `v-if="can('module.action')"`.
4. **i18n**: use the page-scoped `translate` function that maps to `en`/`zh-cn` locales via the `tnzi.admin.modules.{module}.{page}.*` namespace. Task 3.39 adds all keys.
5. **Test**: one integration test per page mounting the component with mocked `@tnzi/core/services/{module}` at module boundary; asserts mount + fetch triggered + create button opens modal. Deeper assertions are Phase 6 E2E territory.
6. **Router**: do NOT add the route inside the page task. Task 3.38 collects and registers all 28 routes at once.
7. **Bridge**: the module's bridge must already be filled in (earlier task in the same module group) before any page using it can be written.

Rationale: this convention exists so Phase 3 tasks 3.2–3.37 can be ~120 lines each. Deviations require updating this file and flagging the reason.
