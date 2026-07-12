import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { NBadge } from 'naive-ui'
import TChatLauncher from '../../../src/components/chat/TChatLauncher.vue'
import { ChatNewMessageEffect } from '@tnzi/core/services/chat'

const badgeStub = { stubs: { NBadge: { template: '<div><slot/></div>' }, Icon: true } }

describe('TChatLauncher', () => {
  it('emits open on click', async () => {
    const w = mount(TChatLauncher, { props: { unreadCount: 0 }, global: badgeStub })
    await w.find('button').trigger('click')
    expect(w.emitted('open')).toBeTruthy()
  })
  it('passes unreadCount to badge', () => {
    const w = mount(TChatLauncher, { props: { unreadCount: 5 }, global: { stubs: { Icon: true } } })
    const badge = w.findComponent(NBadge)
    expect(badge.props('value')).toBe(5)
    expect(badge.props('show')).toBe(true)
  })

  it('plays the configured effect animation when attention bumps', async () => {
    const w = mount(TChatLauncher, {
      props: { unreadCount: 1, effect: ChatNewMessageEffect.Shake, attention: 0 },
      global: badgeStub,
    })
    expect(w.find('.t-chat-launcher').classes()).not.toContain('t-chat-launcher--shake')
    await w.setProps({ attention: 1 })
    expect(w.find('.t-chat-launcher').classes()).toContain('t-chat-launcher--shake')
  })

  it('does not animate when the effect is None', async () => {
    const w = mount(TChatLauncher, {
      props: { unreadCount: 1, effect: ChatNewMessageEffect.None, attention: 0 },
      global: badgeStub,
    })
    await w.setProps({ attention: 1 })
    const animated = w.find('.t-chat-launcher').classes().some((c) => c.startsWith('t-chat-launcher--'))
    expect(animated).toBe(false)
  })
})
