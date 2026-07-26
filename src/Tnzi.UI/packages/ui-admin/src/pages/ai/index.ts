/**
 * Phase 5 - AI admin pages barrel.
 *
 * Subsequent Phase 5 page tasks (5.3-5.14) append their exports here.
 * The router (Task 5.15) imports from this file rather than reaching into
 * each sub-directory directly.
 */
export { default as Agents } from './agents/Agents.vue' // Task 5.2
export { default as AgentDetail } from './agents/AgentDetail.vue' // Task 5.3
export { default as AgentRunMonitor } from './agents/AgentRunMonitor.vue' // Task 5.4
export { default as WorkflowEditor } from './workflows/WorkflowEditor.vue' // Task 5.5
export { default as WorkflowRunViewer } from './workflows/WorkflowRunViewer.vue' // Task 5.6
export { default as Skills } from './skills/Skills.vue' // Task 5.7
export { default as Providers } from './providers/Providers.vue' // Task 5.8
export { default as UsageDashboard } from './usage/UsageDashboard.vue' // Task 5.9
export { default as Knowledge } from './knowledge/Knowledge.vue' // Task 5.10
export { default as McpServers } from './mcp/McpServers.vue' // Task 5.11
export { default as Quotas } from './quota/Quotas.vue' // Task 5.12
export { default as EvaluationViewer } from './evaluations/EvaluationViewer.vue' // Task 5.14
