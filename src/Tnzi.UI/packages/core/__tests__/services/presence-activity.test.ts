// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPresenceActivityReporter, type PresenceActivityReporter } from '../../src/services/presence/activity'

const MINUTE = 60_000
const LAST_ACTIVITY_KEY = 'tnzi:presence:last-activity'
const IDLE_REPORTED_KEY = 'tnzi:presence:idle-reported'

function mockClient() {
  return { post: vi.fn(async () => ({ succeeded: true, code: 200 })) }
}

function reports(client: ReturnType<typeof mockClient>): boolean[] {
  return client.post.mock.calls.map((call) => (call as unknown as [string, { active: boolean }])[1].active)
}

function activity(): void {
  window.dispatchEvent(new Event('keydown'))
}

/**
 * The server keeps ONE auto-away flag per user, while every tab runs its own
 * reporter. A tab left in the background used to report idle after its own
 * timer ran out and flip a user who was busy in another tab to Away, and the
 * busy tab never said otherwise because it had never been idle itself.
 */
describe('createPresenceActivityReporter', () => {
  let reporter: PresenceActivityReporter | null = null

  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-10-11T10:00:00Z'))
    window.localStorage.clear()
  })

  afterEach(() => {
    reporter?.stop()
    reporter = null
    vi.useRealTimers()
  })

  function start(client: ReturnType<typeof mockClient>, minutes = 15) {
    reporter = createPresenceActivityReporter({ client: client as never, getIdleMinutes: () => minutes })
    reporter.start()
  }

  it('does not report idle while another tab of the same origin is active', () => {
    const client = mockClient()
    start(client)

    // Another tab keeps seeing activity; this one sees none.
    for (let i = 0; i < 4; i++) {
      vi.advanceTimersByTime(10 * MINUTE)
      window.localStorage.setItem(LAST_ACTIVITY_KEY, String(Date.now()))
    }

    expect(reports(client)).not.toContain(false)
  })

  it('reports idle once every tab has been quiet for the threshold', () => {
    const client = mockClient()
    start(client)

    vi.advanceTimersByTime(5 * MINUTE)
    window.localStorage.setItem(LAST_ACTIVITY_KEY, String(Date.now()))
    vi.advanceTimersByTime(15 * MINUTE)

    expect(reports(client)).toEqual([false])
    expect(window.localStorage.getItem(IDLE_REPORTED_KEY)).not.toBeNull()
  })

  it('reports active when this tab is used after ANOTHER tab reported idle', () => {
    const client = mockClient()
    start(client)
    activity() // first activity of the tab: announces itself
    client.post.mockClear()

    // Another tab reported the user idle; this tab never went idle itself.
    vi.advanceTimersByTime(2 * MINUTE)
    window.localStorage.setItem(IDLE_REPORTED_KEY, String(Date.now()))
    activity()

    expect(reports(client)).toEqual([true])
    expect(window.localStorage.getItem(IDLE_REPORTED_KEY)).toBeNull()
  })

  it('tells the server the user is active on the first activity after start', () => {
    // A flag latched by a tab that was closed while idle, or by another device,
    // would otherwise stay until this tab itself went idle and came back.
    const client = mockClient()
    start(client)

    activity()
    vi.advanceTimersByTime(2000)
    activity()

    expect(reports(client)).toEqual([true])
  })

  it('does not report idle twice when another tab already did', () => {
    const client = mockClient()
    start(client)
    window.localStorage.setItem(IDLE_REPORTED_KEY, String(Date.now()))

    vi.advanceTimersByTime(16 * MINUTE)

    expect(reports(client)).toEqual([])
  })

  it('still works when web storage is unavailable', () => {
    const client = mockClient()
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    try {
      start(client)
      activity()
      vi.advanceTimersByTime(16 * MINUTE)
      activity()
      expect(reports(client)).toEqual([true, false, true])
    } finally {
      getItem.mockRestore()
      setItem.mockRestore()
    }
  })
})
