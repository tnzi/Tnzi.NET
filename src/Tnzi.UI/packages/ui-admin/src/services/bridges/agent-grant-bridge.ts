/**
 * Agent resource grant bridge - `admin/agents/grants`.
 *
 * A standalone bridge rather than another sub-contract on `ai-bridge`: grants are
 * not a CRUD resource of their own (they are created by the agent editor's list
 * reconcile, addressed per grant only to toggle or delete them), and the reverse
 * lookups are keyed by the RESOURCE (skill slug / knowledge base id / tool key),
 * not by a grant.
 *
 * Every call throws on a refused or failed response, reads included. The pages
 * render "used by N agents" from `usedBy`; a refusal resolved to `[]` would read
 * as "no agent depends on this" right before the operator deletes it.
 */
import type { HttpClient } from '@tnzi/core/http'
import {
  useAdminAgentGrantApi,
  type AgentGrantResourceType,
  type AgentGrantListDto,
  type AgentGrantUsageDto,
} from '@tnzi/core/services/ai'

import { ensureOk, unwrapOk } from '../_mappers'

export type { AgentGrantResourceType, AgentGrantListDto, AgentGrantUsageDto }
export type { AgentGrantDto } from '@tnzi/core/services/ai'

export interface AgentGrantBridge {
  /** Every grant of an agent, disabled ones included. */
  listForAgent(agentId: string): Promise<AgentGrantListDto>
  /**
   * Non-deleted agents holding an ENABLED grant for the resource: a skill slug, a
   * knowledge base id, or a tool key (tool group name or tool name, exact match).
   */
  usedBy(type: AgentGrantResourceType, key: string): Promise<AgentGrantUsageDto[]>
  setEnabled(type: AgentGrantResourceType, grantId: string, enabled: boolean): Promise<void>
  remove(type: AgentGrantResourceType, grantId: string): Promise<void>
}

export interface AgentGrantBridgeDeps {
  client?: HttpClient
}

const NO_CLIENT = 'Agent grant API is not available (no HTTP client)'

export function createAgentGrantBridge(deps: AgentGrantBridgeDeps): AgentGrantBridge {
  const api = deps.client ? useAdminAgentGrantApi(deps.client) : null

  function requireApi(): ReturnType<typeof useAdminAgentGrantApi> {
    if (!api) throw new Error(NO_CLIENT)
    return api
  }

  return {
    async listForAgent(agentId) {
      const list = unwrapOk<AgentGrantListDto>(await requireApi().listForAgent(agentId))
      return {
        toolGroups: list?.toolGroups ?? [],
        toolNames: list?.toolNames ?? [],
        skills: list?.skills ?? [],
        knowledgeBases: list?.knowledgeBases ?? [],
      }
    },

    async usedBy(type, key) {
      const grants = requireApi()
      const response =
        type === 'skill'
          ? await grants.agentsUsingSkill(key)
          : type === 'knowledge'
            ? await grants.agentsUsingKnowledge(key)
            : await grants.agentsUsingTool(key)
      return unwrapOk<AgentGrantUsageDto[]>(response) ?? []
    },

    async setEnabled(type, grantId, enabled) {
      ensureOk(await requireApi().setEnabled(type, grantId, enabled))
    },

    async remove(type, grantId) {
      ensureOk(await requireApi().remove(type, grantId))
    },
  }
}
