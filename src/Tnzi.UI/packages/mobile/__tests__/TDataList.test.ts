import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import Vant, { List, PullRefresh } from 'vant'
import TDataList from '../src/components/list/TDataList.vue'

interface Row extends Record<string, unknown> {
  id: number
}

const items: Row[] = [{ id: 1 }, { id: 2 }]

function mountList(loadState: Record<string, unknown>) {
  return mount(TDataList, {
    props: { items, loadState, itemKey: 'id' },
    global: { plugins: [Vant] },
  })
}

describe('TDataList', () => {
  it('emits refresh once per pull, with no duplicate mobile-only event', async () => {
    const wrapper = mountList({})
    const pullRefresh = wrapper.findComponent(PullRefresh)

    pullRefresh.vm.$emit('refresh')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('refresh')).toHaveLength(1)
    expect(wrapper.emitted('pullRefresh')).toBeUndefined()
    expect(wrapper.emitted('update:query')).toHaveLength(1)
  })

  it('closes the indicator immediately when the parent reports no load state', async () => {
    const wrapper = mountList({})
    const pullRefresh = wrapper.findComponent(PullRefresh)

    pullRefresh.vm.$emit('update:modelValue', true)
    pullRefresh.vm.$emit('refresh')
    await wrapper.vm.$nextTick()

    expect(pullRefresh.props('modelValue')).toBe(false)
  })

  it('keeps the indicator open until the parent load finishes', async () => {
    const wrapper = mountList({ loading: true })
    const pullRefresh = wrapper.findComponent(PullRefresh)

    pullRefresh.vm.$emit('update:modelValue', true)
    pullRefresh.vm.$emit('refresh')
    await wrapper.vm.$nextTick()

    // Still loading: a fixed timer would already have stopped the animation here.
    expect(pullRefresh.props('modelValue')).toBe(true)

    await wrapper.setProps({ loadState: { loading: false } })

    expect(pullRefresh.props('modelValue')).toBe(false)
  })

  // van-list resets its internal loading flag to the `loading` prop on every
  // re-render, so with no mirrored load state every scroll event during a
  // pending fetch asked for the next page again.
  it('emits loadMore once per fetch when the parent reports no load state', async () => {
    const wrapper = mountList({})
    const list = wrapper.findComponent(List)

    list.vm.$emit('load')
    await wrapper.vm.$nextTick()
    list.vm.$emit('load')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('loadMore')).toHaveLength(1)
    expect(list.props('loading')).toBe(true)

    // The next page arrived: the list may ask again.
    await wrapper.setProps({ items: [...items, { id: 3 }] })
    expect(list.props('loading')).toBe(false)
    list.vm.$emit('load')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('loadMore')).toHaveLength(2)
  })

  /**
   * The Vant idiom for an infinite list is `list.value.push(...page)`: the
   * same reactive array, appended in place. A guard released only when the
   * array REFERENCE changed stayed set for the lifetime of the component for
   * such a consumer (no `loadState`, in-place append): exactly one `loadMore`,
   * page 2 never requested, no error anywhere.
   */
  it('releases the in-flight guard when the parent appends rows in place', async () => {
    const rows = reactive<Row[]>([{ id: 1 }, { id: 2 }])
    const wrapper = mount(TDataList, {
      props: { items: rows, loadState: {}, itemKey: 'id' },
      global: { plugins: [Vant] },
    })
    const list = wrapper.findComponent(List)

    list.vm.$emit('load')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('loadMore')).toHaveLength(1)

    rows.push({ id: 3 })
    await wrapper.vm.$nextTick()
    expect(list.props('loading')).toBe(false)
    list.vm.$emit('load')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('loadMore')).toHaveLength(2)
  })

  it('releases the in-flight guard when the parent reports an error', async () => {
    const wrapper = mountList({})
    const list = wrapper.findComponent(List)

    list.vm.$emit('load')
    await wrapper.vm.$nextTick()
    await wrapper.setProps({ loadState: { error: 'network' } })
    expect(list.props('loading')).toBe(false)
    list.vm.$emit('load')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('loadMore')).toHaveLength(2)
  })

  it('releases the in-flight guard when the parent load finishes or reports no more', async () => {
    const wrapper = mountList({ loading: false })
    const list = wrapper.findComponent(List)

    list.vm.$emit('load')
    await wrapper.vm.$nextTick()
    await wrapper.setProps({ loadState: { loading: true } })
    await wrapper.setProps({ loadState: { loading: false } })
    expect(list.props('loading')).toBe(false)

    list.vm.$emit('load')
    await wrapper.vm.$nextTick()
    await wrapper.setProps({ loadState: { loading: false, noMore: true } })
    expect(list.props('finished')).toBe(true)
    expect(wrapper.emitted('loadMore')).toHaveLength(2)
  })

  it('trigger="manual" loads through a button, never through scrolling', async () => {
    const wrapper = mount(TDataList, {
      props: { items, trigger: 'manual' },
      global: { plugins: [Vant] },
    })
    const list = wrapper.findComponent(List)
    expect(list.props('disabled')).toBe(true)
    expect(list.props('immediateCheck')).toBe(false)

    const button = wrapper.find('.t-data-list__load-more')
    expect(button.exists()).toBe(true)
    await button.trigger('click')
    expect(wrapper.emitted('loadMore')).toHaveLength(1)

    await wrapper.setProps({ loadState: { noMore: true } })
    expect(wrapper.find('.t-data-list__load-more').exists()).toBe(false)
  })

  it('trigger="pull" refreshes by pulling and never auto-loads more', () => {
    const wrapper = mount(TDataList, {
      props: { items, trigger: 'pull' },
      global: { plugins: [Vant] },
    })
    expect(wrapper.findComponent(List).props('disabled')).toBe(true)
    expect(wrapper.findComponent(PullRefresh).props('disabled')).toBe(false)
    expect(wrapper.find('.t-data-list__load-more').exists()).toBe(false)
  })

  it('trigger="scroll" auto-loads and turns pull-to-refresh off', () => {
    const wrapper = mount(TDataList, {
      props: { items, trigger: 'scroll' },
      global: { plugins: [Vant] },
    })
    expect(wrapper.findComponent(List).props('disabled')).toBe(false)
    expect(wrapper.findComponent(PullRefresh).props('disabled')).toBe(true)
  })

  it('pullToRefresh=false still wins over the hybrid default', () => {
    const wrapper = mount(TDataList, {
      props: { items, pullToRefresh: false },
      global: { plugins: [Vant] },
    })
    expect(wrapper.findComponent(PullRefresh).props('disabled')).toBe(true)
    expect(wrapper.findComponent(List).props('disabled')).toBe(false)
  })

  it('forwards a consumer class to the root element', () => {
    const wrapper = mount(TDataList, {
      props: { items },
      attrs: { class: 'consumer-class' },
      global: { plugins: [Vant] },
    })

    expect(wrapper.classes()).toContain('consumer-class')
    expect(wrapper.classes()).toContain('t-data-list')
  })
})
