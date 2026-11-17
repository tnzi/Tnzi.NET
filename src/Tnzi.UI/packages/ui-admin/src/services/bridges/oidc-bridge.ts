/**
 * OIDC bridge - adapts `Tnzi.Identity.OpenIdConnect`'s admin API to the CRUD
 * contracts the client-registry and grants pages consume.
 *
 * Sub-contracts:
 *   - clients → useAdminOidcApi (list / create / update / enable / rotate / delete)
 *   - grants  → useAdminOidcApi (list / revoke)
 *
 * ★ `clients.create` and `clients.rotateSecret` return the PLAINTEXT client
 *   secret, and the server will never hand it over again (it stores a hash).
 *   The page shows it once; a lost secret can only be rotated.
 *
 * `grants.create` / `grants.update` reject: grants come from a user signing in
 * or consenting, and an administrator has no "consent on someone's behalf".
 */
import {
  useAdminOidcApi,
  type CreateOidcClientDto,
  type OidcClientDto,
  type OidcClientQueryDto,
  type OidcClientSecretDto,
  type OidcGrantDto,
  type OidcGrantQueryDto,
  type UpdateOidcClientDto,
} from '@tnzi/core/services/oidc'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, mapQueryToListRequest, pagedResult, unwrapOk } from '../_mappers'

type HttpClient = Parameters<typeof useAdminOidcApi>[0]

export interface OidcBridgeDeps {
  /** Production path: provide HttpClient; the bridge builds the API internally. */
  client?: HttpClient
  /** Test path: inject a mock API directly. */
  adminOidcApi?: ReturnType<typeof useAdminOidcApi>
}

/** Client registry. Create returns the one-time secret alongside the client. */
export interface OidcClientContract {
  fetch(query: CrudPageQuery): Promise<CrudPageResult<OidcClientDto>>
  create(data: CreateOidcClientDto): Promise<OidcClientSecretDto>
  update(id: string, data: UpdateOidcClientDto): Promise<OidcClientDto>
  delete(ids: string[]): Promise<void>
  /** Disabling revokes every grant and token under the client. */
  setEnabled(id: string, enabled: boolean): Promise<void>
  /** New secret; the old one stops working immediately. */
  rotateSecret(id: string): Promise<OidcClientSecretDto>
}

export type OidcGrantContract = BridgeCrudContract<OidcGrantDto>

export interface OidcBridge {
  clients: OidcClientContract
  grants: OidcGrantContract
}

const unavailable = (name: string) => (): Promise<never> =>
  Promise.reject(new Error(`oidc-bridge: ${name} - no deps provided`))

const unsupported = (name: string, reason: string) => (): Promise<never> =>
  Promise.reject(new Error(`oidc-bridge: ${name} is not supported - ${reason}`))

export function createOidcBridge(deps: OidcBridgeDeps = {}): OidcBridge {
  const api = deps.adminOidcApi ?? (deps.client ? useAdminOidcApi(deps.client) : null)

  if (!api) {
    return {
      clients: {
        fetch: unavailable('clients.fetch') as never,
        create: unavailable('clients.create'),
        update: unavailable('clients.update'),
        delete: unavailable('clients.delete'),
        setEnabled: unavailable('clients.setEnabled'),
        rotateSecret: unavailable('clients.rotateSecret'),
      },
      grants: {
        fetch: unavailable('grants.fetch') as never,
        create: unavailable('grants.create'),
        update: unavailable('grants.update'),
        delete: unavailable('grants.delete'),
      },
    }
  }

  const a = api

  const clients: OidcClientContract = {
    fetch: async (query) => {
      const params = mapQueryToListRequest(query) as unknown as OidcClientQueryDto
      const result = unwrapOk<CrudPageResult<OidcClientDto>>(await a.getClients(params))
      return pagedResult({
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      })
    },
    create: async (data) => unwrapOk<OidcClientSecretDto>(await a.createClient(data)),
    update: async (id, data) => unwrapOk<OidcClientDto>(await a.updateClient(id, data)),
    delete: async (ids) => {
      // No batch endpoint: deleting a client is rare and cascades to its grants
      // and tokens, so a per-id loop that stops at the first refusal is the honest shape.
      for (const id of ids) {
        ensureOk(await a.deleteClient(id))
      }
    },
    setEnabled: async (id, enabled) => {
      ensureOk(await a.setClientEnabled(id, enabled))
    },
    rotateSecret: async (id) => unwrapOk<OidcClientSecretDto>(await a.rotateClientSecret(id)),
  }

  const grants: OidcGrantContract = {
    fetch: async (query) => {
      const params = mapQueryToListRequest(query) as unknown as OidcGrantQueryDto
      const result = unwrapOk<CrudPageResult<OidcGrantDto>>(await a.getGrants(params))
      return pagedResult({
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      })
    },
    create: unsupported('grants.create', 'grants come from the user signing in or consenting'),
    update: unsupported('grants.update', 'a grant has no editable fields; revoke it instead'),
    delete: async (ids) => {
      for (const id of ids) {
        ensureOk(await a.revokeGrant(id))
      }
    },
  }

  return { clients, grants }
}
