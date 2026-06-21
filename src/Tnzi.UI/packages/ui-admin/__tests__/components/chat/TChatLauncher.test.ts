import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { NBadge } from 'naive-ui'
import TChatLauncher from '../../../src/components/chat/TChatLauncher.vue'

describe('TChatLauncher', () => {
  it('emits open on click', async () => {
    const w = mount(TChatLauncher, { props: { unreadCount: 0 }, global: { stubs: { NBadge: { template: '<div><slot/></div>' }, Icon: true } } })
    await w.find('button').trigger('click')
    expect(w.emitted('open')).toBeTruthy()
  })
  it('passes unreadCount to badge', () => {
    const w = mount(TChatLauncher, { props: { unreadCount: 5 }, global: { stubs: { Icon: true } } })
    const badge = w.findComponent(NBadge)
    expect(badge.props('value')).toBe(5)
    expect(badge.props('show')).toBe(true)
  })
})
