import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { mount } from '@vue/test-utils'

const resolve = vi.fn(async (id: string, kind?: string) => `/api/files/${id}/${kind ?? 'preview'}?sig=tok-${id}`)
const resolveMany = vi.fn(
  async (ids: string[]) => new Map(ids.map((id) => [id, `/api/files/${id}/preview?sig=tok-${id}`])),
)
const plain = vi.fn((id: string, kind?: string) => `/api/files/${id}/${kind ?? 'preview'}`)
/** When the resolver says the cached tokens stop being served. `null` by default: no re-sign. */
const refreshAt = vi.fn((_ids: readonly string[]): number | null => null)
const invalidate = vi.fn((_ids: readonly string[]) => undefined)

const client = { id: 'client' }

vi.mock('../../src/plugin/client', () => ({
  useAdminClient: () => client,
}))
vi.mock('../../src/services/file-url-resolver', () => ({
  getFileUrlResolver: () => ({ resolve, resolveMany, plain, clear: vi.fn(), refreshAt, invalidate }),
}))

const { useFileUrl, useFileUrls } = await import('../../src/headless/useFileUrl')

/** Mount a component that just exposes what the composable returned. */
function mountWith<T>(setup: () => T) {
  let exposed!: T
  const Comp = defineComponent({
    setup() {
      exposed = setup()
      return () => h('div')
    },
  })
  const wrapper = mount(Comp)
  return { wrapper, get: () => exposed }
}

describe('useFileUrl', () => {
  beforeEach(() => {
    resolve.mockClear()
    resolveMany.mockClear()
    plain.mockClear()
  })

  it('resolves a signed URL for a private file', async () => {
    const { get } = mountWith(() => useFileUrl(() => 'f1'))

    await nextTick()
    await nextTick()

    expect(get().url.value).toBe('/api/files/f1/preview?sig=tok-f1')
  })

  it('honours the requested kind', async () => {
    mountWith(() => useFileUrl(() => 'f1', { kind: 'download' }))

    await nextTick()

    expect(resolve).toHaveBeenCalledWith('f1', 'download')
  })

  it('skips the round trip for a file known to be public', async () => {
    // Avatars are public; minting a token per avatar would be a request per row
    // for nothing.
    const { get } = mountWith(() => useFileUrl(() => 'f1', { isPublic: () => true }))

    await nextTick()

    expect(resolve).not.toHaveBeenCalled()
    expect(get().url.value).toBe('/api/files/f1/preview')
  })

  it('resolves to null for an empty id without a request', async () => {
    const { get } = mountWith(() => useFileUrl(() => null))

    await nextTick()

    expect(get().url.value).toBeNull()
    expect(resolve).not.toHaveBeenCalled()
  })

  it('discards a stale answer when the id changed mid-flight', async () => {
    // List rows get recycled. A late answer for the previous row is simply
    // wrong, and showing it means one row briefly renders another row's file.
    const slow = new Map<string, (v: string) => void>()
    resolve.mockImplementation(
      (id: string) => new Promise<string>((r) => slow.set(id, r)) as Promise<string>,
    )

    const id = ref('old')
    const { get } = mountWith(() => useFileUrl(id))
    await nextTick()

    id.value = 'new'
    await nextTick()

    slow.get('new')!('/api/files/new/preview?sig=tok-new')
    await nextTick()
    slow.get('old')!('/api/files/old/preview?sig=tok-old')
    await nextTick()
    await nextTick()

    expect(get().url.value).toBe('/api/files/new/preview?sig=tok-new')
  })
})

