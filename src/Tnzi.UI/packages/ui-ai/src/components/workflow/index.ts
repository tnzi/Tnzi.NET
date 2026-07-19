export { default as TWorkflowCanvas } from './TWorkflowCanvas.vue';
export { default as TWorkflowNode } from './TWorkflowNode.vue';
export { default as TWorkflowEdge } from './TWorkflowEdge.vue';
export { default as TWorkflowConnection } from './TWorkflowConnection.vue';
export { default as TWorkflowControls } from './TWorkflowControls.vue';
export { default as TWorkflowPanel } from './TWorkflowPanel.vue';
export { default as TWorkflowToolbar } from './TWorkflowToolbar.vue';
export { default as TWorkflowMinimap } from './TWorkflowMinimap.vue';

// Re-export @vue-flow primitives so consumers writing custom node templates
// against TWorkflowCanvas's `#node-<type>` slots can register handles without
// needing a direct `@vue-flow/core` dependency.
export { Handle, Position } from '@vue-flow/core';
export type { NodeProps, EdgeProps, Connection, NodeChange, EdgeChange } from '@vue-flow/core';
