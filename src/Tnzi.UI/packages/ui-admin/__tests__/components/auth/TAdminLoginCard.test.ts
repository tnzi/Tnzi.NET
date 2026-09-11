import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
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
})
