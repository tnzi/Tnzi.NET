/**
 * Dual-control bridge - the approver's queue for `Tnzi.Authorization`'s
 * four-eyes requests.
 *
 * Three of the five CRUD verbs are deliberately absent, and their absence is
 * the design rather than an omission:
 *
 *   create - a request is always raised inside a business action, because it
 *            has to carry that action's parameter snapshot. The business
 *            module's own endpoint calls `IDualControlService.RequestAsync`;
 *            there is no generic "raise a dual-control request" endpoint to
 *            call and there should not be one.
 *   update - the whole point is that the approved payload is frozen. Editing a
 *            pending request would let "transfer 100" become "transfer 1M"
 *            between the ask and the nod.
 *   delete - a decided request is the evidence that the second person looked.
 *            Cancel records that the requester withdrew it; deleting would
 *            erase that it was ever asked.
 *
 * What is left - read, approve, reject, cancel - is the generic half, which is
 * exactly why it lives in the framework instead of being rewritten by every
 * consuming application.
 *
 * ★ Approving needs TWO permissions: `authorization.dualControl.approve` to
 *   reach the endpoint at all, and `{operation}.approve` - checked server-side
 *   per request - to decide that particular kind of action. The second is what
 *   stops an approver of expense reports from also approving account deletions,
 *   so the UI must not assume the button being visible means the call will pass.
 */
import {
  useAdminDualControlApi,
  type DualControlDecisionDto,
  type DualControlQueryDto,
  type DualControlRequestDto,
} from '@tnzi/core/services/authorization'
import type { CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, mapQueryToListRequest, pagedResult, unwrapResult as unwrap, unwrapOk } from '../_mappers'

type HttpClient = Parameters<typeof useAdminDualControlApi>[0]

export interface DualControlBridgeDeps {
  /** Production path: provide HttpClient; the bridge builds the API internally. */
  client?: HttpClient
  /** Test path: inject a mock API directly. */
  adminDualControlApi?: ReturnType<typeof useAdminDualControlApi>
}

export interface DualControlContract {
  /** Page through requests. */
  fetch(query: CrudPageQuery): Promise<CrudPageResult<DualControlRequestDto>>
  /** Hydrate one request - the drawer reads the payload snapshot from here. */
  getById(id: string): Promise<DualControlRequestDto>
  /** Approve. Fails server-side when you are the requester. */
  approve(id: string, comment?: string): Promise<DualControlRequestDto>
  /** Reject. Same permission as approving - whoever may approve may also refuse. */
  reject(id: string, reason?: string): Promise<DualControlRequestDto>
  /** Withdraw your own pending request. The service allows only the requester. */
  cancel(id: string): Promise<void>
}

export interface DualControlBridge {
  requests: DualControlContract
}

const unavailable = (name: string) => (): Promise<never> =>
  Promise.reject(new Error(`dual-control-bridge: ${name} - no deps provided`))

export function createDualControlBridge(deps: DualControlBridgeDeps = {}): DualControlBridge {
  const api = deps.adminDualControlApi ?? (deps.client ? useAdminDualControlApi(deps.client) : null)

  if (!api) {
    return {
      requests: {
        fetch: unavailable('requests.fetch') as never,
        getById: unavailable('requests.getById'),
        approve: unavailable('requests.approve'),
        reject: unavailable('requests.reject'),
        cancel: unavailable('requests.cancel'),
      },
    }
  }

  const a = api

  async function fetchRequests(query: CrudPageQuery): Promise<CrudPageResult<DualControlRequestDto>> {
    const params = mapQueryToListRequest(query) as unknown as DualControlQueryDto
    const result = unwrap<CrudPageResult<DualControlRequestDto>>(await a.query(params))
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  // An empty comment and no comment are the same thing to the backend
  // (`DualControlDecisionDto.Comment` is optional), so don't send `""`.
  const decision = (comment?: string): DualControlDecisionDto | undefined =>
    comment && comment.trim() ? { comment: comment.trim() } : undefined

  const requests: DualControlContract = {
    fetch: fetchRequests,
    getById: async (id: string) => unwrap<DualControlRequestDto>(await a.get(id)),
    approve: async (id: string, comment?: string) =>
      unwrapOk<DualControlRequestDto>(await a.approve(id, decision(comment))),
    reject: async (id: string, reason?: string) =>
      unwrapOk<DualControlRequestDto>(await a.reject(id, decision(reason))),
    cancel: async (id: string) => {
      ensureOk(await a.cancel(id))
    },
  }

  return { requests }
}
