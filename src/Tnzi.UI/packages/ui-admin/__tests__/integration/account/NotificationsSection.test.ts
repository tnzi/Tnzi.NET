import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// The User Center's Notifications section, driven through the real bridge and
// the real @tnzi/core API down to a fake HttpClient - so every assertion below
// is on the request that would actually leave the browser.
//
// Self-service only: every call goes to /notification/preferences (the user is
// the session's), never to the /admin variant.

const http = vi.hoisted(() => ({
  get: vi.fn(),
  put: vi.fn(),
  post: vi.fn(),
  delete: vi.fn(),
}))
vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => http }))
vi.mock('../../../src/utils/time-zones', async (importActual) => ({
  ...(await importActual<typeof import('../../../src/utils/time-zones')>()),
  browserTimeZone: () => 'America/Toronto',
}))

const messageApi = {
  success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(),
  loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn(),
}

import NotificationsSection from '../../../src/pages/account/sections/NotificationsSection.vue'
import { createUserCenterState, provideUserCenterContext } from '../../../src/pages/account/user-center-context'
import { makePageTranslator } from '../../../src/pages/_shared/translate'

const ok = <T>(data: T) => ({ success: true, code: 200, data })

async function mountSection(rows: unknown[]) {
  http.get.mockResolvedValue(ok(rows))
  http.put.mockImplementation(async (_url: string, body: unknown) => ok({ id: 'new', userId: 'u1', ...(body as object) }))
  http.post.mockResolvedValue(ok(undefined))
  http.delete.mockResolvedValue(ok(undefined))

  const Host = {
    components: { NotificationsSection },
    setup() {
      provideUserCenterContext(
        createUserCenterState({
          bridge: { me: {}, getAuthConfig: vi.fn(async () => null) } as never,
          storage: { files: { previewUrl: (id: string) => `/preview/${id}` } } as never,
          authStore: { userInfo: null } as never,
          message: messageApi as never,
          t: makePageTranslator('account.userCenter'),
          config: {},
          logoutAndRedirect: () => undefined,
        }),
      )
      return {}
    },
    template: '<NotificationsSection />',
  }
  const wrapper = mount(Host, { attachTo: document.body })
  await flushPromises()
  return wrapper
}

function button(wrapper: VueWrapper, test: string) {
  return wrapper.get(`[data-test="${test}"]`)
}

function confirm(wrapper: VueWrapper, text: string) {
  const popconfirm = wrapper.findAllComponents({ name: 'Popconfirm' }).find((c) => c.text().includes(text))!
  popconfirm.vm.$emit('positive-click')
}

