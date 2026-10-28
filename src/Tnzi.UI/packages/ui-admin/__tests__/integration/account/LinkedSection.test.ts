import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// The OAuth callback of a link flow is anonymous and a link token is just a URL
// parameter, so the callback no longer links: it stages the provider identity
// and sends the browser back here with `#linkCode=…&linkedProvider=…`. This
// section redeems the code as the signed-in user; the backend only links when
// that user issued the token.

const messageApi = {
  success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(),
  loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn(),
}

import LinkedSection from '../../../src/pages/account/sections/LinkedSection.vue'
import {
  createUserCenterState,
  provideUserCenterContext,
} from '../../../src/pages/account/user-center-context'
import { makePageTranslator } from '../../../src/pages/_shared/translate'

async function mountLinked(completeOAuthLink: (provider: string, code: string) => Promise<void>) {
  const me = {
    getLinkedAccounts: vi.fn(async () => []),
    completeOAuthLink: vi.fn(completeOAuthLink),
    issueOAuthLinkToken: vi.fn(),
    unlinkAccount: vi.fn(),
  }
  const Host = {
    components: { LinkedSection },
    setup() {
      const ctx = createUserCenterState({
        bridge: { me, getAuthConfig: vi.fn(async () => null), oauthLoginUrl: () => '' } as never,
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
    template: '<LinkedSection />',
  }
  const wrapper = mount(Host, { attachTo: document.body })
  await flushPromises()
  return { wrapper, me }
}

describe('LinkedSection - finishing a staged link', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  afterEach(() => {
    window.history.replaceState(null, '', '/')
  })

  it('★ redeems the completion code from the fragment, then drops it from the address bar', async () => {
    window.history.replaceState(null, '', '/account?section=linked#linkCode=code-1&linkedProvider=github')

    const { me, wrapper } = await mountLinked(async () => undefined)

    expect(me.completeOAuthLink).toHaveBeenCalledWith('github', 'code-1')
    expect(window.location.hash).toBe('')
    expect(window.location.search).toBe('?section=linked')
    // The list is (re)loaded after completing, so the new row shows up.
    expect(me.getLinkedAccounts).toHaveBeenCalled()
    wrapper.unmount()
  })

  it('shows the server refusal (another account\'s code, expired, used) instead of swallowing it', async () => {
    window.history.replaceState(null, '', '/account#linkCode=code-2&linkedProvider=github')

    const { wrapper } = await mountLinked(async () => {
      throw new Error('Invalid or expired link confirmation')
    })

    expect(messageApi.error).toHaveBeenCalledWith('Invalid or expired link confirmation')
    expect(window.location.hash).toBe('')
    wrapper.unmount()
  })

  it('does nothing when the page was not reached from a link callback', async () => {
    window.history.replaceState(null, '', '/account#some-anchor')

    const { me, wrapper } = await mountLinked(async () => undefined)

    expect(me.completeOAuthLink).not.toHaveBeenCalled()
    expect(window.location.hash).toBe('#some-anchor')
    wrapper.unmount()
  })
})
