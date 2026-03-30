<script setup lang="ts">
import { TWorkflowCanvas, TWorkflowNode, TWorkflowEdge, TWorkflowControls, TWorkflowPanel, TWorkflowToolbar, TWorkflowMinimap } from '@tnzi/ai/components';
import { useWorkflowVisualization } from '@tnzi/ai/composables';
import { mockWorkflowNodes, mockWorkflowEdges } from '../mock/data';

const { nodes, edges, setWorkflow, updateNodeStatus } = useWorkflowVisualization();
setWorkflow({ nodes: mockWorkflowNodes, edges: mockWorkflowEdges });

function simulateProgress() {
  updateNodeStatus('n3', 'completed');
  setTimeout(() => updateNodeStatus('n4', 'active'), 1000);
  setTimeout(() => {
    updateNodeStatus('n4', 'completed');
    updateNodeStatus('n5', 'active');
  }, 2500);
  setTimeout(() => updateNodeStatus('n5', 'completed'), 4000);
}
</script>

<template>
  <div class="h-full relative">
    <TWorkflowCanvas :nodes="nodes" :edges="edges">
      <template #default>
        <TWorkflowPanel position="top-left">
          <TWorkflowToolbar @run="simulateProgress" />
        </TWorkflowPanel>
        <TWorkflowPanel position="top-right">
          <TWorkflowControls />
        </TWorkflowPanel>
        <TWorkflowMinimap />
      </template>
    </TWorkflowCanvas>
  </div>
</template>
