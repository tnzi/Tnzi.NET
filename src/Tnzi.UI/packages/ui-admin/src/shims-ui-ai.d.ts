/**
 * Type shim for @tnzi/ui-ai.
 *
 * @tnzi/ui-ai uses internal `@/...` path aliases that don't resolve from
 * outside the package, and its dist/ is not built during ui-admin typecheck.
 * Pages that lazy-load ui-ai components (e.g. WorkflowCanvas via
 * defineAsyncComponent) only need the symbol to exist at type level — the
 * real module is resolved at runtime by the bundler / by the test stub at
 * __tests__/stubs/ui-ai.ts.
 */
declare module '@tnzi/ui-ai' {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export const WorkflowCanvas: any
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const _default: any
  export default _default
}
