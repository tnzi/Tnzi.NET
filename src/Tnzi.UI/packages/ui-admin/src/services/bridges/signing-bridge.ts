/**
 * Signing bridge - adapts `Tnzi.Signing`'s admin API to the CRUD contracts the
 * signing pages consume.
 *
 * Sub-contracts:
 *   - requests  → useAdminSigningApi (list / detail / create / send / void)
 *   - templates → useAdminSigningApi (list / detail / CRUD)
 *
 * Two shapes here are deliberately NOT the generic CRUD ones:
 *
 *   requests.update  - a signing request has no editable body. Once it is out,
 *                      the only moves are "send" and "void", and both are
 *                      lifecycle transitions rather than edits. Rejects.
 *   requests.delete  - likewise: a request that went out to real people is
 *                      evidence. Voiding records that it was called off;
 *                      deleting would erase that it ever existed. Rejects, and
 *                      the page offers Void instead.
 *
 * ★ `requests.send` returns the PLAINTEXT tokens, and the backend will never
 *   hand them over again (it stores hashes). The caller owns getting them to the
 *   recipients from that moment on.
 */
import {
  useAdminSigningApi,
  type CreateEnvelopeDto,
  type CreateEnvelopeTemplateDto,
  type EnvelopeDto,
  type EnvelopeListDto,
  type EnvelopeQueryDto,
  type EnvelopeTemplateDto,
  type EnvelopeTemplateListDto,
  type EnvelopeTemplateQueryDto,
  type IssuedSigningLink,
  type UpdateEnvelopeTemplateDto,
} from '@tnzi/core/services/signing'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, mapQueryToListRequest, pagedResult, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminSigningApi>[0]

export interface SigningBridgeDeps {
  /** Production path: provide HttpClient; the bridge builds the API internally. */
  client?: HttpClient
  /** Test path: inject a mock API directly. */
  adminSigningApi?: ReturnType<typeof useAdminSigningApi>
}

/** Signing requests. Lifecycle transitions instead of edit/delete. */
export interface SigningRequestContract extends BridgeCrudContract<EnvelopeListDto, CreateEnvelopeDto> {
  /** Hydrate a list row into the full detail, including per-recipient progress. */
  getById(id: string): Promise<EnvelopeDto>
  /**
   * Issue the one-time signing links.
   *
   * ★ The returned tokens are plaintext and are returned exactly once. Show them
   * to the operator or dispatch them immediately - re-sending invalidates the
   * links already out there.
   */
  send(id: string): Promise<IssuedSigningLink[]>
  /** Call off a request that has not completed. */
  void(id: string): Promise<void>
}

export interface SigningTemplateContract
  extends BridgeCrudContract<EnvelopeTemplateListDto, CreateEnvelopeTemplateDto, UpdateEnvelopeTemplateDto> {
  /** Hydrate a list row into the full template, including its placed fields. */
  getById(id: string): Promise<EnvelopeTemplateDto>
}

export interface SigningBridge {
  requests: SigningRequestContract
  templates: SigningTemplateContract
}

const unavailable = (name: string) => (): Promise<never> =>
  Promise.reject(new Error(`signing-bridge: ${name} - no deps provided`))

const unsupported = (name: string, instead: string) => (): Promise<never> =>
  Promise.reject(new Error(`signing-bridge: ${name} is not supported - use ${instead}`))

export function createSigningBridge(deps: SigningBridgeDeps = {}): SigningBridge {
  const api = deps.adminSigningApi ?? (deps.client ? useAdminSigningApi(deps.client) : null)

  if (!api) {
    return {
      requests: {
        fetch: unavailable('requests.fetch') as never,
        getById: unavailable('requests.getById'),
        create: unavailable('requests.create'),
        update: unavailable('requests.update'),
        delete: unavailable('requests.delete'),
        send: unavailable('requests.send'),
        void: unavailable('requests.void'),
      },
      templates: {
        fetch: unavailable('templates.fetch') as never,
        getById: unavailable('templates.getById'),
        create: unavailable('templates.create'),
        update: unavailable('templates.update'),
        delete: unavailable('templates.delete'),
      },
    }
  }

  const a = api

  // ---- requests ----

  async function fetchRequests(query: CrudPageQuery): Promise<CrudPageResult<EnvelopeListDto>> {
    const params = mapQueryToListRequest(query) as unknown as EnvelopeQueryDto
    const result = unwrap<CrudPageResult<EnvelopeListDto>>(await a.getRequests(params))
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  const requests: SigningRequestContract = {
    fetch: fetchRequests,
    getById: async (id: string) => unwrap<EnvelopeDto>(await a.getRequest(id)),
    create: async (data: CreateEnvelopeDto) => {
      // The create endpoint returns the full detail; the list row is a subset of
      // it, so handing it straight back keeps the page from re-fetching.
      const created = unwrap<EnvelopeDto>(await a.createRequest(data))
      return created as unknown as EnvelopeListDto
    },
    update: unsupported('requests.update', 'send / void'),
    delete: unsupported('requests.delete', 'void (a dispatched request is evidence)'),
    send: async (id: string) => unwrap<IssuedSigningLink[]>(await a.sendRequest(id)) ?? [],
    void: async (id: string) => {
      ensureOk(await a.voidRequest(id))
    },
  }

  // ---- templates ----

  async function fetchTemplates(query: CrudPageQuery): Promise<CrudPageResult<EnvelopeTemplateListDto>> {
    const params = mapQueryToListRequest(query) as unknown as EnvelopeTemplateQueryDto
    const result = unwrap<CrudPageResult<EnvelopeTemplateListDto>>(await a.getTemplates(params))
    return pagedResult({
      items: result.items ?? [],
      totalCount: result.totalCount ?? 0,
      pageIndex: result.pageIndex ?? query.pageIndex,
      pageSize: result.pageSize ?? query.pageSize,
    })
  }

  const templates: SigningTemplateContract = {
    fetch: fetchTemplates,
    getById: async (id: string) => unwrap<EnvelopeTemplateDto>(await a.getTemplate(id)),
    create: async (data: CreateEnvelopeTemplateDto) => {
      const created = unwrap<EnvelopeTemplateDto>(await a.createTemplate(data))
      return created as EnvelopeTemplateListDto
    },
    update: async (id: string, data: UpdateEnvelopeTemplateDto) => {
      const updated = unwrap<EnvelopeTemplateDto>(await a.updateTemplate(id, data))
      return updated as EnvelopeTemplateListDto
    },
    delete: async (ids: string[]) => {
      // No batch endpoint: deleting a template is rare and is rejected outright
      // once any request has referenced it, so a per-id loop is the honest shape.
      for (const id of ids) {
        ensureOk(await a.deleteTemplate(id))
      }
    },
  }

  return { requests, templates }
}
