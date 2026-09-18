import { describe, it, expect, vi } from 'vitest'
import { defineCrudBridge, defineChildBridge } from '../../src/services/defineCrudBridge'
import type { HttpClient } from '@tnzi/core'
import type { CrudPageQuery } from '../../src/services/types'

function ok<T>(data: T) {
  return { succeeded: true, success: true, code: 200, data }
}
function fail(message: string) {
  return { succeeded: false, success: false, code: 409, data: null, message }
}

function mockClient(overrides: Partial<Record<'get' | 'post' | 'put' | 'delete', unknown>> = {}): HttpClient {
  return {
    get: vi.fn(async (url: string) => ok({ url })),
    post: vi.fn(async (url: string, body?: unknown) => ok({ url, body })),
    put: vi.fn(async (url: string, body?: unknown) => ok({ url, body })),
    delete: vi.fn(async () => ok(undefined)),
    ...overrides,
  } as unknown as HttpClient
}

interface Dto { id: string; name: string }
const q = (over: Partial<CrudPageQuery> = {}): CrudPageQuery =>
  ({ pageIndex: 1, pageSize: 20, ...over } as CrudPageQuery)

describe('defineCrudBridge', () => {
  it('fetch posts to {base}/query with the mapped query and returns a PagedList', async () => {
    const post = vi.fn(async () => ok({ items: [{ id: '1', name: 'a' }], totalCount: 1, pageIndex: 1, pageSize: 20 }))
    const bridge = defineCrudBridge<Dto>(mockClient({ post }), '/admin/things')
    const page = await bridge.fetch(q({ searchText: 'x' }))
    expect(post).toHaveBeenCalledWith('/admin/things/query', expect.objectContaining({ pageIndex: 1, pageSize: 20, keyword: 'x' }))
    expect(page.items).toHaveLength(1)
    expect(page.totalCount).toBe(1)
    expect(page.totalPages).toBe(1) // pagedResult fills derived fields
  })

  it('create posts to {base} and unwraps the data', async () => {
    const post = vi.fn(async () => ok({ id: '9', name: 'new' }))
    const created = await defineCrudBridge<Dto>(mockClient({ post }), '/admin/things').create({ name: 'new' } as Partial<Dto>)
    expect(post).toHaveBeenCalledWith('/admin/things', { name: 'new' })
    expect(created).toEqual({ id: '9', name: 'new' })
  })

  it('update puts to {base}/{id}', async () => {
    const put = vi.fn(async () => ok({ id: '9', name: 'upd' }))
    await defineCrudBridge<Dto>(mockClient({ put }), '/admin/things').update('9', { name: 'upd' } as Partial<Dto>)
    expect(put).toHaveBeenCalledWith('/admin/things/9', { name: 'upd' })
  })

  it('delete (batch, default) deletes {base}/batch with body ids', async () => {
    const del = vi.fn(async () => ok(undefined))
    await defineCrudBridge<Dto>(mockClient({ delete: del }), '/admin/things').delete(['1', '2'])
    expect(del).toHaveBeenCalledWith('/admin/things/batch', { body: ['1', '2'] })
  })

  it('delete (single mode) deletes {base}/{id} per id', async () => {
    const del = vi.fn(async () => ok(undefined))
    await defineCrudBridge<Dto>(mockClient({ delete: del }), '/admin/things', { deleteMode: 'single' }).delete(['1', '2'])
    expect(del).toHaveBeenNthCalledWith(1, '/admin/things/1')
    expect(del).toHaveBeenNthCalledWith(2, '/admin/things/2')
  })

  it('delete throws when the server refuses (ensureOk surfaces business failures)', async () => {
    const bridge = defineCrudBridge<Dto>(mockClient({ delete: vi.fn(async () => fail('has dependents')) }), '/admin/things')
    await expect(bridge.delete(['1'])).rejects.toThrow('has dependents')
  })

  it('applies toCreate / toUpdate body mappers', async () => {
    const post = vi.fn(async () => ok({ id: '1', name: 'x' }))
    const put = vi.fn(async () => ok({ id: '1', name: 'x' }))
    const bridge = defineCrudBridge<Dto>(mockClient({ post, put }), '/admin/things', {
      toCreate: (d) => ({ wrapped: d }),
      toUpdate: (id, d) => ({ id, wrapped: d }),
    })
    await bridge.create({ name: 'x' } as Partial<Dto>)
    expect(post).toHaveBeenCalledWith('/admin/things', { wrapped: { name: 'x' } })
    await bridge.update('1', { name: 'y' } as Partial<Dto>)
    expect(put).toHaveBeenCalledWith('/admin/things/1', { id: '1', wrapped: { name: 'y' } })
  })

  // A business refusal is RESOLVED by HttpClient as `{ succeeded: false, data: undefined }`.
  // `useCrudPage.submit` only routes thrown errors to the error path, so a factory that
  // resolves `undefined` here gets a green "created" toast, a closed form and a list
  // without the row. Every write below must reject with the server's own message.
  describe('rejects with the server message when the envelope is failed', () => {
    const refused = { post: vi.fn(async () => fail('duplicate code')), put: vi.fn(async () => fail('period locked')) }

    it('create', async () => {
      const bridge = defineCrudBridge<Dto>(mockClient(refused), '/admin/things')
      await expect(bridge.create({ name: 'x' } as Partial<Dto>)).rejects.toThrow('duplicate code')
    })

    it('update', async () => {
      const bridge = defineCrudBridge<Dto>(mockClient(refused), '/admin/things')
      await expect(bridge.update('1', { name: 'x' } as Partial<Dto>)).rejects.toThrow('period locked')
    })

    it('save(null) and save(id)', async () => {
      const bridge = defineCrudBridge<Dto>(mockClient(refused), '/admin/things')
      await expect(bridge.save(null, { name: 'x' } as Partial<Dto>)).rejects.toThrow('duplicate code')
      await expect(bridge.save('1', { name: 'x' } as Partial<Dto>)).rejects.toThrow('period locked')
    })

    // Reads too: the declared return type is `TDto`, and `undefined` would only move the
    // failure to a later TypeError that no longer carries the server's reason.
    it('fetch / getDetail / listAll', async () => {
      const bridge = defineCrudBridge<Dto>(
        mockClient({ get: vi.fn(async () => fail('not found')), post: vi.fn(async () => fail('forbidden')) }),
        '/admin/things',
      )
      await expect(bridge.fetch(q())).rejects.toThrow('forbidden')
      await expect(bridge.getDetail('1')).rejects.toThrow('not found')
      await expect(bridge.listAll()).rejects.toThrow('not found')
    })
  })

  it('save routes to create (null id) or update (id present)', async () => {
    const post = vi.fn(async () => ok({ id: 'new', name: 'n' }))
    const put = vi.fn(async () => ok({ id: '5', name: 'u' }))
    const bridge = defineCrudBridge<Dto>(mockClient({ post, put }), '/admin/things')
    await bridge.save(null, { name: 'n' } as Partial<Dto>)
    expect(post).toHaveBeenCalledWith('/admin/things', { name: 'n' })
    await bridge.save('5', { name: 'u' } as Partial<Dto>)
    expect(put).toHaveBeenCalledWith('/admin/things/5', { name: 'u' })
  })
})

