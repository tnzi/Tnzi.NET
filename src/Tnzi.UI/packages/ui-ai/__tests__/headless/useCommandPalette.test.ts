import { describe, it, expect, vi, afterEach } from 'vitest'
import { ref } from 'vue'
import { useCommandPalette, type CommandAction } from '../../src/headless/useCommandPalette'

const makeActions = (): CommandAction[] => [
  { id: 'new-chat', label: 'New Chat', category: 'chat', keywords: ['create', 'start'], run: vi.fn() },
  { id: 'open-settings', label: 'Open Settings', category: 'app', keywords: ['preferences'], run: vi.fn() },
  { id: 'toggle-theme', label: 'Toggle Theme', category: 'app', keywords: ['dark', 'light'], run: vi.fn() },
  { id: 'switch-scenario', label: 'Switch Scenario', category: 'scenario', run: vi.fn() },
]

describe('useCommandPalette', () => {
  afterEach(() => vi.restoreAllMocks())

  it('starts closed', () => {
    const actions = ref(makeActions())
    const { open } = useCommandPalette({ actions })
    expect(open.value).toBe(false)
  })

  it('opens and closes via methods', () => {
    const actions = ref(makeActions())
    const { open, show, hide } = useCommandPalette({ actions })
    show()
    expect(open.value).toBe(true)
    hide()
    expect(open.value).toBe(false)
  })

  it('toggle flips open state', () => {
    const actions = ref(makeActions())
    const { open, toggle } = useCommandPalette({ actions })
    toggle()
    expect(open.value).toBe(true)
    toggle()
    expect(open.value).toBe(false)
  })

  it('empty query returns all actions', () => {
    const actions = ref(makeActions())
    const { results } = useCommandPalette({ actions })
    expect(results.value.length).toBe(4)
  })

  it('filters by label substring case-insensitively', () => {
    const actions = ref(makeActions())
    const { query, results } = useCommandPalette({ actions })
    query.value = 'CHAT'
    expect(results.value.length).toBe(1)
    expect(results.value[0]?.id).toBe('new-chat')
  })

  it('matches against keywords', () => {
    const actions = ref(makeActions())
    const { query, results } = useCommandPalette({ actions })
    query.value = 'preferences'
    expect(results.value.length).toBe(1)
    expect(results.value[0]?.id).toBe('open-settings')
  })

  it('matches against category', () => {
    const actions = ref(makeActions())
    const { query, results } = useCommandPalette({ actions })
    query.value = 'scenario'
    expect(results.value.some((a) => a.id === 'switch-scenario')).toBe(true)
  })

  it('truncates results to maxResults', () => {
    const many: CommandAction[] = Array.from({ length: 100 }, (_, i) => ({
      id: `action-${i}`,
      label: `Action ${i}`,
      run: vi.fn(),
    }))
    const actions = ref(many)
    const { results } = useCommandPalette({ actions, maxResults: 10 })
    expect(results.value.length).toBe(10)
  })

  it('highlightedIndex starts at 0 and clamps within results', () => {
    const actions = ref(makeActions())
    const { highlightedIndex, moveDown, moveUp } = useCommandPalette({ actions })
    expect(highlightedIndex.value).toBe(0)
    moveDown()
    expect(highlightedIndex.value).toBe(1)
    moveDown()
    moveDown()
    moveDown()
    expect(highlightedIndex.value).toBe(3)
    moveUp()
    expect(highlightedIndex.value).toBe(2)
  })

  it('query change resets highlightedIndex to 0', () => {
    const actions = ref(makeActions())
    const { query, highlightedIndex, moveDown } = useCommandPalette({ actions })
    moveDown()
    moveDown()
    expect(highlightedIndex.value).toBe(2)
    query.value = 'theme'
    expect(highlightedIndex.value).toBe(0)
  })

  it('activate runs the highlighted action and closes palette', async () => {
    const actions = ref(makeActions())
    const runSpy = actions.value[0]!.run as ReturnType<typeof vi.fn>
    const { open, show, activate } = useCommandPalette({ actions })
    show()
    await activate()
    expect(runSpy).toHaveBeenCalledOnce()
    expect(open.value).toBe(false)
  })

  it('activate on empty results is a no-op', async () => {
    const actions = ref(makeActions())
    const { query, show, activate, open } = useCommandPalette({ actions })
    show()
    query.value = 'zzzzz' // no matches
    await activate()
    expect(open.value).toBe(true)
  })

  it('opening the palette resets query and index', () => {
    const actions = ref(makeActions())
    const { query, highlightedIndex, show, hide, moveDown } = useCommandPalette({ actions })
    show()
    query.value = 'theme'
    moveDown()
    hide()
    show()
    expect(query.value).toBe('')
    expect(highlightedIndex.value).toBe(0)
  })
})