describe('useFileUrls', () => {
  beforeEach(() => {
    resolveMany.mockClear()
  })

  it('resolves a list in a single call', async () => {
    const { get } = mountWith(() => useFileUrls(() => ['a', 'b']))

    await nextTick()
    await nextTick()

    expect(resolveMany).toHaveBeenCalledTimes(1)
    expect(get().urls.value.get('a')).toContain('sig=tok-a')
    expect(get().urls.value.get('b')).toContain('sig=tok-b')
  })

  it('does not re-resolve when a re-render hands over an equal list', async () => {
    // Parent re-renders produce a new array identity every time; keying on it
    // would re-mint the same batch on every keystroke elsewhere on the page.
    const tick = ref(0)
    mountWith(() => useFileUrls(() => (tick.value >= 0 ? ['a', 'b'] : [])))

    await nextTick()
    tick.value = 1
    await nextTick()
    await nextTick()

    expect(resolveMany).toHaveBeenCalledTimes(1)
  })

  it('drops the map when the list empties', async () => {
    const ids = ref<string[]>(['a'])
    const { get } = mountWith(() => useFileUrls(ids))
    await nextTick()
    await nextTick()
    expect(get().urls.value.size).toBe(1)

    ids.value = []
    await nextTick()

    expect(get().urls.value.size).toBe(0)
  })
})

