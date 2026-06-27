/**
 * TPresencePicker tests
 *
 * The redesigned picker is an avatar button (status dot) that opens an arrow
 * NPopover whose content is a status menu. NPopover content teleports and does
 * not render reliably in jsdom, so — as with the old NDropdown version — we call
 * the component's internal `select` handler directly: it is the exact function
 * each menu option's click invokes, so the assertion covers the real emit path.
 */
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TPresencePicker from '../../../src/components/chat/TPresencePicker.vue'
import { UserPresenceStatus } from '@tnzi/core/services/chat'

const globalConfig = {
  stubs: {
    NPopover: { template: '<div class="n-popover-stub"><slot name="trigger" /><slot /></div>' },
    TChatAvatar: true,
    TPresenceDot: true,
    Icon: true,
  },
}

describe('TPresencePicker', () => {
  it('renders the avatar trigger button', () => {
    const wrapper = mount(TPresencePicker, {
      props: { status: UserPresenceStatus.Online, name: 'Alice' },
      global: globalConfig,
    })
    expect(wrapper.find('.t-presence-picker').exists()).toBe(true)
  })

  it('emits change with the picked status', () => {
    const cases: UserPresenceStatus[] = [
      UserPresenceStatus.Online,
      UserPresenceStatus.Away,
      UserPresenceStatus.Busy,
      UserPresenceStatus.Invisible,
    ]
    for (const status of cases) {
      const wrapper = mount(TPresencePicker, {
        props: { status: UserPresenceStatus.Offline, name: 'Alice' },
        global: globalConfig,
      })
      ;(wrapper.vm as unknown as { select: (s: UserPresenceStatus) => void }).select(status)
      expect(wrapper.emitted('change')?.[0]).toEqual([status])
    }
  })
})
