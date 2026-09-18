import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// The session list from `/users/profile/sessions` has no "this is you"
// marker, but the access token this tab holds does: its `session_id` claim.
// The section reads it to label the caller's own row and keep "Revoke" off
// it - revoking your own session from the User Center bounced you to the
// login page with no explanation. The same id also fixes the "Sign out other
// devices" button, which was disabled off `sessions.length` and therefore
// never, since the list always includes the caller's session while signed in.

function base64url(input: string): string {
  return Buffer.from(input, 'utf8').toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}
function jwtWith(sessionId: string): string {
  return `${base64url('{"alg":"HS256"}')}.${base64url(JSON.stringify({ sub: 'u1', session_id: sessionId }))}.sig`
}

const clientMock = vi.hoisted(() => ({ token: null as string | null }))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ getAccessToken: () => clientMock.token }),
}))

const messageApi = {
  success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(),
  loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn(),
}

import SessionsSection from '../../../src/pages/account/sections/SessionsSection.vue'
import {
  createUserCenterState,
  provideUserCenterContext,
} from '../../../src/pages/account/user-center-context'
import { makePageTranslator } from '../../../src/pages/_shared/translate'

const OWN = { id: '0F8FAD5B-D9CB-469F-A165-70867728950E', userId: 'u1', deviceInfo: 'Chrome / Windows', ipAddress: '10.0.0.1', creationTime: '2026-09-12T00:00:00Z', lastActivityTime: '2026-09-12T01:00:00Z', isRevoked: false }
const OTHER = { id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee', userId: 'u1', deviceInfo: 'Safari / iPhone', ipAddress: '10.0.0.2', creationTime: '2026-09-11T00:00:00Z', lastActivityTime: '2026-09-11T01:00:00Z', isRevoked: false }

async function mountSessions(rows: unknown[]) {
  const me = {
    getSessions: vi.fn(async () => rows),
    revokeSession: vi.fn(async () => undefined),
    revokeAllSessions: vi.fn(async () => undefined),
  }
  const Host = {
    components: { SessionsSection },
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
    template: '<SessionsSection />',
  }
  const wrapper = mount(Host, { attachTo: document.body })
  await flushPromises()
  return { wrapper, me }
}

function rowsOf(wrapper: ReturnType<typeof mount>) {
  // TResponsiveTable renders a naive data table on desktop; each body row is a <tr>.
  return wrapper.findAll('tbody tr').filter((tr) => tr.text().includes('/'))
}

describe('SessionsSection - current session', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    clientMock.token = null
  })

  it('★ labels the caller\'s row (case-insensitively) and renders no Revoke on it', async () => {
    clientMock.token = jwtWith(OWN.id.toLowerCase())
    const { wrapper } = await mountSessions([OWN, OTHER])

    const rows = rowsOf(wrapper)
    expect(rows).toHaveLength(2)
    const own = rows.find((r) => r.text().includes('Chrome'))!
    const other = rows.find((r) => r.text().includes('Safari'))!

    expect(own.text()).toContain('This device')
    expect(own.findAll('button').some((b) => b.text() === 'Revoke')).toBe(false)
    expect(other.text()).not.toContain('This device')
    expect(other.findAll('button').some((b) => b.text() === 'Revoke')).toBe(true)
  })

  it('★ "Sign out other devices" is disabled when only the caller\'s own session is listed', async () => {
    clientMock.token = jwtWith(OWN.id)
    const { wrapper } = await mountSessions([OWN])

    const button = wrapper.findAll('button').find((b) => b.text().includes('Sign out other devices'))!
    expect(button.attributes('disabled')).toBeDefined()
  })

  it('"Sign out other devices" is enabled when another device is listed', async () => {
    clientMock.token = jwtWith(OWN.id)
    const { wrapper } = await mountSessions([OWN, OTHER])

    const button = wrapper.findAll('button').find((b) => b.text().includes('Sign out other devices'))!
    expect(button.attributes('disabled')).toBeUndefined()
  })

  it('without a session-bound token nothing is marked and every row keeps its (confirmed) Revoke', async () => {
    clientMock.token = 'opaque'
    const { wrapper } = await mountSessions([OWN, OTHER])

    expect(wrapper.text()).not.toContain('This device')
    const rows = rowsOf(wrapper)
    expect(rows.every((r) => r.findAll('button').some((b) => b.text() === 'Revoke'))).toBe(true)
    const button = wrapper.findAll('button').find((b) => b.text().includes('Sign out other devices'))!
    expect(button.attributes('disabled')).toBeUndefined()
  })
})
