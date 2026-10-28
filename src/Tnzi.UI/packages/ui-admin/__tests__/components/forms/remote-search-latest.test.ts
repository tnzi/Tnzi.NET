import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { nextTick } from 'vue'

/**
 * Remote keyword searches show the answer to the latest keyword.
 *
 * Each search box debounced its own `setTimeout` and wrote its results after an
 * `await`. A slower answer to an earlier keyword ("jo") landing after the
 * answer to the later one ("john") replaced it, so the list no longer matched
 * what was typed. A refused search left an unhandled rejection and nothing on
 * screen, and a pending search still fired after its component was gone.
 */
const messageApi = { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(), loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn() }

vi.mock('@tnzi/ui', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/ui')>()),
  useSafeMessage: () => messageApi,
}))

const TUserSelector = (await import('../../../src/components/forms/TUserSelector.vue')).default
const { useChatStore } = await import('../../../src/stores/useChatStore')
const TNewChatDialog = (await import('../../../src/components/chat/TNewChatDialog.vue')).default
const TMemberPickerDialog = (await import('../../../src/components/chat/TMemberPickerDialog.vue')).default
const TSearchHistoryDialog = (await import('../../../src/components/chat/TSearchHistoryDialog.vue')).default

function deferred<T>() {
  let resolve!: (v: T) => void
  let reject!: (e: unknown) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

const DEBOUNCE = 320
const wait = (ms: number) => new Promise((r) => setTimeout(r, ms))

const selectStub = {
  name: 'Select',
  props: ['value', 'options', 'loading'],
  emits: ['update:value', 'search'],
  template: '<div class="n-select-stub" />',
}
const dialogStub = { template: '<div class="dialog"><slot /><slot name="footer" /></div>' }

describe('TUserSelector (selector factory)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('keeps the answer to the latest keyword when an earlier one answers last', async () => {
    const answers = new Map<string, ReturnType<typeof deferred<Array<{ label: string; value: string }>>>>()
    const fetcher = vi.fn((kw: string) => {
      const d = deferred<Array<{ label: string; value: string }>>()
      answers.set(kw, d)
      return d.promise
    })
    const w = mount(TUserSelector, { props: { value: null, fetcher }, global: { stubs: { Select: selectStub } } })
    await nextTick()
    answers.get('')!.resolve([])
    await flushPromises()

    const select = w.findComponent(selectStub)
    select.vm.$emit('search', 'jo')
    await wait(DEBOUNCE)
    select.vm.$emit('search', 'john')
    await wait(DEBOUNCE)

    answers.get('john')!.resolve([{ label: 'John', value: 'u2' }])
    await flushPromises()
    answers.get('jo')!.resolve([{ label: 'Jo', value: 'u1' }, { label: 'John', value: 'u2' }])
    await flushPromises()

    expect(select.props('options')).toEqual([{ label: 'John', value: 'u2' }])
    expect(select.props('loading')).toBe(false)
  })

  it('reports a refused search instead of leaving an unhandled rejection', async () => {
    const fetcher = vi.fn(async (_kw: string) => { throw new Error('Forbidden') })
    mount(TUserSelector, { props: { value: null, fetcher }, global: { stubs: { Select: selectStub } } })
    await flushPromises()

    expect(messageApi.error).toHaveBeenCalledWith('Forbidden')
  })

  it('drops a pending search when the selector goes away', async () => {
    const fetcher = vi.fn(async (_kw: string) => [])
    const w = mount(TUserSelector, { props: { value: null, fetcher }, global: { stubs: { Select: selectStub } } })
    await flushPromises()

    w.findComponent(selectStub).vm.$emit('search', 'late')
    w.unmount()
    await wait(DEBOUNCE)

    expect(fetcher).not.toHaveBeenCalledWith('late')
  })
})

function chatBridge(searchContacts: (kw: string) => Promise<unknown>, searchMessages?: (id: string, q: { keyword: string }) => Promise<unknown>) {
  return {
    listConversations: vi.fn(async () => []),
    getUnreadCount: vi.fn(async () => 0),
    searchContacts: vi.fn(searchContacts),
    searchMessages: vi.fn(searchMessages ?? (async () => ({ messages: [], hasMore: false }))),
  }
}

