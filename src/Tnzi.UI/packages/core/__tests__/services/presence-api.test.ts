import { describe, expect, it, vi } from 'vitest'
import { PRESENCE_BATCH_SIZE, usePresenceApi } from '../../src/services/presence/api'
import { UserPresenceStatus, type UserPresenceDto } from '../../src/services/presence/types'

function ids(count: number): string[] {
  return Array.from({ length: count }, (_, i) => `00000000-0000-0000-0000-${String(i).padStart(12, '0')}`)
}

function mockClient(fail?: (userIds: string[]) => boolean) {
  return {
    get: vi.fn(async (_url: string, options?: { params?: { userIds?: string[] } }) => {
      const userIds = options?.params?.userIds ?? []
      if (fail?.(userIds)) return { succeeded: false, code: 500, message: 'boom' }
      const data: UserPresenceDto[] = userIds.map((userId) => ({ userId, status: UserPresenceStatus.Online }))
      return { succeeded: true, code: 200, data }
    }),
  }
}

/**
 * Every id becomes `userIds=<guid>&` in the query string (~45 bytes). A chat
 * host passes all of its peers at once, and IIS rejects a query string over
 * 2048 bytes by default (Kestrel's request line tops out at 8 KB), so an agent
 * with 60 conversations got no presence at all. Batching keeps each URL small.
 */
describe('usePresenceApi().getPresence', () => {
  it('keeps every request under the IIS default query-string limit', async () => {
    const c = mockClient()
    await usePresenceApi(c as never).getPresence(ids(250))

    for (const [, options] of c.get.mock.calls) {
      const query = (options?.params?.userIds ?? []).map((id: string) => `userIds=${id}`).join('&')
      expect(query.length).toBeLessThan(2048)
    }
  })

  it('splits a large id list into batches and merges the results in order', async () => {
    const c = mockClient()
    const all = ids(PRESENCE_BATCH_SIZE * 2 + 5)
    const result = await usePresenceApi(c as never).getPresence(all)

    expect(c.get).toHaveBeenCalledTimes(3)
    expect(result.succeeded).toBe(true)
    expect(result.data?.map((p) => p.userId)).toEqual(all)
  })

  it('sends a small list as one request, with duplicates collapsed', async () => {
    const c = mockClient()
    const [a, b] = ids(2)
    await usePresenceApi(c as never).getPresence([a!, b!, a!])

    expect(c.get).toHaveBeenCalledTimes(1)
    expect(c.get).toHaveBeenCalledWith('/presence', { params: { userIds: [a, b] } })
  })

  it('fails as a whole when any batch fails', async () => {
    const all = ids(PRESENCE_BATCH_SIZE + 1)
    const c = mockClient((userIds) => userIds.includes(all[PRESENCE_BATCH_SIZE]!))
    const result = await usePresenceApi(c as never).getPresence(all)

    expect(result.succeeded).toBe(false)
    expect(result.message).toBe('boom')
  })
})
