/**
 * The attention indicators: a conversation's running / unread state at the end of
 * its row (sidebar, rail flyout, Recents page), a `TSegmented` option's `running`
 * ring (alone and around a dot), and the toned nav badge in the expanded nav and
 * on the collapsed rail.
 *
 * Colours, sizes and the animation itself are not visible to happy-dom; this pins
 * what is rendered, what a screen reader is given, and the stylesheet rules that
 * swap the state for the "..." and still the motion.
 */
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, h, nextTick, ref, shallowRef, type App } from 'vue'
import type { ThreadItem } from '../../src/components/chat/TThreadList.vue'
import type { RecentThreadsController } from '../../src/headless/recents'
import { DEFAULT_RECENTS_PREFERENCES } from '../../src/headless/recents'

const { default: TThreadList } = await import('../../src/components/chat/TThreadList.vue')
const { default: TRailThreadList } = await import('../../src/components/chat/TRailThreadList.vue')
const { default: TRecentsPage } = await import('../../src/components/chat/TRecentsPage.vue')
const { default: TSegmented } = await import('../../src/components/layout/TSegmented.vue')
const { default: TSidebarNav } = await import('../../src/components/layout/TSidebarNav.vue')
const { default: TChatRail } = await import('../../src/components/layout/TChatRail.vue')

const threads: ThreadItem[] = [
  { id: 'run', title: 'Deploying', origin: 'Email', running: true, unread: true },
  { id: 'new', title: 'Digest ready', origin: 'Schedule', unread: true },
  { id: 'calm', title: 'Plain chat', origin: 'Email' },
]
const threadIcon = (thread: ThreadItem) => (thread.origin ? { icon: 'lucide:mail', label: `From ${thread.origin}` } : null)

let app: App | null = null
let host: HTMLElement | null = null

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({ render })
  app.mount(host)
  return host
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve()
    await nextTick()
  }
}

/** The rules of a stylesheet whose selector list contains `part`, as "selector { body }". */
function rulesWith(path: string, part: string): string[] {
  const sfc = readFileSync(resolve(__dirname, '../../src/components', path), 'utf8')
  const style = (/<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '')
  return Array.from(style.matchAll(/([^{}]+)\{([^{}]*)\}/g))
    .filter((m) => m[1]!.includes(part))
    .map((m) => `${m[1]!.trim().replace(/\s+/g, ' ')} { ${m[2]!.trim().replace(/\s+/g, ' ')} }`)
}

/** The words of an element, one per text node, as a screen reader runs them together with pauses. */
function words(el: Element): string {
  const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT)
  const parts: string[] = []
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = node.textContent?.trim()
    if (text) parts.push(text)
  }
  return parts.join(' ')
}

const statusOf = (scope: Element | null) => scope?.querySelector('[data-thread-status]')?.getAttribute('data-thread-status') ?? null

beforeEach(() => localStorage.clear())

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
  document.body.innerHTML = ''
})

