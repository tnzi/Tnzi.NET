import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

/**
 * The recipient-facing unsubscribe page.
 *
 * The whole one-click unsubscribe chain exists for this one screen, and every
 * person who reaches it arrives from an email with no account and no session -
 * so the things worth pinning are: it works without auth, it confirms before it
 * acts, it says one thing for every kind of dead link, and it offers a way back.
 */
const unsubMock = vi.hoisted(() => ({
  preview: vi.fn(async () => ({
    maskedAddress: 'r***@example.com',
    channel: 'Email',
    category: 'Marketing',
  })),
  unsubscribe: vi.fn(async () => true),
  resubscribe: vi.fn(async () => true),
}))

const routeQuery = vi.hoisted(() => ({ value: { token: 'signed.token' } as Record<string, string> }))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: routeQuery.value, params: {}, path: '/unsubscribe' }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn(), resolve: vi.fn(() => ({ path: '/' })) }),
}))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/services/bridges/notification-bridge', () => ({
  createNotificationBridge: () => ({ publicUnsubscribe: unsubMock }),
}))

const stubs = {
  Spin: { template: '<div><slot /></div>' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Input: { props: ['value'], template: '<textarea />' },
}

async function mountPage() {
  const { default: Page } = await import('../../../src/pages/notification/UnsubscribePage.vue')
  const wrapper = mount(Page, { global: { stubs } })
  await nextTick()
  await new Promise((r) => setTimeout(r, 10))
  return wrapper
}

describe('UnsubscribePage', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    routeQuery.value = { token: 'signed.token' }
    unsubMock.preview.mockClear()
    unsubMock.unsubscribe.mockClear()
    unsubMock.resubscribe.mockClear()
  })

  it('previews what the link would unsubscribe, masked', async () => {
    const wrapper = await mountPage()

    expect(unsubMock.preview).toHaveBeenCalledWith('signed.token')
    expect(wrapper.text()).toContain('r***@example.com')
  })

  /**
   * ★ Confirmation is not optional here: the link may have been forwarded, and
   * a mail client's link prefetcher must never be able to unsubscribe someone.
   */
  it('does not act on load - only on confirm', async () => {
    const wrapper = await mountPage()

    expect(unsubMock.unsubscribe).not.toHaveBeenCalled()

    await wrapper.find('button').trigger('click')
    await nextTick()

    expect(unsubMock.unsubscribe).toHaveBeenCalledWith('signed.token', undefined)
  })

  it('offers a way back after unsubscribing', async () => {
    const wrapper = await mountPage()
    await wrapper.find('button').trigger('click')
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))

    // Now on the "you are unsubscribed" state, whose only button is the undo.
    await wrapper.find('button').trigger('click')
    await nextTick()

    expect(unsubMock.resubscribe).toHaveBeenCalledWith('signed.token')
  })

  /** Every unusable link reads the same - telling them apart tells a prober which tokens are real. */
  it('says one thing for a dead link', async () => {
    unsubMock.preview.mockResolvedValueOnce(null as never)
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('cannot be used')
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('treats a missing token as a dead link without asking the server', async () => {
    routeQuery.value = {}
    const wrapper = await mountPage()

    expect(unsubMock.preview).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('cannot be used')
  })
})
