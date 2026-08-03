import { describe, it, expect, vi, beforeEach } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { mount } from '@vue/test-utils'

const resolve = vi.fn(async (id: string, kind?: string) => `/api/files/${id}/${kind ?? 'preview'}?sig=tok-${id}`)
const resolveMany = vi.fn(
  async (ids: string[]) => new Map(ids.map((id) => [id, `/api/files/${id}/preview?sig=tok-${id}`])),
)
const plain = vi.fn((id: string, kind?: string) => `/api/files/${id}/${kind ?? 'preview'}`)

const client = { id: 'client' }

vi.mock('../../src/plugin/client', () => ({
  useAdminClient: () => client,
}))
vi.mock('../../src/services/file-url-resolver', () => ({
  getFileUrlResolver: () => ({ resolve, resolveMany, plain, clear: vi.fn() }),
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
