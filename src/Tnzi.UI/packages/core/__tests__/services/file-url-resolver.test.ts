import { describe, it, expect, vi } from 'vitest'
import { createFileUrlResolver } from '../../src/services/storage/file-url'
import type { FileAccessTokenDto } from '../../src/services/storage/types'

function inTenMinutes(): string {
  return new Date(Date.now() + 10 * 60_000).toISOString()
}

function mockClient(mint: (ids: string[]) => FileAccessTokenDto[]) {
  // `succeeded` (not `success`) is the field isSuccess reads - the wire name.
  const post = vi.fn(async (_url: string, body: unknown) => ({
    succeeded: true,
    code: 200,
    data: mint((body as { fileIds: string[] }).fileIds),
  }))
  return {
    client: {
      get: vi.fn(),
      post,
      resolveUrl: vi.fn((p: string) => `/api${p}`),
    },
    post,
  }
}

/** Mint a token for every requested id. */
const mintAll = (ids: string[]): FileAccessTokenDto[] =>
  ids.map((id) => ({ fileId: id, token: `tok-${id}`, expiresAt: inTenMinutes() }))

describe('createFileUrlResolver', () => {
  // Real timers on purpose: the batch flush sits behind a 0ms timer, and the
  // promise a caller awaits only settles after it runs. Awaiting is therefore
  // already deterministic - fake timers would only add ceremony.
  const settle = <T>(promise: Promise<T>): Promise<T> => promise

  it('appends the minted token as the sig query parameter', async () => {
    const { client } = mockClient(mintAll)
    const resolver = createFileUrlResolver(client as never)

    const url = await settle(resolver.resolve('f1'))

    expect(url).toBe('/api/files/f1/preview?sig=tok-f1')
  })

  it('points at the endpoint matching the requested kind', async () => {
    const { client } = mockClient(mintAll)
    const resolver = createFileUrlResolver(client as never)

    const [preview, download, thumb] = await settle(
      Promise.all([
        resolver.resolve('f1', 'preview'),
        resolver.resolve('f1', 'download'),
        resolver.resolve('f1', 'thumbnail'),
      ]),
    )

    expect(preview).toContain('/files/f1/preview?sig=')
    expect(download).toContain('/files/f1/download?sig=')
    expect(thumb).toContain('/files/f1/thumbnail?sig=')
  })

  it('coalesces every resolve in one tick into a single batch request', async () => {
    // A message list mounts N bubbles at once. Without batching that is N round
    // trips for one screen.
    const { client, post } = mockClient(mintAll)
    const resolver = createFileUrlResolver(client as never)

    const urls = await settle(
      Promise.all(['a', 'b', 'c'].map((id) => resolver.resolve(id))),
    )

    expect(post).toHaveBeenCalledTimes(1)
    expect(post.mock.calls[0][1]).toEqual({ fileIds: ['a', 'b', 'c'], expiresInSeconds: undefined })
    expect(urls.every((u) => u !== null)).toBe(true)
  })

  it('reuses a cached token instead of minting again', async () => {
    const { client, post } = mockClient(mintAll)
    const resolver = createFileUrlResolver(client as never)

    await settle(resolver.resolve('f1'))
    const again = await settle(resolver.resolve('f1', 'download'))

    expect(post).toHaveBeenCalledTimes(1)
    expect(again).toBe('/api/files/f1/download?sig=tok-f1')
  })

  it('re-mints once the cached token is close to expiring', async () => {
    // A page open for a long time must not start 404-ing its own images.
    const start = Date.now()
    const now = vi.spyOn(Date, 'now').mockReturnValue(start)
    try {
      const { client, post } = mockClient(mintAll)
      const resolver = createFileUrlResolver(client as never, { refreshMarginMs: 60_000 })

      await settle(resolver.resolve('f1'))
      now.mockReturnValue(start + 9 * 60_000 - 1)
      await settle(resolver.resolve('f1'))
      expect(post).toHaveBeenCalledTimes(1)

      now.mockReturnValue(start + 9 * 60_000)
      await settle(resolver.resolve('f1'))
      expect(post).toHaveBeenCalledTimes(2)
    } finally {
      now.mockRestore()
    }
  })

  it('caps the refresh margin at half a token lifetime shorter than the margin', async () => {
    // A 30s TTL under a 60s margin used to be stale on arrival: every render
    // minted again, and a view re-signing on refreshAt() would spin.
    const start = Date.now()
    const now = vi.spyOn(Date, 'now').mockReturnValue(start)
    try {
      const { client, post } = mockClient((ids) =>
        ids.map((id) => ({ fileId: id, token: `tok-${id}`, expiresAt: new Date(start + 30_000).toISOString() })),
      )
      const resolver = createFileUrlResolver(client as never, { refreshMarginMs: 60_000 })

      await settle(resolver.resolve('f1'))
      expect(resolver.refreshAt(['f1'])).toBe(start + 15_000)
      await settle(resolver.resolve('f1'))
      expect(post).toHaveBeenCalledTimes(1)
    } finally {
      now.mockRestore()
    }
  })

  it('refreshAt() reports the earliest stale point among the ids, ignoring the uncached', async () => {
    const start = Date.now()
    const now = vi.spyOn(Date, 'now').mockReturnValue(start)
    try {
      const expiries: Record<string, number> = { a: start + 10 * 60_000, b: start + 5 * 60_000 }
      const { client } = mockClient((ids) =>
        ids.map((id) => ({ fileId: id, token: `tok-${id}`, expiresAt: new Date(expiries[id]).toISOString() })),
      )
      const resolver = createFileUrlResolver(client as never, { refreshMarginMs: 60_000 })

      await settle(resolver.resolveMany(['a', 'b']))

      expect(resolver.refreshAt(['a', 'b', 'never-minted'])).toBe(start + 4 * 60_000)
      expect(resolver.refreshAt(['never-minted'])).toBeNull()
      // A token already past its stale point is not "due later".
      now.mockReturnValue(start + 4 * 60_000)
      expect(resolver.refreshAt(['b'])).toBeNull()
      expect(resolver.refreshAt(['a', 'b'])).toBe(start + 9 * 60_000)
    } finally {
      now.mockRestore()
    }
  })

  it('invalidate() makes the next resolve mint a new token', async () => {
    const start = Date.now()
    const now = vi.spyOn(Date, 'now').mockReturnValue(start)
    try {
      let round = 0
      const { client, post } = mockClient((ids) => {
        round += 1
        return ids.map((id) => ({ fileId: id, token: `tok-${id}-${round}`, expiresAt: inTenMinutes() }))
      })
      const resolver = createFileUrlResolver(client as never)

      await settle(resolver.resolveMany(['a', 'b']))
      now.mockReturnValue(start + 60_000)
      resolver.invalidate(['a'])
      const map = await settle(resolver.resolveMany(['a', 'b']))

      expect(post).toHaveBeenCalledTimes(2)
      // Only the invalidated id was asked for again.
      expect(post.mock.calls[1][1]).toEqual({ fileIds: ['a'], expiresInSeconds: undefined })
      expect(map.get('a')).toContain('sig=tok-a-2')
      expect(map.get('b')).toContain('sig=tok-b-1')
    } finally {
      now.mockRestore()
    }
  })

  it('invalidate() keeps a token minted moments ago', async () => {
    // An <img> that fails for another reason (no thumbnail for that type) calls
    // refresh again on the new URL; a fresh token keeps the URL the same and
    // the browser does not loop.
    const start = Date.now()
    const now = vi.spyOn(Date, 'now').mockReturnValue(start)
    try {
      const { client, post } = mockClient(mintAll)
      const resolver = createFileUrlResolver(client as never, { minReissueIntervalMs: 10_000 })

      await settle(resolver.resolve('f1'))
      now.mockReturnValue(start + 9_999)
      resolver.invalidate(['f1'])
      await settle(resolver.resolve('f1'))

      expect(post).toHaveBeenCalledTimes(1)
    } finally {
      now.mockRestore()
    }
  })

  it('returns null for an id the backend omitted', async () => {
    // Omission is how the backend says "you may not read this" without
    // revealing whether the id exists - it must not hang or throw.
    const { client } = mockClient((ids) => mintAll(ids.filter((id) => id !== 'forbidden')))
    const resolver = createFileUrlResolver(client as never)

    const [ok, denied] = await settle(
      Promise.all([resolver.resolve('f1'), resolver.resolve('forbidden')]),
    )

    expect(ok).toContain('sig=')
    expect(denied).toBeNull()
  })

  it('returns null instead of throwing when the mint request fails', async () => {
    // A broken thumbnail must not take down the list that contains it.
    const client = {
      get: vi.fn(),
      post: vi.fn(async () => {
        throw new Error('network down')
      }),
      resolveUrl: vi.fn((p: string) => `/api${p}`),
    }
    const resolver = createFileUrlResolver(client as never)

    expect(await settle(resolver.resolve('f1'))).toBeNull()
  })

  it('splits oversized batches and still settles every id', async () => {
    const { client, post } = mockClient(mintAll)
    const resolver = createFileUrlResolver(client as never, { maxBatchSize: 2 })

    const ids = ['a', 'b', 'c', 'd', 'e']
    const urls = await Promise.all(ids.map((id) => resolver.resolve(id)))

    expect(post).toHaveBeenCalledTimes(3)
    expect(urls.filter(Boolean)).toHaveLength(5)
  })

  it('resolveMany returns a map without the unreadable ids', async () => {
    const { client } = mockClient((ids) => mintAll(ids.filter((id) => id !== 'nope')))
    const resolver = createFileUrlResolver(client as never)

    const map = await settle(resolver.resolveMany(['a', 'nope', 'a']))

    expect(map.size).toBe(1)
    expect(map.get('a')).toContain('sig=tok-a')
  })

  it('plain() builds an unsigned URL for public files', () => {
    const { client, post } = mockClient(mintAll)
    const resolver = createFileUrlResolver(client as never)

    expect(resolver.plain('f1')).toBe('/api/files/f1/preview')
    expect(post).not.toHaveBeenCalled()
  })

  it('clear() drops cached tokens so a new session mints its own', async () => {
    // Tokens are minted against a session; carrying them across a user switch
    // would let the next user render the previous one's files.
    const { client, post } = mockClient(mintAll)
    const resolver = createFileUrlResolver(client as never)

    await settle(resolver.resolve('f1'))
    resolver.clear()
    await settle(resolver.resolve('f1'))

    expect(post).toHaveBeenCalledTimes(2)
  })

  it('ignores empty ids without a round trip', async () => {
    const { client, post } = mockClient(mintAll)
    const resolver = createFileUrlResolver(client as never)

    expect(await resolver.resolve('')).toBeNull()
    expect(post).not.toHaveBeenCalled()
  })
})

