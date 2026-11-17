/**
 * `TSearchSelect` mounted: the field opens a list with the search box focused, typing filters
 * (local) or loads (remote, with loading, empty and failure rows), the keyboard walks and picks,
 * Escape stays inside, scrolling to the end fetches the next page, and the chosen record's label
 * shows though it is not listed.
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'
import type { SearchSelectLoadContext, SearchSelectPage, TSearchSelectOption } from '../../src/components/controls/search-select'

const { TSearchSelect } = await import('../../src/components/controls/index')

let app: App | null = null
let host: HTMLElement | null = null

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
  document.body.innerHTML = ''
  vi.useRealTimers()
})

const flush = async () => {
  for (let i = 0; i < 4; i++) {
    await Promise.resolve()
    await nextTick()
  }
}

function key(target: Element, name: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...init })
  target.dispatchEvent(event)
  return event
}

function type(input: HTMLInputElement, value: string): void {
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

const PEOPLE: TSearchSelectOption<number>[] = [
  { value: 1, label: 'Ada Lovelace', description: 'ada@example.com' },
  { value: 2, label: 'Alan Turing', disabled: true },
  { value: 3, label: 'Grace Hopper' },
]

function mount(props: Record<string, unknown>) {
  const value = ref<number | null>((props.modelValue as number | null | undefined) ?? null)
  const picked: Array<TSearchSelectOption<number> | null> = []
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({
    render: () =>
      h(TSearchSelect as never, {
        ...props,
        modelValue: value.value,
        placeholder: 'Pick a person',
        'onUpdate:modelValue': (next: number | null) => (value.value = next),
        onSelect: (option: TSearchSelectOption<number> | null) => picked.push(option),
      }),
  })
  app.mount(host)
  const trigger = host.querySelector('.t-search-select__trigger') as HTMLButtonElement
  return {
    value,
    picked,
    trigger,
    input: () => document.querySelector('.t-search-select__input') as HTMLInputElement | null,
    options: () => [...document.querySelectorAll<HTMLElement>('[role="option"]')],
    status: () => document.querySelector('.t-search-select__status')?.textContent?.trim() ?? null,
  }
}

describe('TSearchSelect with its own options', () => {
  it('opens with the search box focused, filters as one types, and picks with the keyboard', async () => {
    const m = mount({ options: PEOPLE })
    expect(m.trigger.textContent?.trim()).toBe('Pick a person')
    key(m.trigger, 'ArrowDown')
    await flush()
    const input = m.input()!
    // Focus moves once the menu is placed (it is hidden until then); a key before that lands in the box.
    key(m.trigger, 'g')
    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
    expect(document.activeElement).toBe(input)
    expect(input.value).toBe('g')
    expect(input.getAttribute('role')).toBe('combobox')
    expect(m.options()).toHaveLength(2) // Alan Turing, Grace Hopper

    type(input, 'gr')
    await flush()
    expect(m.options().map((o) => o.textContent?.trim())).toEqual(['Grace Hopper'])
    expect(input.getAttribute('aria-activedescendant')).toBe(m.options()[0]!.id)
    key(input, 'Enter')
    await flush()
    expect(m.value.value).toBe(3)
    expect(m.picked.map((o) => o?.value)).toEqual([3])
    expect(m.input()).toBeNull()
    expect(document.activeElement).toBe(m.trigger)
    expect(m.trigger.textContent?.trim()).toBe('Grace Hopper')
  })

  it('skips disabled options, says when nothing matches, and keeps Escape to itself', async () => {
    const m = mount({ options: PEOPLE })
    m.trigger.click()
    await flush()
    const input = m.input()!
    key(input, 'ArrowDown')
    await flush()
    expect(input.getAttribute('aria-activedescendant')).toBe(m.options()[2]!.id)

    type(input, 'nobody')
    await flush()
    expect(m.options()).toHaveLength(0)
    expect(m.status()).toBe('No matches')

    let reachedDocument = false
    const listener = (event: KeyboardEvent) => (reachedDocument = event.key === 'Escape')
    document.addEventListener('keydown', listener)
    const escape = key(input, 'Escape')
    document.removeEventListener('keydown', listener)
    await flush()
    expect(escape.defaultPrevented).toBe(true)
    expect(reachedDocument).toBe(false)
    expect(m.input()).toBeNull()
  })

  it('opens on a typed letter with it in the search box, and leaves IME keys alone', async () => {
    const m = mount({ options: PEOPLE })
    key(m.trigger, 't', { isComposing: true })
    await flush()
    expect(m.input()).toBeNull()
    key(m.trigger, 't')
    await flush()
    expect(m.input()!.value).toBe('t')
    expect(m.options().map((o) => o.dataset.value)).toEqual(['2'])
    key(m.input()!, 'Enter', { isComposing: true })
    await flush()
    expect(m.value.value).toBeNull()
  })

  it('clears to null with its clear button', async () => {
    const m = mount({ options: PEOPLE, modelValue: 1, clearable: true })
    expect(m.trigger.textContent?.trim()).toBe('Ada Lovelace')
    ;(host!.querySelector('.t-search-select__clear') as HTMLButtonElement).click()
    await flush()
    expect(m.value.value).toBeNull()
    expect(m.picked).toEqual([null])
    expect(host!.querySelector('.t-search-select__clear')).toBeNull()
  })
})

describe('TSearchSelect with a loader', () => {
  type Call = { query: string; context: SearchSelectLoadContext; resolve: (page: SearchSelectPage<number>) => void; reject: (e: unknown) => void }

  function loader() {
    const calls: Call[] = []
    const load = (query: string, context: SearchSelectLoadContext) =>
      new Promise<SearchSelectPage<number>>((resolve, reject) => calls.push({ query, context, resolve, reject }))
    return { calls, load }
  }

  const rows = (from: number, count: number) =>
    Array.from({ length: count }, (_, i) => ({ value: from + i, label: `Client ${from + i}` }))

  it("shows the chosen record's label before anything is loaded", () => {
    const { load } = loader()
    const m = mount({ load, modelValue: 812, selectedLabel: 'Client 812' })
    expect(m.trigger.textContent?.trim()).toBe('Client 812')
    expect(m.trigger.dataset.value).toBe('812')
  })

  it('loads on open with a loading row, searches after the pause, and picks from the answer', async () => {
    vi.useFakeTimers()
    const { calls, load } = loader()
    const m = mount({ load, debounce: 100 })
    m.trigger.click()
    await flush()
    expect(calls).toHaveLength(1)
    expect(calls[0]!.query).toBe('')
    expect(m.status()).toBe('Loading...')
    expect(m.input()!.getAttribute('aria-busy')).toBe('true')
    calls[0]!.resolve({ options: rows(1, 3), hasMore: false })
    await flush()
    expect(m.options()).toHaveLength(3)

    type(m.input()!, 'client 2')
    await flush()
    expect(m.status()).toBe('Loading...')
    await vi.advanceTimersByTimeAsync(100)
    expect(calls[1]!.query).toBe('client 2')
    expect(calls[0]!.context.signal.aborted).toBe(false)
    calls[1]!.resolve({ options: [{ value: 2, label: 'Client 2' }], hasMore: false })
    await flush()
    m.options()[0]!.click()
    await flush()
    expect(m.value.value).toBe(2)
    // Picked from a list that is gone now: the label stays.
    expect(m.trigger.textContent?.trim()).toBe('Client 2')
  })

  it('fetches the next page at the end of the list, by scrolling and by the keyboard', async () => {
    const { calls, load } = loader()
    const m = mount({ load })
    m.trigger.click()
    await flush()
    calls[0]!.resolve({ options: rows(1, 2), hasMore: true })
    await flush()
    const list = document.querySelector('.t-search-select__list') as HTMLElement
    list.dispatchEvent(new Event('scroll'))
    await flush()
    expect(calls).toHaveLength(2)
    expect(calls[1]!.context.offset).toBe(2)
    calls[1]!.resolve({ options: rows(3, 2), hasMore: true })
    await flush()
    expect(m.options()).toHaveLength(4)

    const input = m.input()!
    for (let i = 0; i < 4; i++) key(input, 'ArrowDown')
    await flush()
    expect(calls).toHaveLength(3)
    expect(calls[2]!.context.offset).toBe(4)
  })

  it('shows a failure with Retry, which loads again', async () => {
    const { calls, load } = loader()
    const m = mount({ load })
    m.trigger.click()
    await flush()
    calls[0]!.reject(new Error('offline'))
    await flush()
    expect(document.querySelector('.t-search-select__status.is-error')?.getAttribute('role')).toBe('alert')
    ;(document.querySelector('.t-search-select__retry') as HTMLButtonElement).click()
    await flush()
    expect(calls).toHaveLength(2)
    calls[1]!.resolve({ options: [], hasMore: false })
    await flush()
    expect(m.status()).toBe('No matches')
  })

  it('aborts what is in flight when the list closes', async () => {
    const { calls, load } = loader()
    const m = mount({ load })
    m.trigger.click()
    await flush()
    key(m.input()!, 'Escape')
    await flush()
    expect(calls[0]!.context.signal.aborted).toBe(true)
  })
})
