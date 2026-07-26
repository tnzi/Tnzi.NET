/**
 * `@tnzi/ui-ai/workflow` - everything that depends on `@vue-flow/core`.
 *
 * `@vue-flow/core` (plus background + minimap) is by far the heaviest
 * dependency in this package, and only the workflow editor needs it. Keeping
 * the canvas, its node/edge primitives and the re-exported `Handle`/`Position`
 * behind this dedicated subpath means a consumer that only builds a chat
 * product never pulls vue-flow into its module graph.
 *
 * @example
 * ```ts
 * import { TWorkflowCanvas, Handle, Position } from '@tnzi/ui-ai/workflow'
 * import type { NodeProps } from '@tnzi/ui-ai/workflow'
 * ```
 */

export {
  TWorkflowCanvas,
  TWorkflowNode,
  TWorkflowEdge,
  TWorkflowConnection,
  TWorkflowControls,
  TWorkflowPanel,
  TWorkflowToolbar,
  TWorkflowMinimap,
} from '../components/workflow/index';

/* Straight from the package, not via `components/workflow/index`: that barrel
   is reachable from the root barrel, so re-exporting through it would drag
   `@vue-flow/core` back into every consumer's module graph. This file is the
   single place allowed to name it. */
export { Handle, Position } from '@vue-flow/core';
export type {
  NodeProps,
  EdgeProps,
  Connection,
  NodeChange,
  EdgeChange,
} from '@vue-flow/core';

export { useWorkflowVisualization } from '../composables/useWorkflowVisualization';
export type {
  WorkflowNodeDef,
  WorkflowEdgeDef,
  WorkflowDefinition,
  UseWorkflowVisualizationReturn,
} from '../composables/useWorkflowVisualization';
