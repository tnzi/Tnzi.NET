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
    const { client, post } = mockClient((ids) =>
      ids.map((id) => ({ fileId: id, token: `tok-${id}`, expiresAt: new Date(Date.now() + 30_000).toISOString() })),
    )
    const resolver = createFileUrlResolver(client as never, { refreshMarginMs: 60_000 })

    await settle(resolver.resolve('f1'))
    await settle(resolver.resolve('f1'))

    expect(post).toHaveBeenCalledTimes(2)
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
