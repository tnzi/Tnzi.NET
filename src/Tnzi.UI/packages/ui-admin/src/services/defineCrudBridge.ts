/**
 * `defineCrudBridge` / `defineChildBridge` - factories for the ubiquitous
 * "plain REST resource" bridge shape so consumer apps declare an endpoint base
 * instead of hand-writing the same `unwrapResult(await client.post(...))` /
 * `ensureOk(await client.delete(...))` plumbing for every resource.
 *
 * The framework's own built-in bridges wrap structured `useXxxApi(client)`
 * sub-contracts; these factories target the other common case - an app hitting
 * a conventional REST controller (`POST {base}/query`, `POST {base}`,
 * `PUT {base}/{id}`, `DELETE {base}/batch`, `GET {base}/{child}/by-parent/{id}`)
 * directly, which is exactly the per-resource boilerplate a consumer repeats.
 *
 * The result already satisfies `BridgeCrudContract<TDto>` so it plugs straight
 * into `useCrudPage({ fetchData: bridge.fetch, createData: bridge.create, ... })`.
 */
import type { HttpClient } from '@tnzi/core'
import { ensureOk, mapQueryToListRequest, pagedResult, unwrapResult } from './_mappers'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from './types'

interface PagedEnvelope<T> {
  items: T[]
  totalCount: number
  pageIndex: number
  pageSize: number
}

export interface CrudBridgeOptions<TCreateDto, TUpdateDto> {
  /** Map create form data → request body before POST. Default: pass through. */
  toCreate?: (data: TCreateDto) => unknown
  /** Map update form data → request body before PUT. Default: pass through. */
  toUpdate?: (id: string, data: TUpdateDto) => unknown
  /**
   * Delete strategy. `'batch'` → `DELETE {base}/batch` with `body: ids`
   * (default); `'single'` → `DELETE {base}/{id}` per id.
   */
  deleteMode?: 'batch' | 'single'
  /** List query path suffix (default `'query'` → `POST {base}/query`). */
  queryPath?: string
}

export interface CrudBridge<TDto, TCreateDto, TUpdateDto, TId extends string>
  extends BridgeCrudContract<TDto, TCreateDto, TUpdateDto, TId> {
  /** `GET {base}/{id}` - full detail record. */
  getDetail(id: TId): Promise<TDto>
  /** `GET {base}` - unpaged full list (small lookups). */
  listAll(): Promise<TDto[]>
  /** Create when `id` is null, else update - for a shared create/edit form. */
  save(id: TId | null, data: TCreateDto | TUpdateDto): Promise<TDto>
}

export function defineCrudBridge<
  TDto,
  TCreateDto = Partial<TDto>,
  TUpdateDto = Partial<TDto>,
  TId extends string = string,
>(
  client: HttpClient,
  base: string,
  options: CrudBridgeOptions<TCreateDto, TUpdateDto> = {},
): CrudBridge<TDto, TCreateDto, TUpdateDto, TId> {
  const { toCreate, toUpdate, deleteMode = 'batch', queryPath = 'query' } = options
  const createBody = (data: TCreateDto): unknown => (toCreate ? toCreate(data) : data)
  const updateBody = (id: string, data: TUpdateDto): unknown => (toUpdate ? toUpdate(id, data) : data)

  return {
    async fetch(query: CrudPageQuery): Promise<CrudPageResult<TDto>> {
      const env = await client.post<PagedEnvelope<TDto>>(`${base}/${queryPath}`, mapQueryToListRequest(query))
      return pagedResult(unwrapResult<PagedEnvelope<TDto>>(env))
    },
    async create(data: TCreateDto): Promise<TDto> {
      return unwrapResult<TDto>(await client.post<TDto>(base, createBody(data)))
    },
    async update(id: TId, data: TUpdateDto): Promise<TDto> {
      return unwrapResult<TDto>(await client.put<TDto>(`${base}/${id}`, updateBody(id, data)))
    },
    async delete(ids: TId[]): Promise<void> {
      if (deleteMode === 'single') {
        // Discarded-result writes MUST ensureOk so a business refusal (e.g. a
        // 409 delete veto) surfaces as a thrown error instead of resolving.
        for (const id of ids) ensureOk(await client.delete<void>(`${base}/${id}`))
      } else {
        ensureOk(await client.delete<void>(`${base}/batch`, { body: ids }))
      }
    },
    async getDetail(id: TId): Promise<TDto> {
      return unwrapResult<TDto>(await client.get<TDto>(`${base}/${id}`))
    },
    async listAll(): Promise<TDto[]> {
      return unwrapResult<TDto[]>(await client.get<TDto[]>(base))
    },
    async save(id: TId | null, data: TCreateDto | TUpdateDto): Promise<TDto> {
      return id
        ? unwrapResult<TDto>(await client.put<TDto>(`${base}/${id}`, updateBody(id, data as TUpdateDto)))
        : unwrapResult<TDto>(await client.post<TDto>(base, createBody(data as TCreateDto)))
    },
  }
}

export interface ChildBridge<TDto> {
  /** `GET {base}/{parentSegment}/{parentId}` - all children of one parent. */
  byParent(parentId: string): Promise<TDto[]>
  create(data: unknown): Promise<TDto>
  update(id: string, data: unknown): Promise<TDto>
  delete(id: string): Promise<void>
}

/**
 * A business sub-resource listed by its parent id
 * (`GET {base}/{parentSegment}/{parentId}` + create/update/delete). Collapses
 * the repeated "by-parent list + CRUD" shape (matter parties / key-dates /
 * documents / expenses …) into one declaration.
 */
export function defineChildBridge<TDto>(
  client: HttpClient,
  base: string,
  parentSegment: string,
): ChildBridge<TDto> {
  return {
    async byParent(parentId: string): Promise<TDto[]> {
      return unwrapResult<TDto[]>(await client.get<TDto[]>(`${base}/${parentSegment}/${parentId}`))
    },
    async create(data: unknown): Promise<TDto> {
      return unwrapResult<TDto>(await client.post<TDto>(base, data))
    },
    async update(id: string, data: unknown): Promise<TDto> {
      return unwrapResult<TDto>(await client.put<TDto>(`${base}/${id}`, data))
    },
    async delete(id: string): Promise<void> {
      ensureOk(await client.delete<void>(`${base}/${id}`))
    },
  }
}