describe('User Center - Notifications section', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  /** Pin "today" so zone conversions do not depend on the season the suite runs in. */
  function inJuly(): void {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-07-15T12:00:00Z'))
  }

  it('loads the caller\'s own preferences and shows one switch per delivering channel', async () => {
    const wrapper = await mountSection([])

    expect(http.get).toHaveBeenCalledWith('/notification/preferences')
    const text = wrapper.text()
    for (const label of ['Email', 'Text messages', 'Push notifications', 'Fax']) expect(text).toContain(label)
    // InApp / Webhook rows are never read by a sender; no switch for them.
    expect(text).not.toContain('Webhook')
    // Security mail is outside these switches, and the page says so.
    expect(text).toContain('sign-in codes and password resets are always delivered')
  })

  it('Save stays disabled until something changes', async () => {
    const wrapper = await mountSection([])
    expect(button(wrapper, 'notify-save').attributes('disabled')).toBeDefined()
  })

  it('★ turning one channel off writes that channel only, with no zone it is not setting, through the self-service route', async () => {
    const wrapper = await mountSection([])

    await button(wrapper, 'notify-enabled-Sms').trigger('click')
    await button(wrapper, 'notify-save').trigger('click')
    await flushPromises()

    expect(http.put).toHaveBeenCalledTimes(1)
    // No quietHoursTimeZone: a browser zone this server cannot resolve must not
    // be able to block switching a channel off.
    expect(http.put).toHaveBeenCalledWith('/notification/preferences', {
      channel: 'Sms',
      isEnabled: false,
    })
    expect(messageApi.success).toHaveBeenCalledWith('Notification settings saved')
    // Reloaded from the server after a successful save.
    expect(http.get).toHaveBeenCalledTimes(2)
  })

  it('switching quiet hours on starts from 22:00-07:00 and saves them with the zone', async () => {
    const wrapper = await mountSection([])

    await button(wrapper, 'notify-quiet-Email').trigger('click')
    await button(wrapper, 'notify-save').trigger('click')
    await flushPromises()

    expect(http.put).toHaveBeenCalledWith('/notification/preferences', {
      channel: 'Email',
      isEnabled: true,
      quietHoursStart: '22:00:00',
      quietHoursEnd: '07:00:00',
      quietHoursTimeZone: 'America/Toronto',
    })
  })

  it('★ stored zone-less quiet hours are shown as UTC on their channel, and converting them keeps the same instants', async () => {
    inJuly()
    const wrapper = await mountSection([
      { id: 'e1', userId: 'u1', channel: 'Email', isEnabled: true, quietHoursStart: '03:00:00', quietHoursEnd: '11:00:00', quietHoursTimeZone: null },
    ])

    // The shared zone stays the browser's; the legacy row says it is UTC.
    expect(wrapper.get('[data-test="notify-zone"]').text()).toContain('America/Toronto')
    expect(wrapper.get('[data-test="notify-own-zone-Email"]').text()).toContain('These times are in UTC.')

    await button(wrapper, 'notify-convert-Email').trigger('click')
    await button(wrapper, 'notify-save').trigger('click')
    await flushPromises()

    // 03:00-11:00 UTC is 23:00-07:00 in Toronto in July: converted, not relabelled.
    expect(http.put).toHaveBeenCalledTimes(1)
    expect(http.put).toHaveBeenCalledWith('/notification/preferences', {
      channel: 'Email',
      isEnabled: true,
      quietHoursStart: '23:00:00',
      quietHoursEnd: '07:00:00',
      quietHoursTimeZone: 'America/Toronto',
    })
  })

  it('★ "use this browser\'s time zone" converts every set window to it', async () => {
    inJuly()
    const wrapper = await mountSection([
      { id: 'e1', userId: 'u1', channel: 'Email', isEnabled: true, quietHoursStart: '22:00:00', quietHoursEnd: '07:00:00', quietHoursTimeZone: 'Asia/Tokyo' },
    ])

    await button(wrapper, 'notify-use-browser-zone').trigger('click')
    await button(wrapper, 'notify-save').trigger('click')
    await flushPromises()

    // 22:00-07:00 in Tokyo is 09:00-18:00 in Toronto in July.
    expect(http.put).toHaveBeenCalledWith('/notification/preferences', expect.objectContaining({
      channel: 'Email',
      quietHoursStart: '09:00:00',
      quietHoursEnd: '18:00:00',
      quietHoursTimeZone: 'America/Toronto',
    }))
  })

  it('★ switching off a channel kept in its own zone writes its window back untouched, and nothing else', async () => {
    const wrapper = await mountSection([
      { id: 'e1', userId: 'u1', channel: 'Email', isEnabled: true, quietHoursStart: '22:00:00', quietHoursEnd: '07:00:00', quietHoursTimeZone: 'Asia/Tokyo' },
      { id: 's1', userId: 'u1', channel: 'Sms', isEnabled: true, quietHoursStart: '21:00:00', quietHoursEnd: '06:00:00', quietHoursTimeZone: null },
    ])

    expect(wrapper.get('[data-test="notify-own-zone-Sms"]').text()).toContain('UTC')
    await button(wrapper, 'notify-enabled-Sms').trigger('click')
    await button(wrapper, 'notify-save').trigger('click')
    await flushPromises()

    expect(http.put).toHaveBeenCalledTimes(1)
    // Still a zone-less (UTC) row with the same times: not relabelled as Tokyo.
    expect(http.put).toHaveBeenCalledWith('/notification/preferences', {
      channel: 'Sms',
      isEnabled: false,
      quietHoursStart: '21:00:00',
      quietHoursEnd: '06:00:00',
    })
  })

  it('refuses to save a changed channel whose quiet hours start and end at the same time', async () => {
    const wrapper = await mountSection([
      { id: 'e1', userId: 'u1', channel: 'Email', isEnabled: true, quietHoursStart: '22:00:00', quietHoursEnd: '22:00:00', quietHoursTimeZone: 'America/Toronto' },
    ])

    await button(wrapper, 'notify-enabled-Email').trigger('click')
    await button(wrapper, 'notify-save').trigger('click')
    await flushPromises()

    expect(http.put).not.toHaveBeenCalled()
    expect(messageApi.warning).toHaveBeenCalledWith(expect.stringContaining('start and end at the same time'))
  })

  it('★ a stored empty window does not block saving a different channel', async () => {
    const wrapper = await mountSection([
      { id: 'e1', userId: 'u1', channel: 'Email', isEnabled: true, quietHoursStart: '22:00:00', quietHoursEnd: '22:00:00', quietHoursTimeZone: 'America/Toronto' },
    ])

    await button(wrapper, 'notify-enabled-Fax').trigger('click')
    await button(wrapper, 'notify-save').trigger('click')
    await flushPromises()

    expect(messageApi.warning).not.toHaveBeenCalled()
    expect(http.put).toHaveBeenCalledTimes(1)
    expect(http.put).toHaveBeenCalledWith('/notification/preferences', { channel: 'Fax', isEnabled: false })
  })

  it('★ a zone the server refuses is reported on the zone field, not as a bare save failure', async () => {
    const wrapper = await mountSection([])
    http.put.mockResolvedValueOnce({
      success: false,
      code: 400,
      message: "Unknown time zone 'America/Toronto'. Use an IANA time zone id such as 'America/Toronto'.",
      errorCode: 'NOTIFICATION_INVALID_TIME_ZONE',
    })

    await button(wrapper, 'notify-quiet-Email').trigger('click')
    await button(wrapper, 'notify-save').trigger('click')
    await flushPromises()

    const expected = 'This server does not recognise the time zone America/Toronto. Pick another one.'
    expect(wrapper.text()).toContain(expected)
    expect(messageApi.error).toHaveBeenCalledWith(expected)
  })

  it('a failed save keeps what was typed and reports the error', async () => {
    const wrapper = await mountSection([])
    http.put.mockResolvedValueOnce({ success: false, code: 400, message: 'Unknown time zone' })

    await button(wrapper, 'notify-enabled-Push').trigger('click')
    await button(wrapper, 'notify-save').trigger('click')
    await flushPromises()

    expect(messageApi.error).toHaveBeenCalledWith('Unknown time zone')
    expect(http.get).toHaveBeenCalledTimes(1)
    expect(button(wrapper, 'notify-save').attributes('disabled')).toBeUndefined()
  })

  it('Restore defaults posts the reset and reloads', async () => {
    const wrapper = await mountSection([{ id: 'e1', userId: 'u1', channel: 'Email', isEnabled: false }])

    confirm(wrapper, 'Restore defaults')
    await flushPromises()

    expect(http.post).toHaveBeenCalledWith('/notification/preferences/reset')
    expect(http.get).toHaveBeenCalledTimes(2)
  })

  it('lists per-category rules and removes one by id', async () => {
    const wrapper = await mountSection([
      { id: 'c1', userId: 'u1', channel: 'Email', category: 'Marketing', isEnabled: false },
    ])

    const rule = wrapper.get('[data-test="notify-rule-c1"]')
    expect(rule.text()).toContain('Email · Marketing')
    expect(rule.text()).toContain('Off')

    confirm(wrapper, 'Remove')
    await flushPromises()

    expect(http.delete).toHaveBeenCalledWith('/notification/preferences/c1')
  })
})