describe('createFileUrlResolver while a batch is in flight', () => {
  /** A client whose mint requests stay open until the test answers them. */
  function heldClient() {
    const requests: { ids: string[]; answer: () => void }[] = []
    const post = vi.fn(
      (_url: string, body: unknown) =>
        new Promise((resolve) => {
          const ids = (body as { fileIds: string[] }).fileIds
          requests.push({
            ids,
            answer: () => resolve({ succeeded: true, code: 200, data: mintAll(ids) }),
          })
        }),
    )
    return {
      client: { get: vi.fn(), post, resolveUrl: vi.fn((p: string) => `/api${p}`) },
      post,
      requests,
    }
  }

  /** Let the 0ms batch timer fire. */
  const tick = () => new Promise<void>((r) => setTimeout(r, 0))

  it('does not request an id again that the open batch already asked for', async () => {
    // A list mounts (batch 1 out), then one more bubble resolves an id from it
    // plus a new one. Batch 2 used to repeat all of batch 1, and every mint
    // runs the server's full read check.
    const { client, post, requests } = heldClient()
    const resolver = createFileUrlResolver(client as never)

    const first = Promise.all([resolver.resolve('a'), resolver.resolve('b')])
    await tick()
    const again = resolver.resolve('a')
    const fresh = resolver.resolve('c')
    await tick()

    expect(post).toHaveBeenCalledTimes(2)
    expect(requests.map((r) => r.ids)).toEqual([['a', 'b'], ['c']])

    requests.forEach((r) => r.answer())
    expect(await first).toEqual(['/api/files/a/preview?sig=tok-a', '/api/files/b/preview?sig=tok-b'])
    expect(await again).toBe('/api/files/a/preview?sig=tok-a')
    expect(await fresh).toBe('/api/files/c/preview?sig=tok-c')
  })

  it('clear() discards the tokens of a batch that answers afterwards', async () => {
    // The batch was authorised as the user who just signed out. Writing its
    // tokens into the cache would hand them to whoever signs in next.
    const { client, post, requests } = heldClient()
    const resolver = createFileUrlResolver(client as never)

    const before = resolver.resolve('f1')
    await tick()
    resolver.clear()
    requests[0].answer()

    expect(await before).toBeNull()

    const after = resolver.resolve('f1')
    await tick()
    expect(post).toHaveBeenCalledTimes(2)
    requests[1].answer()
    expect(await after).toBe('/api/files/f1/preview?sig=tok-f1')
  })

  it('a resolve after clear() does not join the batch the previous user opened', async () => {
    const { client, post, requests } = heldClient()
    const resolver = createFileUrlResolver(client as never)

    void resolver.resolve('f1')
    await tick()
    resolver.clear()
    const after = resolver.resolve('f1')
    await tick()

    // A fresh request, under the new session.
    expect(post).toHaveBeenCalledTimes(2)
    requests[0].answer()
    requests[1].answer()
    expect(await after).toBe('/api/files/f1/preview?sig=tok-f1')
  })

  it('clear() settles ids that were still waiting for their batch', async () => {
    const { client, post } = heldClient()
    const resolver = createFileUrlResolver(client as never)

    const waiting = resolver.resolve('f1')
    resolver.clear()
    await tick()

    expect(await waiting).toBeNull()
    expect(post).not.toHaveBeenCalled()
  })
})