describe('re-signing before the tokens expire', () => {
  // A page that stays open longer than the token TTL kept handing out URLs the
  // server answers with 404: a tab pane remounting its images, a filter
  // mounting new cards, a lazy image scrolling into view.
  const NOW = Date.parse('2026-10-21T09:00:00Z')
  /** The resolver reports the tokens stale 9 minutes in (a 10-minute TTL minus its margin). */
  const STALE_AT = NOW + 9 * 60_000
  let minted = 0

  beforeEach(() => {
    vi.useFakeTimers({ now: NOW })
    minted = 0
    resolve.mockReset()
    resolve.mockImplementation(async (id: string) => `/api/files/${id}/preview?sig=tok-${id}-${++minted}`)
    resolveMany.mockReset()
    resolveMany.mockImplementation(async (ids: string[]) => {
      minted += 1
      return new Map(ids.map((id) => [id, `/api/files/${id}/preview?sig=tok-${id}-${minted}`]))
    })
    refreshAt.mockReset()
    refreshAt.mockImplementation(() => STALE_AT)
    invalidate.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('useFileUrls re-resolves the list once its tokens go stale, and not before', async () => {
    const { get } = mountWith(() => useFileUrls(() => ['a', 'b']))
    await vi.advanceTimersByTimeAsync(0)
    expect(get().urls.value.get('a')).toBe('/api/files/a/preview?sig=tok-a-1')

    // Fresh tokens cost nothing: no request until the stale point.
    await vi.advanceTimersByTimeAsync(9 * 60_000 - 1)
    expect(resolveMany).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(1)
    expect(resolveMany).toHaveBeenCalledTimes(2)
    // Still one batch for the whole list.
    expect(resolveMany).toHaveBeenLastCalledWith(['a', 'b'], undefined)
    expect(get().urls.value.get('a')).toBe('/api/files/a/preview?sig=tok-a-2')
    expect(get().urls.value.get('b')).toBe('/api/files/b/preview?sig=tok-b-2')
  })

  it('keeps loading false while it re-signs, so the page does not flash a spinner', async () => {
    const { get } = mountWith(() => useFileUrls(() => ['a']))
    await vi.advanceTimersByTimeAsync(0)

    let finish!: (v: Map<string, string>) => void
    resolveMany.mockImplementationOnce(() => new Promise((r) => (finish = r)))
    await vi.advanceTimersByTimeAsync(9 * 60_000)

    expect(resolveMany).toHaveBeenCalledTimes(2)
    expect(get().loading.value).toBe(false)
    finish(new Map([['a', '/api/files/a/preview?sig=renewed']]))
    await vi.advanceTimersByTimeAsync(0)
    expect(get().urls.value.get('a')).toBe('/api/files/a/preview?sig=renewed')
  })

  it('keeps re-signing for as long as the page stays open', async () => {
    refreshAt.mockImplementation(() => Date.now() + 9 * 60_000)
    mountWith(() => useFileUrls(() => ['a']))
    await vi.advanceTimersByTimeAsync(0)

    await vi.advanceTimersByTimeAsync(3 * 9 * 60_000)

    expect(resolveMany).toHaveBeenCalledTimes(4)
  })

  it('schedules nothing when no token is cached', async () => {
    // Unreadable ids are absent from the cache: there is nothing to renew.
    refreshAt.mockImplementation(() => null)
    mountWith(() => useFileUrls(() => ['a']))
    await vi.advanceTimersByTimeAsync(0)

    await vi.advanceTimersByTimeAsync(60 * 60_000)

    expect(resolveMany).toHaveBeenCalledTimes(1)
  })

  it('stops re-signing once the component unmounts', async () => {
    const { wrapper } = mountWith(() => useFileUrls(() => ['a']))
    await vi.advanceTimersByTimeAsync(0)

    wrapper.unmount()
    await vi.advanceTimersByTimeAsync(60 * 60_000)

    expect(resolveMany).toHaveBeenCalledTimes(1)
  })

  it('schedules nothing for a resolve that answers after the component unmounted', async () => {
    // Otherwise a timer would outlive its view and mint tokens for nobody, forever.
    let finish!: (v: Map<string, string>) => void
    resolveMany.mockImplementationOnce(() => new Promise((r) => (finish = r)))
    const { wrapper } = mountWith(() => useFileUrls(() => ['a']))
    await vi.advanceTimersByTimeAsync(0)

    wrapper.unmount()
    finish(new Map([['a', '/api/files/a/preview?sig=late']]))
    await vi.advanceTimersByTimeAsync(60 * 60_000)

    expect(resolveMany).toHaveBeenCalledTimes(1)
  })

  it('useFileUrls.refresh(id) drops that token and re-resolves the list', async () => {
    // The consumer calls this from an <img> error handler.
    const { get } = mountWith(() => useFileUrls(() => ['a', 'b']))
    await vi.advanceTimersByTimeAsync(0)

    await get().refresh('b')

    expect(invalidate).toHaveBeenCalledWith(['b'])
    expect(resolveMany).toHaveBeenCalledTimes(2)
    expect(get().urls.value.get('b')).toBe('/api/files/b/preview?sig=tok-b-2')
  })

  it('useFileUrls.refresh() without an id re-signs the whole list', async () => {
    const { get } = mountWith(() => useFileUrls(() => ['a', 'b']))
    await vi.advanceTimersByTimeAsync(0)

    await get().refresh()

    expect(invalidate).toHaveBeenCalledWith(['a', 'b'])
  })

  it('keeps the map identity when a re-sign renewed nothing', async () => {
    // The resolver kept the fresh tokens, so the answer is the same URLs; a new
    // Map would re-render every card for nothing.
    resolveMany.mockImplementation(async (ids: string[]) => new Map(ids.map((id) => [id, `/api/files/${id}/preview?sig=same`])))
    const { get } = mountWith(() => useFileUrls(() => ['a']))
    await vi.advanceTimersByTimeAsync(0)
    const before = get().urls.value

    await get().refresh('a')

    expect(get().urls.value).toBe(before)
  })

  it('useFileUrl re-signs before the token expires', async () => {
    const { get } = mountWith(() => useFileUrl(() => 'f1'))
    await vi.advanceTimersByTimeAsync(0)
    expect(get().url.value).toBe('/api/files/f1/preview?sig=tok-f1-1')

    await vi.advanceTimersByTimeAsync(9 * 60_000)

    expect(resolve).toHaveBeenCalledTimes(2)
    expect(get().url.value).toBe('/api/files/f1/preview?sig=tok-f1-2')
    expect(get().loading.value).toBe(false)
  })

  it('useFileUrl.refresh() drops the token and re-resolves', async () => {
    const { get } = mountWith(() => useFileUrl(() => 'f1'))
    await vi.advanceTimersByTimeAsync(0)

    await get().refresh()

    expect(invalidate).toHaveBeenCalledWith(['f1'])
    expect(get().url.value).toBe('/api/files/f1/preview?sig=tok-f1-2')
  })

  it('useFileUrl neither schedules nor re-signs a public file', async () => {
    const { get } = mountWith(() => useFileUrl(() => 'f1', { isPublic: () => true }))
    await vi.advanceTimersByTimeAsync(0)

    await get().refresh()
    await vi.advanceTimersByTimeAsync(60 * 60_000)

    expect(resolve).not.toHaveBeenCalled()
    expect(invalidate).not.toHaveBeenCalled()
  })
})
