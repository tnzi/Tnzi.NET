import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'

/**
 * `TDetailBlock` - the block header inside a section.
 *
 * What it owns: the title with its icon and the chip beside it, the block's
 * own actions on the right, the hint line, and the spacing under the head
 * when there is no hint. The body and the spacing between blocks stay the
 * host's.
 */
import TDetailBlock from '../../../src/components/detail/TDetailBlock.vue'
import * as componentsBarrel from '../../../src/components/index'

const Icon = defineComponent({ name: 'TSvgIcon', props: ['icon', 'size'], setup: (p) => () => h('i', { 'data-icon': p.icon }) })

function mountBlock(props: Record<string, unknown>, slots: Record<string, string> = {}) {
  return mount(TDetailBlock, {
    props,
    slots,
    global: { stubs: { TSvgIcon: Icon } },
  })
}

describe('TDetailBlock', () => {
  it('is on the components barrel', () => {
    expect(componentsBarrel.TDetailBlock).toBe(TDetailBlock)
  })

  it('renders icon, title, the chip beside it, the actions and the hint', () => {
    const w = mountBlock(
      { title: 'Sign-in IP allow-list', icon: 'mdi:ip-network-outline', hint: 'Only the listed addresses.' },
      {
        titleExtra: '<b class="chip">On</b>',
        actions: '<button class="save">Save</button>',
        default: '<p class="body">body</p>',
      },
    )
    const title = w.find('.t-detail-block__title')
    expect(title.find('i').attributes('data-icon')).toBe('mdi:ip-network-outline')
    expect(title.text()).toContain('Sign-in IP allow-list')
    expect(title.find('.chip').exists()).toBe(true)
    expect(w.find('.t-detail-block__actions .save').exists()).toBe(true)
    expect(w.find('.t-detail-block__hint').text()).toBe('Only the listed addresses.')
    expect(w.find('.body').exists()).toBe(true)
    expect(w.find('.t-detail-block__head').classes()).not.toContain('t-detail-block__head--bare')
  })

  it('renders no actions column and no hint line when neither is given', () => {
    const w = mountBlock({ title: 'Change password' }, { default: '<form />' })
    expect(w.find('.t-detail-block__actions').exists()).toBe(false)
    expect(w.find('.t-detail-block__hint').exists()).toBe(false)
    expect(w.find('i').exists()).toBe(false)
    // Without a hint the head carries the body gap itself.
    expect(w.find('.t-detail-block__head').classes()).toContain('t-detail-block__head--bare')
  })

  it('passes a root class through, so a host can still address the block', () => {
    const w = mountBlock({ title: 'x' }, {})
    expect(w.classes()).toContain('t-detail-block')
  })
})
