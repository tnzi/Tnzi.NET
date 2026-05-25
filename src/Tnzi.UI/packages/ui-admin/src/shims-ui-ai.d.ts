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
  // VueFlow primitives re-exported by @tnzi/ui-ai for consumers writing
  // custom node templates (`#node-<type>` slot) against `WorkflowCanvas`.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export const Handle: any
  export const Position: {
    Top: 'top'
    Right: 'right'
    Bottom: 'bottom'
    Left: 'left'
  }
  /** VueFlow Node template props passed by the `#node-<type>` slot scope. */
  export interface NodeProps<TData = Record<string, unknown>> {
    id: string
    type?: string
    selected?: boolean
    data?: TData
    position?: { x: number; y: number }
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    [key: string]: any
  }
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const _default: any
  export default _default
}
