/**
 * Phase 5 — AI admin pages barrel.
 *
 * Subsequent Phase 5 page tasks (5.3–5.14) append their exports here.
 * The router (Task 5.15) imports from this file rather than reaching into
 * each sub-directory directly.
 */
export { default as AgentList } from './agents/AgentList.vue' // Task 5.2
export { default as AgentDetail } from './agents/AgentDetail.vue' // Task 5.3
export { default as RunMonitor } from './agents/RunMonitor.vue' // Task 5.4
export { default as WorkflowEditor } from './workflows/WorkflowEditor.vue' // Task 5.5
export { default as WorkflowRunViewer } from './workflows/RunViewer.vue' // Task 5.6
export { default as SkillList } from './skills/SkillList.vue' // Task 5.7
export { default as ProviderConfig } from './providers/ProviderConfig.vue' // Task 5.8
export { default as UsageDashboard } from './usage/UsageDashboard.vue' // Task 5.9
export { default as KbManager } from './knowledge/KbManager.vue' // Task 5.10
export { default as McpServerList } from './mcp/McpServerList.vue' // Task 5.11
export { default as QuotaRules } from './quota/QuotaRules.vue' // Task 5.12
export { default as PersonaList } from './personas/PersonaList.vue' // Task 5.13
export { default as EvalViewer } from './evaluations/EvalViewer.vue' // Task 5.14