describe('chat contact and history searches', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('the new-chat dialog keeps the answer to the latest keyword', async () => {
    const answers = new Map<string, ReturnType<typeof deferred<unknown>>>()
    const store = useChatStore()
    store.init(chatBridge((kw) => {
      const d = deferred<unknown>()
      answers.set(kw, d)
      return d.promise
    }) as never)
    const w = mount(TNewChatDialog, { props: { show: false }, global: { stubs: { TChatDialog: dialogStub } } })
    await w.setProps({ show: true })
    answers.get('')!.resolve([])
    await flushPromises()

    const input = w.find('input')
    await input.setValue('a')
    await wait(DEBOUNCE)
    await input.setValue('ab')
    await wait(DEBOUNCE)

    answers.get('ab')!.resolve([{ userId: 'u2', name: 'Abby' }])
    await flushPromises()
    answers.get('a')!.resolve([{ userId: 'u1', name: 'Al' }, { userId: 'u2', name: 'Abby' }])
    await flushPromises()

    const vm = w.vm as unknown as { contacts: Array<{ userId: string }> }
    expect(vm.contacts.map((c) => c.userId)).toEqual(['u2'])
  })

  it('the new-chat dialog reports a refused contact search', async () => {
    const store = useChatStore()
    store.init(chatBridge(async () => { throw new Error('Chat is not enabled for this user') }) as never)
    const w = mount(TNewChatDialog, { props: { show: false }, global: { stubs: { TChatDialog: dialogStub } } })
    await w.setProps({ show: true })
    await flushPromises()

    expect(messageApi.error).toHaveBeenCalledWith('Chat is not enabled for this user')
  })

  it('the member picker keeps the answer to the latest keyword', async () => {
    const answers = new Map<string, ReturnType<typeof deferred<unknown>>>()
    const store = useChatStore()
    store.init(chatBridge((kw) => {
      const d = deferred<unknown>()
      answers.set(kw, d)
      return d.promise
    }) as never)
    const w = mount(TMemberPickerDialog, {
      props: { show: false, title: 'Add', confirmLabel: 'Add' },
      global: { stubs: { TChatDialog: dialogStub } },
    })
    const vm = w.vm as unknown as { load: (kw: string) => Promise<void>; candidates: Array<{ userId: string }> }

    const first = vm.load('a')
    const second = vm.load('ab')
    answers.get('ab')!.resolve([{ userId: 'u2', name: 'Abby' }])
    await flushPromises()
    answers.get('a')!.resolve([{ userId: 'u1', name: 'Al' }])
    await Promise.all([first, second])
    await flushPromises()

    expect(vm.candidates.map((c) => c.userId)).toEqual(['u2'])
  })

  it('the history search keeps the answer to the latest keyword', async () => {
    const answers = new Map<string, ReturnType<typeof deferred<unknown>>>()
    const store = useChatStore()
    store.init(chatBridge(async () => [], (_id, q) => {
      const d = deferred<unknown>()
      answers.set(q.keyword, d)
      return d.promise
    }) as never)
    const w = mount(TSearchHistoryDialog, {
      props: { show: true, conversationId: 'c1' },
      global: { stubs: { TChatDialog: dialogStub } },
    })

    const input = w.find('input')
    await input.setValue('in')
    await input.trigger('input')
    await wait(DEBOUNCE)
    await input.setValue('invoice')
    await input.trigger('input')
    await wait(DEBOUNCE)

    answers.get('invoice')!.resolve({ messages: [{ id: 'm2', content: 'invoice sent' }], hasMore: false })
    await flushPromises()
    answers.get('in')!.resolve({ messages: [{ id: 'm1', content: 'in a minute' }], hasMore: false })
    await flushPromises()

    const vm = w.vm as unknown as { results: Array<{ id: string }> }
    expect(vm.results.map((m) => m.id)).toEqual(['m2'])
  })
})
