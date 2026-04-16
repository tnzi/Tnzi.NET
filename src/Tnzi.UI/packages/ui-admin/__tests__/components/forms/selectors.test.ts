import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import TDictSelector from '../../../src/components/forms/TDictSelector.vue'
import TRoleSelector from '../../../src/components/forms/TRoleSelector.vue'
import TUserSelector from '../../../src/components/forms/TUserSelector.vue'
import TTenantSelector from '../../../src/components/forms/TTenantSelector.vue'

const selectStub = {
  name: 'Select',
  props: ['value', 'options', 'multiple', 'filterable', 'loading', 'remote', 'disabled', 'placeholder'],
  emits: ['update:value', 'search'],
  template: '<div class="n-select-stub" :data-options="options.length" @click="$emit(\'update:value\', \'v1\')"></div>',
}

async function assertSelector(Component: unknown) {
  const fetcher = vi.fn(async (_keyword: string) => [
    { label: 'Option A', value: 'a' },
    { label: 'Option B', value: 'b' },
  ])
  const wrapper = mount(Component as object, {
    props: { value: null, fetcher },
    global: { stubs: { Select: selectStub } },
  })
  await nextTick()
  await new Promise(r => setTimeout(r, 0))
  expect(fetcher).toHaveBeenCalled()
  expect(wrapper.find('.n-select-stub').attributes('data-options')).toBe('2')
  await wrapper.find('.n-select-stub').trigger('click')
  expect(wrapper.emitted('update:value')).toBeTruthy()
}

describe('Selector quartet', () => {
  it('TDictSelector renders and emits', async () => { await assertSelector(TDictSelector) })
  it('TRoleSelector renders and emits', async () => { await assertSelector(TRoleSelector) })
  it('TUserSelector renders and emits', async () => { await assertSelector(TUserSelector) })
  it('TTenantSelector renders and emits', async () => { await assertSelector(TTenantSelector) })

  it('search emits debounced refetch', async () => {
    const fetcher = vi.fn(async (_kw: string) => [])
    const wrapper = mount(TUserSelector, {
      props: { value: null, fetcher },
      global: { stubs: { Select: selectStub } },
    })
    await nextTick()
    wrapper.findComponent(selectStub).vm.$emit('search', 'alice')
    await new Promise(r => setTimeout(r, 320))
    expect(fetcher).toHaveBeenCalledWith('alice')
  })
})
