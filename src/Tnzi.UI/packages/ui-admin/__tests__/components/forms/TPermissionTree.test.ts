import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'

// Capture the props the inner NTree receives + a hook to fire its update event.
const captured: Record<string, unknown> & { emitChecked?: (k: unknown[]) => void } = {}

vi.mock('naive-ui', () => ({
  NTree: defineComponent({
    name: 'NTree',
    props: [
      'data', 'checkedKeys', 'selectedKeys', 'checkable', 'selectable',
      'cascade', 'checkStrategy', 'blockLine', 'defaultExpandAll', 'pattern',
      'keyField', 'labelField', 'childrenField',
    ],
    emits: ['update:checkedKeys', 'update:selectedKeys'],
    setup(props, { emit }) {
      Object.assign(captured, props)
      captured.emitChecked = (k: unknown[]) => emit('update:checkedKeys', k)
      return () => h('div', { class: 'stub-ntree' })
    },
  }),
}))

import TPermissionTree from '../../../src/components/forms/TPermissionTree.vue'

describe('TPermissionTree', () => {
  it('applies assignment-mode defaults (checkable cascade all block-line expand-all)', () => {
    mount(TPermissionTree, { props: { data: [{ key: 'a', label: 'A' }] } })
    expect(captured.checkable).toBe(true)
    expect(captured.cascade).toBe(true)
    expect(captured.checkStrategy).toBe('all')
    expect(captured.blockLine).toBe(true)
    expect(captured.defaultExpandAll).toBe(true)
    expect(captured.selectable).toBe(false)
  })

  it('forwards data + checkedKeys to NTree', () => {
    mount(TPermissionTree, { props: { data: [{ key: 'a', label: 'A' }], checkedKeys: ['a'] } })
    expect(captured.data).toEqual([{ key: 'a', label: 'A' }])
    expect(captured.checkedKeys).toEqual(['a'])
  })

  it('re-emits checked changes coerced to string[]', () => {
    const w = mount(TPermissionTree, { props: { data: [] } })
    captured.emitChecked?.([1, 'b'])
    expect(w.emitted('update:checkedKeys')?.[0]).toEqual([['1', 'b']])
    expect(w.emitted('change')?.[0]).toEqual([['1', 'b']])
  })
})
