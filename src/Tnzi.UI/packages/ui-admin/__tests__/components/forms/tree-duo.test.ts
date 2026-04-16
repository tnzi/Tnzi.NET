import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TPermissionTree from '../../../src/components/forms/TPermissionTree.vue'
import TMenuTree from '../../../src/components/forms/TMenuTree.vue'

const treeStub = {
  name: 'Tree',
  props: ['data', 'checkedKeys', 'checkable', 'cascade', 'draggable', 'keyField', 'labelField', 'childrenField', 'blockLine'],
  emits: ['update:checkedKeys', 'drop'],
  template: '<div class="n-tree-stub" :data-count="data.length"></div>',
}

describe('TPermissionTree', () => {
  const data = [
    { id: 'perm-root', name: 'Root', children: [{ id: 'perm-1', name: 'P1' }] },
  ]

  it('renders NTree with passed data', () => {
    const wrapper = mount(TPermissionTree, {
      props: { data, checkedKeys: [] },
      global: { stubs: { Tree: treeStub } },
    })
    const stub = wrapper.find('.n-tree-stub')
    expect(stub.exists()).toBe(true)
    expect(stub.attributes('data-count')).toBe('1')
  })

  it('emits update:checkedKeys when tree changes selection', async () => {
    const wrapper = mount(TPermissionTree, {
      props: { data, checkedKeys: [] },
      global: { stubs: { Tree: treeStub } },
    })
    wrapper.findComponent(treeStub).vm.$emit('update:checkedKeys', ['perm-1', 'perm-2'])
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('update:checkedKeys')).toBeTruthy()
    expect(wrapper.emitted('update:checkedKeys')![0]).toEqual([['perm-1', 'perm-2']])
  })

  it('also emits change on selection update', async () => {
    const wrapper = mount(TPermissionTree, {
      props: { data, checkedKeys: [] },
      global: { stubs: { Tree: treeStub } },
    })
    wrapper.findComponent(treeStub).vm.$emit('update:checkedKeys', ['perm-1'])
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('change')).toBeTruthy()
  })
})

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
