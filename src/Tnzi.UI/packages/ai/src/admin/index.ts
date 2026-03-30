/**
 * AI Admin
 *
 * Admin dashboard layout + 12 management pages.
 */

export { default as AdminLayout } from './AdminLayout.vue';
export { default as AgentManagement } from './pages/AgentManagement.vue';
export { default as AgentRunMonitor } from './pages/AgentRunMonitor.vue';
export { default as WorkflowEditor } from './pages/WorkflowEditor.vue';
export { default as WorkflowRunViewer } from './pages/WorkflowRunViewer.vue';
export { default as SkillManagement } from './pages/SkillManagement.vue';
export { default as ProviderConfig } from './pages/ProviderConfig.vue';
export { default as UsageAnalytics } from './pages/UsageAnalytics.vue';
export { default as KnowledgeBaseManager } from './pages/KnowledgeBaseManager.vue';
export { default as McpServerPanel } from './pages/McpServerPanel.vue';
export { default as QuotaManagement } from './pages/QuotaManagement.vue';
export { default as PersonaManagement } from './pages/PersonaManagement.vue';
export { default as EvaluationViewer } from './pages/EvaluationViewer.vue';

export type { AdminNavItem } from './AdminLayout.vue';
export type { AgentItem } from './pages/AgentManagement.vue';
export type { AgentRunItem } from './pages/AgentRunMonitor.vue';
// WorkflowEditor and WorkflowRunViewer use Node/Edge from @vue-flow/core directly
export type { SkillItem } from './pages/SkillManagement.vue';
export type { ProviderItem } from './pages/ProviderConfig.vue';
export type { UsageSummary, UsageEntry } from './pages/UsageAnalytics.vue';
export type { KnowledgeBaseItem } from './pages/KnowledgeBaseManager.vue';
export type { McpServerItem, McpToolItem } from './pages/McpServerPanel.vue';
export type { QuotaItem } from './pages/QuotaManagement.vue';
export type { PersonaItem } from './pages/PersonaManagement.vue';
export type { EvaluationItem } from './pages/EvaluationViewer.vue';
