/**
 * useWorkflowVisualization — Converts workflow definitions to @vue-flow nodes/edges
 */

import { ref, computed, readonly, type Ref, type DeepReadonly } from 'vue';
import type { Node, Edge } from '@vue-flow/core';

export interface WorkflowNodeDef {
  id: string;
  type: string;
  label: string;
  description?: string;
  position?: { x: number; y: number };
  status?: 'pending' | 'active' | 'completed' | 'failed';
}

export interface WorkflowEdgeDef {
  id: string;
  source: string;
  target: string;
  label?: string;
  animated?: boolean;
}

export interface WorkflowDefinition {
  nodes: WorkflowNodeDef[];
  edges: WorkflowEdgeDef[];
}

export interface UseWorkflowVisualizationReturn {
  nodes: DeepReadonly<Ref<Node[]>>;
  edges: DeepReadonly<Ref<Edge[]>>;
  setWorkflow: (def: WorkflowDefinition) => void;
  updateNodeStatus: (nodeId: string, status: WorkflowNodeDef['status']) => void;
  reset: () => void;
}

export function useWorkflowVisualization(): UseWorkflowVisualizationReturn {
  const nodeDefs = ref<WorkflowNodeDef[]>([]);
  const edgeDefs = ref<WorkflowEdgeDef[]>([]);

  const nodes = computed<Node[]>(() =>
    nodeDefs.value.map((n, index) => ({
      id: n.id,
      type: 'custom',
      label: n.label,
      position: n.position ?? { x: index * 250, y: 100 },
      data: {
        label: n.label,
        description: n.description,
        status: n.status ?? 'pending',
        nodeType: n.type,
      },
    })),
  );

  const edges = computed<Edge[]>(() =>
    edgeDefs.value.map((e) => ({
      id: e.id,
      source: e.source,
      target: e.target,
      label: e.label,
      type: e.animated ? 'animated' : 'default',
      data: { variant: e.animated ? 'animated' : 'default' },
    })),
  );

  function setWorkflow(def: WorkflowDefinition): void {
    nodeDefs.value = [...def.nodes];
    edgeDefs.value = [...def.edges];
  }

  function updateNodeStatus(nodeId: string, status: WorkflowNodeDef['status']): void {
    nodeDefs.value = nodeDefs.value.map((n) =>
      n.id === nodeId ? { ...n, status } : n,
    );
  }

  function reset(): void {
    nodeDefs.value = [];
    edgeDefs.value = [];
  }

  return {
    nodes: readonly(nodes) as DeepReadonly<Ref<Node[]>>,
    edges: readonly(edges) as DeepReadonly<Ref<Edge[]>>,
    setWorkflow,
    updateNodeStatus,
    reset,
  };
}
