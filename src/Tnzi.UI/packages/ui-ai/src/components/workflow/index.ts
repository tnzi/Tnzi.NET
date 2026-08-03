export { default as TWorkflowCanvas } from './TWorkflowCanvas.vue';
export { default as TWorkflowNode } from './TWorkflowNode.vue';
export { default as TWorkflowEdge } from './TWorkflowEdge.vue';
export { default as TWorkflowConnection } from './TWorkflowConnection.vue';
export { default as TWorkflowControls } from './TWorkflowControls.vue';
export { default as TWorkflowPanel } from './TWorkflowPanel.vue';
export { default as TWorkflowToolbar } from './TWorkflowToolbar.vue';
export { default as TWorkflowMinimap } from './TWorkflowMinimap.vue';

// This barrel is NOT reachable from `components/index.ts`, and must stay that
// way. Every SFC above imports `@vue-flow/core` at module scope, so anything
// that names this file pulls the package's heaviest dependency into its module
// graph. `src/workflow/index.ts` (the `@tnzi/ui-ai/workflow` subpath) is the
// one place allowed to reach it, and also the only place `Handle` / `Position`
// and the @vue-flow types are re-exported from.
//
// Note the trap is wider than it looks: a barrel does not have to *name*
// `@vue-flow/core` to leak it. Re-exporting `TWorkflowCanvas` is enough,
// because the leak travels through the component's own imports. Grepping a
// built entry for `vue-flow` therefore proves nothing on its own - it reports
// only direct references, not what the entry pulls in transitively. See
// `__tests__/conventions/vue-flow-isolation.test.ts`, which walks the import
// graph instead.
