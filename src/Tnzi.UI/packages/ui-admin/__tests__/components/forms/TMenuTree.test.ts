import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TMenuTree from '../../../src/components/forms/TMenuTree.vue'

// The permission-assignment grid has its own dedicated spec
// (TPermissionMatrix.test.ts); this file covers the draggable
// menu-reorder tree only.
const treeStub = {
  name: 'Tree',
  props: ['data', 'draggable', 'keyField', 'labelField', 'childrenField', 'blockLine'],
  emits: ['drop'],
  template: '<div class="n-tree-stub" :data-count="data.length"></div>',
}

describe('TMenuTree', () => {
  const data = [
    { key: 'a', label: 'A' },
    { key: 'b', label: 'B' },
  ]

  it('renders menu tree with draggable=true', () => {
    const wrapper = mount(TMenuTree, {
      props: { data },
      global: { stubs: { Tree: treeStub } },
    })
    expect(wrapper.find('.n-tree-stub').exists()).toBe(true)
    expect(wrapper.findComponent(treeStub).props('draggable')).toBe(true)
  })

  it('emits reorder with new structure on drop', async () => {
    const wrapper = mount(TMenuTree, {
      props: { data },
      global: { stubs: { Tree: treeStub } },
    })
    wrapper.findComponent(treeStub).vm.$emit('drop', {
      node: { key: 'b', label: 'B' },
      dragNode: { key: 'a', label: 'A' },
      dropPosition: 'before',
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('reorder')).toBeTruthy()
  })
})
