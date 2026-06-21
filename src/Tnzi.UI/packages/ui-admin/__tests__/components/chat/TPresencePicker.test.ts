/**
 * TPresencePicker tests
 *
 * NDropdown does not open its floating menu in jsdom (teleports to document.body
 * and relies on real pointer events), so we cannot trigger a menu click from
 * the outside. Instead we call the component's internal `onSelect` handler
 * directly — this is the exact function that NDropdown calls when the user
 * picks an option, so the assertion covers the real emit path.
 */
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TPresencePicker from '../../../src/components/chat/TPresencePicker.vue'
import { UserPresenceStatus } from '@tnzi/core/services/chat'

// Stubs so mount doesn't blow up in jsdom
const globalConfig = {
  stubs: {
    NDropdown: { template: '<div class="n-dropdown-stub"><slot/></div>', props: ['options', 'trigger', 'renderOption'], emits: ['select'] },
    TChatAvatar: true,
    Icon: true,
  },
}

describe('TPresencePicker', () => {
  it('emits change(Online) when onSelect is called with Online key', () => {
    const wrapper = mount(TPresencePicker, {
      props: { status: UserPresenceStatus.Offline, name: 'Alice' },
      global: globalConfig,
    })
    // Call the handler the same way NDropdown does.
    ;(wrapper.vm as unknown as { onSelect: (key: number) => void }).onSelect(UserPresenceStatus.Online)
    const emitted = wrapper.emitted('change')
    expect(emitted).toBeTruthy()
    expect(emitted![0]).toEqual([UserPresenceStatus.Online])
  })

  it('emits change(Away) when onSelect is called with Away key', () => {
    const wrapper = mount(TPresencePicker, {
      props: { status: UserPresenceStatus.Online, name: 'Bob' },
      global: globalConfig,
    })
    ;(wrapper.vm as unknown as { onSelect: (key: number) => void }).onSelect(UserPresenceStatus.Away)
    expect(wrapper.emitted('change')?.[0]).toEqual([UserPresenceStatus.Away])
  })

  it('emits change(Busy) when onSelect is called with Busy key', () => {
    const wrapper = mount(TPresencePicker, {
      props: { status: UserPresenceStatus.Online, name: 'Carol' },
      global: globalConfig,
    })
    ;(wrapper.vm as unknown as { onSelect: (key: number) => void }).onSelect(UserPresenceStatus.Busy)
    expect(wrapper.emitted('change')?.[0]).toEqual([UserPresenceStatus.Busy])
  })

  it('emits change(Invisible) when onSelect is called with Invisible key', () => {
    const wrapper = mount(TPresencePicker, {
      props: { status: UserPresenceStatus.Online, name: 'Dave' },
      global: globalConfig,
    })
    ;(wrapper.vm as unknown as { onSelect: (key: number) => void }).onSelect(UserPresenceStatus.Invisible)
    expect(wrapper.emitted('change')?.[0]).toEqual([UserPresenceStatus.Invisible])
  })

  it('renders the user name in the trigger area', () => {
    const wrapper = mount(TPresencePicker, {
      props: { status: UserPresenceStatus.Online, name: 'Alice' },
      global: globalConfig,
    })
    expect(wrapper.text()).toContain('Alice')
  })
})