describe('defineChildBridge', () => {
  it('byParent gets {base}/{segment}/{parentId}', async () => {
    const get = vi.fn(async () => ok([{ id: 'c1' }]))
    const child = defineChildBridge<{ id: string }>(mockClient({ get }), '/admin/matters/parties', 'by-matter')
    expect(await child.byParent('m1')).toEqual([{ id: 'c1' }])
    expect(get).toHaveBeenCalledWith('/admin/matters/parties/by-matter/m1')
  })

  it('delete ensures success (throws on refusal)', async () => {
    const child = defineChildBridge<{ id: string }>(mockClient({ delete: vi.fn(async () => fail('locked')) }), '/admin/matters/parties', 'by-matter')
    await expect(child.delete('c1')).rejects.toThrow('locked')
  })

  it('create / update / byParent reject on a failed envelope', async () => {
    const child = defineChildBridge<{ id: string }>(
      mockClient({
        post: vi.fn(async () => fail('party exists')),
        put: vi.fn(async () => fail('matter closed')),
        get: vi.fn(async () => fail('no access')),
      }),
      '/admin/matters/parties',
      'by-matter',
    )
    await expect(child.create({ name: 'p' })).rejects.toThrow('party exists')
    await expect(child.update('c1', { name: 'p' })).rejects.toThrow('matter closed')
    await expect(child.byParent('m1')).rejects.toThrow('no access')
  })
})

