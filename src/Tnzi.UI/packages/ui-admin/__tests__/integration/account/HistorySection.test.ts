import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// `/users/profile/login-history` returns LoginLogDto, which carries the raw
// `userAgent` and no `deviceInfo`. The Device column read `deviceInfo`, so it
// was blank on every row for every user. It now renders the user agent the
// same way the sessions table renders its device string.

const messageApi = {
  success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(),
  loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn(),
}

import HistorySection from '../../../src/pages/account/sections/HistorySection.vue'
import {
  createUserCenterState,
  provideUserCenterContext,
} from '../../../src/pages/account/user-center-context'
import { makePageTranslator } from '../../../src/pages/_shared/translate'

const CHROME_ON_WINDOWS = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36'

const ROW = {
  id: 'l1',
  userId: 'u1',
  userName: 'alice',
  ipAddress: '10.0.0.1',
  userAgent: CHROME_ON_WINDOWS,
  isSuccess: true,
  failureReason: null,
  loginTime: '2026-09-12T00:00:00Z',
}

async function mountHistory(rows: unknown[]) {
  const me = { getLoginHistory: vi.fn(async () => rows) }
  const Host = {
    components: { HistorySection },
    setup() {
      const ctx = createUserCenterState({
        bridge: { me, getAuthConfig: vi.fn(async () => null) } as never,
        storage: { files: { previewUrl: (id: string) => `/preview/${id}` } } as never,
        authStore: { userInfo: null } as never,
        message: messageApi as never,
        t: makePageTranslator('account.userCenter'),
        config: {},
        logoutAndRedirect: () => undefined,
      })
      provideUserCenterContext(ctx)
      return {}
    },
    template: '<HistorySection />',
  }
  const wrapper = mount(Host, { attachTo: document.body })
  await flushPromises()
  return { wrapper, me }
}

describe('HistorySection - device column', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('★ renders the device parsed from the row\'s user agent, with the raw agent as its tooltip', async () => {
    const { wrapper } = await mountHistory([ROW])

    const row = wrapper.findAll('tbody tr').find((tr) => tr.text().includes('10.0.0.1'))!
    expect(row.text()).toContain('Windows · Chrome')
    expect(row.find(`[title="${CHROME_ON_WINDOWS}"]`).exists()).toBe(true)
  })

  it('a row without a user agent shows the placeholder rather than a blank cell', async () => {
    const { wrapper } = await mountHistory([{ ...ROW, userAgent: null }])

    const row = wrapper.findAll('tbody tr').find((tr) => tr.text().includes('10.0.0.1'))!
    const cells = row.findAll('td')
    // time / ip / device / result / reason
    expect(cells[2]!.text()).toBe('-')
  })
})
