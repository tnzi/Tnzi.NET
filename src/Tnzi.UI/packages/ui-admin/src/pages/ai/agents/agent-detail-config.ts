/**
 * Phase 5 Task 5.3 — agents/AgentDetail sibling config (pure data).
 *
 * AgentDetail is a master-detail page (NOT a TCrudPage list) — it reuses the
 * Phase 5.2 form schema for its Overview tab editor. Tab definitions live here
 * so the .vue file stays focused on layout and wiring.
 *
 * NOTE — bridge methods consumed by AgentDetail.vue:
 *   bridge.agents.fetch       → load single agent (filters:{id}); ai-bridge has
 *                                NO agents.getById, so we degrade to a filtered
 *                                fetch and pick the first matching item
 *   bridge.agents.update      → save Overview edits
 *   bridge.agentRuns.fetch    → recent runs tab (filters:{agentId})
 *   bridge.skills.fetch       → skills tab (no agentId filter on backend yet,
 *                                shown as a generic skill list placeholder)
 *
 * Versions tab is a placeholder — `bridge.agents.versions` does not exist and
 * MUST NOT be invented per Lesson 12 (do not fabricate bridge methods).
 */
import type { FormSchemaItem } from '../../_shared/form-schema'
import { agentFormSchema } from './agent-list-config'

export interface AgentDetailTab {
  name: string
  label: string
}

export const agentDetailTabs: AgentDetailTab[] = [
  { name: 'overview', label: 'Overview' },
  { name: 'versions', label: 'Versions' },
  { name: 'runs', label: 'Recent Runs' },
  { name: 'skills', label: 'Skills' },
]

/**
 * Overview-tab form schema — derived from the Phase 5.2 agent form. The
 * Overview editor is a subset (no temperature/maxTokens/timeout — those belong
 * in the dedicated execution-config editor that ships in a later phase).
 */
export const agentOverviewSchema: FormSchemaItem[] = agentFormSchema.filter((f) =>
  ['name', 'description', 'provider', 'model', 'instructions', 'isEnabled'].includes(f.key),
)

/**
 * i18n keys referenced by AgentDetail.vue. Owned by Task 5.16.
 *
 *   modules.ai.agent.detail.title
 *   modules.ai.agent.detail.backToList
 *   modules.ai.agent.detail.tabs.overview
 *   modules.ai.agent.detail.tabs.versions
 *   modules.ai.agent.detail.tabs.runs
 *   modules.ai.agent.detail.tabs.skills
 *   modules.ai.agent.detail.save
 *   modules.ai.agent.detail.saved
 *   modules.ai.agent.detail.loadError
 *   modules.ai.agent.detail.versionsPlaceholder
 *   modules.ai.agent.detail.skillsPlaceholder
 */
