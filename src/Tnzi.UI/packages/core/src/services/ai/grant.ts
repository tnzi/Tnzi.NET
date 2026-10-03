/**
 * Agent resource grant API - the governance surface over the grants that give an
 * agent its tool groups / single tools / skills / knowledge bases.
 *
 * Mirrors `Tnzi.AI/Controllers/Admin/DefaultAgentGrantAdminController` (route
 * `admin/agents/grants`). Class-level gate `ai.agent.view`; the write endpoints
 * (enabled / priority / delete) additionally require `ai.agent.update`.
 *
 * How it relates to `AgentDto`: the agent's `toolGroups` / `skillSlugs` /
 * `knowledgeBaseIds` lists carry ENABLED grants only, and an agent update
 * reconciles those lists (add + remove enabled entries). A disabled grant is
 * invisible to those lists and survives such an update unchanged; it is listed,
 * re-enabled and deleted only through this API.
 *
 * Priority only orders the list projections (higher first). The runtime does not
 * rank grants by it: skills are filtered as a set, knowledge base hits are merged
 * by score, tool groups are a set.
 */

import type { HttpClient } from '../../http/http';

/** Route segment of a grant's resource category (bound to backend `GrantResourceType`). */
export type AgentGrantResourceType = 'tool' | 'skill' | 'knowledge';

/** One grant, disabled ones included. */
export interface AgentGrantDto {
  /** Grant id, addressed by the enable / priority / delete endpoints. */
  id: string;
  /** Tool group name, tool name, skill slug, or knowledge base id. */
  key: string;
  /** Disabled grants are invisible to the runtime. */
  isEnabled: boolean;
  /** Orders the list projections only (higher first). */
  priority: number;
}

/** Every grant of one agent, grouped by resource category. */
export interface AgentGrantListDto {
  /** Tool group grants (the unit the agent editor assigns). */
  toolGroups: AgentGrantDto[];
  /** Single-tool grants (written by version rollback or the API, not by the agent editor). */
  toolNames: AgentGrantDto[];
  skills: AgentGrantDto[];
  /** `key` is the knowledge base id. */
  knowledgeBases: AgentGrantDto[];
}

/** One reverse-lookup row: a non-deleted agent holding an ENABLED grant for the resource. */
export interface AgentGrantUsageDto {
  agentId: string;
  agentName: string;
  /** Whether the agent itself is enabled. */
  agentIsEnabled: boolean;
}

/** Admin agent grant API (`admin/agents/grants`). */
export function useAdminAgentGrantApi(client: HttpClient) {
  const base = '/admin/agents/grants';
  return {
    /** Every grant of an agent, disabled ones included. */
    listForAgent: (agentId: string) =>
      client.get<AgentGrantListDto>(`${base}/agent/${agentId}`),

    /** Agents with an enabled grant for a tool group or tool name (exact key match). */
    agentsUsingTool: (key: string) =>
      client.get<AgentGrantUsageDto[]>(`${base}/reverse/tool`, { params: { key } }),

    /** Agents with an enabled grant for a skill slug. */
    agentsUsingSkill: (slug: string) =>
      client.get<AgentGrantUsageDto[]>(`${base}/reverse/skill`, { params: { slug } }),

    /** Agents with an enabled grant for a knowledge base. */
    agentsUsingKnowledge: (knowledgeBaseId: string) =>
      client.get<AgentGrantUsageDto[]>(`${base}/reverse/knowledge`, { params: { knowledgeBaseId } }),

    /** Enable or disable one grant (`ai.agent.update`). 404 when the id is not a grant of that category. */
    setEnabled: (type: AgentGrantResourceType, grantId: string, enabled: boolean) =>
      client.post<void>(`${base}/${type}/${grantId}/enabled`, { enabled }),

    /** Set one grant's priority (`ai.agent.update`). Orders list projections only. */
    setPriority: (type: AgentGrantResourceType, grantId: string, priority: number) =>
      client.post<void>(`${base}/${type}/${grantId}/priority`, { priority }),

    /** Delete one grant (`ai.agent.update`). The only way to remove a disabled grant. */
    remove: (type: AgentGrantResourceType, grantId: string) =>
      client.delete<void>(`${base}/${type}/${grantId}`),
  };
}
