export { default as TWorkflowCanvas } from './TWorkflowCanvas.vue';
export { default as TWorkflowNode } from './TWorkflowNode.vue';
export { default as TWorkflowEdge } from './TWorkflowEdge.vue';
export { default as TWorkflowConnection } from './TWorkflowConnection.vue';
export { default as TWorkflowControls } from './TWorkflowControls.vue';
export { default as TWorkflowPanel } from './TWorkflowPanel.vue';
export { default as TWorkflowToolbar } from './TWorkflowToolbar.vue';
export { default as TWorkflowMinimap } from './TWorkflowMinimap.vue';

// `Handle` / `Position` and the @vue-flow types are re-exported from
// `src/workflow/index.ts` (the `@tnzi/ui-ai/workflow` subpath), NOT from here.
// This barrel is reachable from the root barrel via `components/index.ts`, and
// a `export … from '@vue-flow/core'` line here survives bundling as a real
// import, which would put `@vue-flow/core` back into the module graph of every
// consumer that touches `@tnzi/ui-ai` or `@tnzi/ui-ai/components`.
