/**
 * Permission bridge — wraps `/admin/permissions/*` exposed by
 * `Tnzi.AI.Controllers.Admin.DefaultPermissionAdminController`.
 *
 * 5 endpoints:
 *   • GET    /admin/permissions/rules                      (session + global rules snapshot)
 *   • POST   /admin/permissions/rules/evaluate             (debug / dry-run)
 *   • GET    /admin/permissions/persisted-rules            (DB-persisted rules)
 *   • POST   /admin/permissions/persisted-rules            (create)
 *   • DELETE /admin/permissions/persisted-rules/{id}       (delete)
 */
import type { HttpClient } from '@tnzi/core/http'

export type PermissionBehavior = 0 | 1 | 2 // Allow / Ask / Deny
export type ToolPermissionScope = 0 | 1 | 2 | 3 // System / Project / User / Session

export interface PermissionRuleItemDto {
  toolPattern: string
  toolGroup?: string | null
  commandPrefix?: string | null
  serverName?: string | null
  pathPrefix?: string | null
  isSubAgentOnly: boolean
  subAgentName?: string | null
  isWorkflowOnly: boolean
  workflowNodeName?: string | null
  behavior: PermissionBehavior
  scope: ToolPermissionScope
  priority: number
  isDestructiveOnly: boolean
  reason?: string | null
}

export interface PermissionRulesDto {
  hasRules: boolean
  sessionRules: PermissionRuleItemDto[]
}

export interface PersistedPermissionRuleDto {
  id: string
  toolPattern?: string | null
  toolGroup?: string | null
  commandPrefix?: string | null
  serverName?: string | null
  pathPrefix?: string | null
  behavior: PermissionBehavior
  scope: ToolPermissionScope
  priority: number
  isDestructiveOnly: boolean
  isSubAgentOnly: boolean
  reason?: string | null
  userId?: string | null
  isEnabled: boolean
  creationTime: string
  lastModificationTime?: string | null
}

export interface CreatePersistedPermissionRuleDto {
  toolPattern?: string | null
  toolGroup?: string | null
  commandPrefix?: string | null
  serverName?: string | null
  pathPrefix?: string | null
  behavior: PermissionBehavior
  scope: ToolPermissionScope
  priority: number
  isDestructiveOnly: boolean
  isSubAgentOnly: boolean
  reason?: string | null
  userId?: string | null
  isEnabled: boolean
}

export interface PermissionEvaluateRequestDto {
  toolName: string
  toolGroup?: string | null
  serverName?: string | null
  isSubAgent?: boolean
  subAgentName?: string | null
  isDestructive?: boolean
  shellCommand?: string | null
  workingDirectory?: string | null
}

export interface PermissionEvaluateResultDto {
  toolName: string
  behavior: PermissionBehavior
  reason?: string | null
  scope?: ToolPermissionScope | null
  matchedRulePattern?: string | null
  matchedToolGroup?: string | null
  matchedServerName?: string | null
  matchedPathPrefix?: string | null
  matchedSubAgentName?: string | null
  matchedWorkflowNodeName?: string | null
}

export interface PermissionBridgeDeps {
  client?: HttpClient
}

export interface PermissionBridge {
  getRules(): Promise<PermissionRulesDto | null>
  evaluate(req: PermissionEvaluateRequestDto): Promise<PermissionEvaluateResultDto | null>
  getPersistedRules(): Promise<PersistedPermissionRuleDto[]>
  createPersistedRule(input: CreatePersistedPermissionRuleDto): Promise<PersistedPermissionRuleDto | null>
  deletePersistedRule(id: string): Promise<void>
}

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
}

export function createPermissionBridge(deps: PermissionBridgeDeps = {}): PermissionBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createPermissionBridge: no HttpClient provided'))
    return {
      getRules: noOp as never,
      evaluate: noOp as never,
      getPersistedRules: noOp as never,
      createPersistedRule: noOp as never,
      deletePersistedRule: noOp as never,
    }
  }

  return {
    getRules: async () =>
      unwrap<PermissionRulesDto | null>(
        await client.get<PermissionRulesDto>('/admin/permissions/rules'),
      ),
    evaluate: async (req) =>
      unwrap<PermissionEvaluateResultDto | null>(
        await client.post<PermissionEvaluateResultDto>('/admin/permissions/rules/evaluate', req),
      ),
    getPersistedRules: async () =>
      unwrap<PersistedPermissionRuleDto[]>(
        await client.get<PersistedPermissionRuleDto[]>('/admin/permissions/persisted-rules'),
      ) ?? [],
    createPersistedRule: async (input) =>
      unwrap<PersistedPermissionRuleDto | null>(
        await client.post<PersistedPermissionRuleDto>('/admin/permissions/persisted-rules', input),
      ),
    deletePersistedRule: async (id: string) => {
      await client.delete(`/admin/permissions/persisted-rules/${encodeURIComponent(id)}`)
    },
  }
}
