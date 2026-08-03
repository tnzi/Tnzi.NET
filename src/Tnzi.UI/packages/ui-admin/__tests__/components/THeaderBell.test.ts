import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

vi.mock('naive-ui', () => ({
  NBadge: {
    name: 'NBadge',
    props: { value: {}, max: {}, show: Boolean, processing: Boolean },
    template: '<span class="nbadge" :data-value="value" :data-show="String(show)"><slot /></span>',
  },
  NPopover: {
    name: 'NPopover',
    props: ['trigger', 'placement', 'showArrow'],
    emits: ['update:show'],
    methods: {
      open() {
        ;(this as unknown as { $emit: (e: string, v: boolean) => void }).$emit('update:show', true)
      },
    },
    template:
      '<div class="npop"><button class="npop-toggle" @click="open" /><slot name="trigger" /><div class="npop-body"><slot /></div></div>',
  },
}))
vi.mock('@tnzi/ui', () => ({
  TSvgIcon: { name: 'TSvgIcon', props: ['icon'], template: '<i class="tsvg" />' },
}))

import THeaderBell from '../../src/components/layout/THeaderBell.vue'

describe('THeaderBell', () => {
  it('shows the unread badge and renders one #item per item', () => {
    const w = mount(THeaderBell, {
      props: { unreadCount: 3, items: [{ id: 1 }, { id: 2 }] },
      slots: { item: '<div class="bell-item">x</div>' },
    })
    expect(w.find('.nbadge').attributes('data-value')).toBe('3')
    expect(w.find('.nbadge').attributes('data-show')).toBe('true')
    expect(w.findAll('.bell-item')).toHaveLength(2)
  })

  it('emits load-more when hasMore and the button is clicked', async () => {
    const w = mount(THeaderBell, { props: { items: [{ id: 1 }], hasMore: true }, slots: { item: '<div />' } })
    await w.find('.t-header-bell__more').trigger('click')
    expect(w.emitted('load-more')).toBeTruthy()
  })

  it('shows the empty state when there are no items', () => {
    const w = mount(THeaderBell, { props: { items: [], emptyText: 'All caught up' } })
    expect(w.text()).toContain('All caught up')
    expect(w.find('.nbadge').attributes('data-show')).toBe('false')
  })

  it('forwards the popover open/close as update:show', async () => {
    // ★ 没有它,宿主没有任何时机去刷新。剩下的唯一办法是定时轮询 —— 每个间隔
    //   花一次请求去追一个「只有用户打开面板才看得见」的变化。
    const w = mount(THeaderBell, { props: { items: [] } })
    await w.find('.npop-toggle').trigger('click')
    expect(w.emitted('update:show')?.[0]?.[0]).toBe(true)
  })
})