describe('thread rows', () => {
  it("the sidebar shows running before unread, in place of the origin icon at the row's end, and says it in words", async () => {
    const root = mount(() => h(TThreadList, { threads, threadIcon }))
    await settle()
    const rows = Array.from(root.querySelectorAll('.t-thread-list__row'))

    const ends = rows.map((row) => row.querySelector('.t-thread-list__end')!)
    expect(ends.map(statusOf)).toEqual(['running', 'unread', null])
    expect(ends.map((end) => !!end.querySelector('.t-thread-icon'))).toEqual([false, false, true])
    // The state is decoration; the words are in the row button's name, after the title
    for (const end of ends) end.querySelectorAll('[data-thread-status]').forEach((s) => expect(s.getAttribute('aria-hidden')).toBe('true'))
    const names = rows.map((row) => words(row.querySelector('.t-thread-list__select')!))
    expect(names).toEqual(['From Email Deploying Working', 'From Schedule Digest ready Unread', 'From Email Plain chat'])
    // It gives way to the "..." as the origin icon does
    expect(ends[0]!.querySelector('[data-thread-status]')!.classList.contains('t-thread-list__icon')).toBe(true)
  })

  it('a touch screen, where the "..." always shows, gets the state just before it', async () => {
    const root = mount(() => h(TThreadList, { threads, threadIcon }))
    await settle()
    const row = root.querySelector('.t-thread-list__row')!
    const touch = row.querySelector('.t-thread-list__touch-status')!
    expect(touch.nextElementSibling!.classList.contains('t-thread-list__end')).toBe(true)
    expect(rulesWith('chat/TThreadList.vue', '.t-thread-list__touch-status')).toEqual([
      '.t-thread-list__touch-status { display: none; }',
      '.t-thread-list__touch-status { display: block; margin-right: -6px; }',
    ])
  })

  it('the rail flyout shows the state where the origin icon would be', async () => {
    const root = mount(() => h(TRailThreadList, { threads, threadIcon }))
    await settle()
    const rows = Array.from(root.querySelectorAll('.t-rail-thread-list__row'))
    expect(rows.map(statusOf)).toEqual(['running', 'unread', null])
    expect(rows.map((row) => !!row.querySelector('.t-thread-icon'))).toEqual([false, false, true])
    expect(words(rows[1]!)).toBe('From Schedule Digest ready Unread')
  })

  it('the Recents page shows it in the cell it shares with the "..."', async () => {
    const controller: RecentThreadsController = {
      preferences: ref(DEFAULT_RECENTS_PREFERENCES),
      setPreferences: vi.fn(async () => {}),
      rename: vi.fn(async () => true),
      archive: vi.fn(async () => true),
      unarchive: vi.fn(async () => true),
      page: {
        threads: shallowRef(threads),
        status: ref<'active' | 'archived'>('active'),
        keyword: ref(''),
        hasMore: ref(false),
        loading: ref(false),
        loadingMore: ref(false),
        loaded: ref(true),
        load: vi.fn(async () => {}),
        loadMore: vi.fn(async () => {}),
        setStatus: vi.fn(async () => {}),
        search: vi.fn(async () => {}),
      },
    }
    const root = mount(() => h(TRecentsPage, { controller, threadIcon }))
    await settle()
    const ends = Array.from(root.querySelectorAll('.t-recents-page__end'))
    expect(ends.map(statusOf)).toEqual(['running', 'unread', null])
    expect(ends[2]!.querySelector('.t-thread-icon')).not.toBeNull()
  })

  it('the running ring stands still under reduced motion', () => {
    const sfc = readFileSync(resolve(__dirname, '../../src/components/chat/TThreadStatus.vue'), 'utf8')
    expect(sfc).toMatch(/@media \(prefers-reduced-motion: reduce\) \{\s*\.t-thread-status--running \{ animation: none; \}/)
  })
})

describe('TSegmented running', () => {
  const options = [
    { value: 'chat', label: 'Chat', icon: 'lucide:message-square' },
    { value: 'code', label: 'Code', icon: 'lucide:code', running: true },
    { value: 'mail', label: 'Mail', icon: 'lucide:mail', running: true, runningLabel: 'Syncing', dot: 'danger' as const, dotLabel: 'Needs you' },
  ]

  it('draws a ring of its own, or around the dot, and names it on an icon-only option', () => {
    const root = mount(() => h(TSegmented, { modelValue: 'chat', options, label: 'Mode', iconOnly: true }))
    const [chat, code, mail] = Array.from(root.querySelectorAll('[role="radio"]'))

    expect(chat!.querySelector('[data-segment-running]')).toBeNull()
    expect(chat!.getAttribute('aria-label')).toBe('Chat')

    const ring = code!.querySelector('[data-segment-running]')!
    expect(ring.classList.contains('is-around-dot')).toBe(false)
    expect(code!.getAttribute('aria-label')).toBe('Code, Working')

    expect(mail!.querySelector('[data-segment-dot]')!.classList.contains('is-running')).toBe(true)
    expect(mail!.querySelector('[data-segment-running]')!.classList.contains('is-around-dot')).toBe(true)
    expect(mail!.getAttribute('aria-label')).toBe('Mail, Needs you, Syncing')
  })

  it('says it in words after the label when labels show', () => {
    const root = mount(() => h(TSegmented, { modelValue: 'chat', options, label: 'Mode' }))
    const code = root.querySelectorAll('[role="radio"]')[1]!
    expect(code.hasAttribute('aria-label')).toBe(false)
    expect(code.querySelector('[data-segment-running] .sr-only')!.textContent).toBe('Working')
  })

  it('stops turning under reduced motion', () => {
    expect(rulesWith('layout/TSegmented.vue', '.t-segmented__dot.is-running::after').some((r) => r.includes('animation: none'))).toBe(true)
    expect(rulesWith('layout/TSegmented.vue', '.t-segmented__running,').some((r) => r.includes('animation: none'))).toBe(true)
  })
})

describe('nav badge tones', () => {
  const groups = [
    {
      id: 'main',
      items: [
        { id: 'inbox', label: 'Inbox', icon: 'lucide:inbox', badge: 3, badgeTone: 'danger' as const },
        { id: 'tasks', label: 'Tasks', icon: 'lucide:list', badge: 5, badgeTone: 'warning' as const },
        { id: 'notes', label: 'Notes', icon: 'lucide:notebook', badge: 12 },
      ],
    },
  ]

  it('fills the badge red or amber by its tone and leaves an untoned one grey', () => {
    const root = mount(() => h(TSidebarNav, { groups }))
    const badges = Array.from(root.querySelectorAll('.t-sidebar-nav__item-badge'))
    expect(badges.map((b) => b.getAttribute('data-badge-tone'))).toEqual(['danger', 'warning', null])
    expect(badges[0]!.classList.contains('t-sidebar-nav__item-badge--danger')).toBe(true)
    expect(badges[1]!.classList.contains('t-sidebar-nav__item-badge--warning')).toBe(true)
    expect(badges[2]!.className).toBe('t-sidebar-nav__item-badge')
    expect(rulesWith('layout/TSidebarNav.vue', '.t-sidebar-nav__item-badge--danger')).toContain(
      '.t-sidebar-nav__item-badge--danger { background: var(--tnzi-ai-badge-danger); color: var(--tnzi-ai-on-badge-danger); }',
    )
  })

  it('shows a toned badge on the collapsed rail too, read after the label, and no untoned one', () => {
    const root = mount(() => h(TChatRail, { groups }))
    const buttons = Array.from(root.querySelectorAll('.t-chat-rail__btn'))
    expect(buttons.map((b) => b.querySelector('[data-badge-tone]')?.getAttribute('data-badge-tone') ?? null)).toEqual([
      'danger',
      'warning',
      null,
    ])
    expect(buttons.map((b) => b.getAttribute('aria-label'))).toEqual(['Inbox, 3', 'Tasks, 5', 'Notes'])
  })

  it('declares the badge colours once for both modes, with ink that reads on them', () => {
    const tokens = readFileSync(resolve(__dirname, '../../src/styles/tokens.css'), 'utf8')
    expect(tokens.match(/--tnzi-ai-badge-danger:/g)).toHaveLength(1)
    expect(tokens).toMatch(/--tnzi-ai-on-badge-warning:\s*#1a1a1a/)
  })
})
