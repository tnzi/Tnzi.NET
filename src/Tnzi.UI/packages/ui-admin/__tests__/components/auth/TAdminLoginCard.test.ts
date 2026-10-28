import { describe, it, expect, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import TAdminLoginCard from '../../../src/components/auth/TAdminLoginCard.vue'

/**
 * NTabs is deliberately NOT stubbed: the only thing worth asserting about the
 * method switcher is the text on it, and a stub cannot see that. Both tabs used
 * to render blank (see `TDetailLayout` for the naive-ui mechanism).
 */
function mountCard(props: Record<string, unknown> = {}) {
  return mount(TAdminLoginCard, {
    props: { onLogin: async () => {}, enableCodeLogin: true, ...props },
  })
}

describe('TAdminLoginCard', () => {
  it('labels the password / SMS-code method tabs', () => {
    const labels = mountCard().findAll('.n-tabs-tab__label')
    expect(labels).toHaveLength(2)
    expect(labels[0].text()).toBe('Password')
    expect(labels[1].text()).toBe('SMS code')
  })

  it('routes the labels through `translate` when one is supplied', () => {
    const labels = mountCard({ translate: (k: string) => `t:${k}` }).findAll('.n-tabs-tab__label')
    expect(labels.map((n) => n.text())).toEqual(['t:admin.login.pwd', 't:admin.login.code'])
  })

  it('omits the switcher entirely when code login is off', () => {
    expect(mountCard({ enableCodeLogin: false }).find('.n-tabs').exists()).toBe(false)
  })

  // The backend's [Required] refuses a whitespace-only user name or password:
  // letting one through only trades the field message for a failed sign-in
  // that counts towards the lockout.
  it.each([
    ['a whitespace-only user name', '   ', 'secret'],
    ['a whitespace-only password', 'admin', '      '],
  ])('holds %s at the form instead of signing in', async (_case, userName, password) => {
    const onLogin = vi.fn(async () => undefined)
    const w = mount(TAdminLoginCard, {
      props: { onLogin, defaultUserName: userName, defaultPassword: password },
      attachTo: document.body,
    })
    await w.find('button[type="submit"]').trigger('click')
    await flushPromises()
    expect(onLogin).not.toHaveBeenCalled()
    w.unmount()
  })

  it('signs in with a password that merely has spaces around it', async () => {
    const onLogin = vi.fn(async () => undefined)
    const w = mount(TAdminLoginCard, {
      props: { onLogin, defaultUserName: 'admin', defaultPassword: ' secret ' },
      attachTo: document.body,
    })
    await w.find('button[type="submit"]').trigger('click')
    await flushPromises()
    expect(onLogin).toHaveBeenCalledTimes(1)
    w.unmount()
  })
})
