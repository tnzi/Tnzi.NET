import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

vi.mock('@iconify/vue', () => ({
  Icon: { name: 'Icon', props: ['icon'], template: '<i class="icon" :data-icon="icon" />' },
}))

import TNoteCard from '../../../src/components/display/TNoteCard.vue'
import TActivityFeed from '../../../src/components/display/TActivityFeed.vue'

const stubs = { TAvatar: { name: 'TAvatar', props: ['name', 'src', 'size', 'seed'], template: '<span class="tavatar" />' } }

describe('TNoteCard', () => {
  it('renders author, time and content', () => {
    const w = mount(TNoteCard, { props: { author: 'Alice', time: '2m ago', content: 'Hello' }, global: { stubs } })
    expect(w.text()).toContain('Alice')
    expect(w.text()).toContain('2m ago')
    expect(w.text()).toContain('Hello')
  })

  it('prefers the default slot over content', () => {
    const w = mount(TNoteCard, { props: { author: 'A', content: 'ignored' }, slots: { default: 'slotted body' }, global: { stubs } })
    expect(w.text()).toContain('slotted body')
  })
})

describe('TActivityFeed', () => {
  it('renders one #item slot per item', () => {
    const w = mount(TActivityFeed, {
      props: { items: [{ id: 1 }, { id: 2 }] },
      slots: { item: '<div class="feed-item">row</div>' },
    })
    expect(w.findAll('.feed-item')).toHaveLength(2)
  })

  it('shows the empty text when there are no items', () => {
    const w = mount(TActivityFeed, { props: { items: [], emptyText: 'Nothing here' } })
    expect(w.text()).toContain('Nothing here')
  })
})
