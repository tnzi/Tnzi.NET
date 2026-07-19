/**
 * Permission bridge — wraps `/admin/permissions/*` exposed by
 * `Tnzi.AI.Controllers.Admin.DefaultPermissionAdminController`.
 *
 * 5 endpoints:
 *   • GET    /admin/permissions/rules                      (session + global rules snapshot)
 *   • POST   /admin/permissions/rules/evaluate             (debug / dry-run)
 *   • GET    /admin/permissions/persisted-rules            (DB-persisted rules)
 *   • POST   /admin/permissions/persisted-rules            (create)
 *   • PUT    /admin/permissions/persisted-rules/{id}       (update)
 *   • DELETE /admin/permissions/persisted-rules/{id}       (delete)
 *
 * DTOs + enums + the raw API factory live in `@tnzi/core/services/ai`
 * (`permission.ts`). This bridge delegates to `usePermissionAdminApi(client)`
 * and adds the tolerant `unwrap` + empty-on-404 shaping the page expects.
 *
 * Enum compatibility: core models `PermissionBehavior` / `ToolPermissionScope`
 * as numeric enums (Allow=0 / Ask=1 / Deny=2; System=0 / Project=1 / User=2 /
 * Session=3). The previous bridge used `0 | 1 | 2`(`| 3`) numeric unions.
 * Numeric enum members are numeric literals, so the consuming page's
 * `switch (n) { case 0: … }` checks and `value: 0` NSelect options stay
 * compatible — `ToolPermissions.vue` is unchanged. Both the enums (value) and
 * the types are re-exported so existing imports keep resolving.
 */
import type { HttpClient } from '@tnzi/core/http'
import { usePermissionAdminApi, PermissionBehavior, ToolPermissionScope } from '@tnzi/core/services/ai'
import { ensureOk, unwrapResult as unwrap } from '../_mappers'

// Re-export the enums as runtime values (consumers may import them as values
// or, as ToolPermissions.vue does, as types).
export { PermissionBehavior, ToolPermissionScope }

export type {
  PermissionRuleItemDto,
  PermissionRulesDto,
  PersistedPermissionRuleDto,
  CreatePersistedPermissionRuleDto,
  PermissionEvaluateRequestDto,
  PermissionEvaluateResultDto,
} from '@tnzi/core/services/ai'

import type {
  PermissionRulesDto,
  PersistedPermissionRuleDto,
  CreatePersistedPermissionRuleDto,
  PermissionEvaluateRequestDto,
  PermissionEvaluateResultDto,
} from '@tnzi/core/services/ai'

export interface PermissionBridgeDeps {
  client?: HttpClient
}

export interface PermissionBridge {
  getRules(): Promise<PermissionRulesDto | null>
  evaluate(req: PermissionEvaluateRequestDto): Promise<PermissionEvaluateResultDto | null>
  getPersistedRules(): Promise<PersistedPermissionRuleDto[]>
  createPersistedRule(input: CreatePersistedPermissionRuleDto): Promise<PersistedPermissionRuleDto | null>
  updatePersistedRule(id: string, input: CreatePersistedPermissionRuleDto): Promise<PersistedPermissionRuleDto | null>
  deletePersistedRule(id: string): Promise<void>
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
      updatePersistedRule: noOp as never,
      deletePersistedRule: noOp as never,
    }
  }

  const api = usePermissionAdminApi(client)

  return {
    getRules: async () =>
      unwrap<PermissionRulesDto | null>(await api.getRules()),
    evaluate: async (req) =>
      unwrap<PermissionEvaluateResultDto | null>(await api.evaluate(req)),
    getPersistedRules: async () =>
      unwrap<PersistedPermissionRuleDto[]>(await api.getPersistedRules()) ?? [],
    createPersistedRule: async (input) =>
      unwrap<PersistedPermissionRuleDto | null>(await api.createPersistedRule(input)),
    updatePersistedRule: async (id, input) =>
      unwrap<PersistedPermissionRuleDto | null>(await api.updatePersistedRule(id, input)),
    deletePersistedRule: async (id: string) => {
      ensureOk(await api.deletePersistedRule(id))
    },
  }
}
