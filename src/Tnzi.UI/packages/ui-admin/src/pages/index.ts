// Phase 2a: legacy page re-exports removed alongside CrudPage/FormModal deletion.
// Pages are rewritten in Phase 3 (identity/authorization/system/...) and
// the real barrel returns there. Kept as an empty module so `src/index.ts`
// `export * from './pages'` continues to resolve during Phase 2a.
export {}
