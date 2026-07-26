import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import Vant, { PullRefresh } from 'vant'
import TDataList from '../components/list/TDataList.vue'

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
