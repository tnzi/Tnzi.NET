import type { MockScenario } from '../types'

const scenario: MockScenario = {
  meta: {
    id: '05-workflow-trip',
    title: 'Workflow: Trip Planner',
    description: 'DAG workflow executes in the side panel with live node status',
    category: 'agent',
    icon: 'lucide:git-branch',
    componentsShowcased: ['WorkflowCanvas', 'NodeEditor', 'AgentQueue', 'RunMonitor'],
  },
  initial: {
    workflowNodes: [
      { id: 'n1', label: 'Parse Request', x: 50, y: 50 },
      { id: 'n2', label: 'Fetch Destinations', x: 250, y: 50 },
      { id: 'n3', label: 'Check Weather', x: 450, y: 50 },
      { id: 'n4', label: 'Draft Itinerary', x: 250, y: 200 },
      { id: 'n5', label: 'Estimate Budget', x: 450, y: 200 },
      { id: 'n6', label: 'Format Response', x: 250, y: 350 },
    ],
    workflowEdges: [
      { id: 'e1', source: 'n1', target: 'n2' },
      { id: 'e2', source: 'n2', target: 'n3' },
      { id: 'e3', source: 'n2', target: 'n4' },
      { id: 'e4', source: 'n3', target: 'n4' },
      { id: 'e5', source: 'n4', target: 'n5' },
      { id: 'e6', source: 'n5', target: 'n6' },
    ],
  },
  events: [
    { at: 0, type: 'user-message', content: 'Plan me a weekend trip to Kyoto in April.' },
    { at: 500, type: 'assistant-start', agentName: 'Trip Planner' },
    { at: 700, type: 'assistant-delta', text: 'Starting workflow…\n' },
    { at: 900, type: 'workflow-node-status', nodeId: 'n1', status: 'running' },
    { at: 1400, type: 'workflow-node-status', nodeId: 'n1', status: 'done' },
    { at: 1500, type: 'workflow-node-status', nodeId: 'n2', status: 'running' },
    { at: 2200, type: 'workflow-node-status', nodeId: 'n2', status: 'done' },
    { at: 2300, type: 'workflow-node-status', nodeId: 'n3', status: 'running' },
    { at: 2500, type: 'workflow-node-status', nodeId: 'n4', status: 'running' },
    { at: 3200, type: 'workflow-node-status', nodeId: 'n3', status: 'done' },
    { at: 3600, type: 'workflow-node-status', nodeId: 'n4', status: 'done' },
    { at: 3700, type: 'workflow-node-status', nodeId: 'n5', status: 'running' },
    { at: 4300, type: 'workflow-node-status', nodeId: 'n5', status: 'done' },
    { at: 4400, type: 'workflow-node-status', nodeId: 'n6', status: 'running' },
    { at: 4800, type: 'assistant-delta', text: '\n**Kyoto Weekend Itinerary**\n\n' },
    { at: 5000, type: 'assistant-delta', text: '**Day 1 (Sat):** Arashiyama bamboo grove → Tenryū-ji → Togetsukyō Bridge. ' },
    { at: 5300, type: 'assistant-delta', text: 'Dinner in Gion district.\n\n' },
    { at: 5500, type: 'assistant-delta', text: '**Day 2 (Sun):** Fushimi Inari-taisha (early morning) → Kiyomizu-dera → Nishiki Market.\n\n' },
    { at: 5800, type: 'assistant-delta', text: '**Weather:** Cherry blossoms likely still in bloom, 15-20°C. Bring a light jacket.\n\n' },
    { at: 6100, type: 'assistant-delta', text: '**Estimated budget:** ¥45,000-60,000 per person (lodging + meals + transit).' },
    { at: 6200, type: 'workflow-node-status', nodeId: 'n6', status: 'done' },
    { at: 6400, type: 'assistant-end', usage: { promptTokens: 18, completionTokens: 340, totalTokens: 358 } },
  ],
}

export default scenario
