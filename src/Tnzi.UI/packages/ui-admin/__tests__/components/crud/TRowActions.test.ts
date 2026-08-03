import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import TRowActions from '../../../src/components/crud/TRowActions.vue'
import type { RowAction } from '../../../src/headless/row-actions'

type Row = { id: string; locked?: boolean }

const stubs = {
  Button: { name: 'Button', props: ['type', 'disabled'], template: '<button :data-type="type" :disabled="disabled" @click="$emit(\'click\')"><slot /></button>' },
  Popconfirm: { name: 'Popconfirm', template: '<div class="popconfirm"><slot name="trigger" /><slot /></div>' },
  Dropdown: { name: 'Dropdown', props: ['options'], template: '<div class="dropdown" :data-count="options.length"><slot /></div>' },
  SvgIcon: true,
}

function mountActions(actions: RowAction<Row>[], extra: Record<string, unknown> = {}, row: Row = { id: '1' }) {
  return mount(TRowActions, {
    props: { row, actions, ...extra } as Record<string, unknown>,
    global: { stubs },
  })
}

describe('TRowActions (declarative)', () => {
  it('renders all actions inline when count <= maxInline (default 2)', () => {
    const w = mountActions([{ key: 'edit', label: 'Edit' }, { key: 'delete', label: 'Delete' }])
    const btns = w.findAll('button')
    expect(btns).toHaveLength(2)
    expect(w.find('.dropdown').exists()).toBe(false)
  })

  it('collapses the tail into a More▾ dropdown when count > maxInline', () => {
    const w = mountActions([
      { key: 'a', label: 'A' },
      { key: 'b', label: 'B' },
      { key: 'c', label: 'C' },
    ])
    // 1 inline button (A) + the More trigger button
    expect(w.find('.dropdown').exists()).toBe(true)
    // dropdown holds the 2 overflow actions (B, C)
    expect(w.find('.dropdown').attributes('data-count')).toBe('2')
  })

  it('collapse=false renders every action inline (no dropdown)', () => {
    const w = mountActions(
      [{ key: 'a', label: 'A' }, { key: 'b', label: 'B' }, { key: 'c', label: 'C' }],
      { collapse: false },
    )
    expect(w.find('.dropdown').exists()).toBe(false)
    expect(w.findAll('button').length).toBe(3)
  })

  it('fires onClick for an inline action without confirm', async () => {
    const onClick = vi.fn()
    const w = mountActions([{ key: 'go', label: 'Go', onClick }])
    await w.find('button').trigger('click')
    expect(onClick).toHaveBeenCalledWith({ id: '1' })
  })

  it('wraps a confirm action in a popconfirm instead of firing directly', () => {
    const onClick = vi.fn()
    const w = mountActions([{ key: 'del', label: 'Delete', confirm: true, onClick }])
    expect(w.find('.popconfirm').exists()).toBe(true)
  })

  it('hides actions whose show() returns false', () => {
    const actions: RowAction<Row>[] = [
      { key: 'unlock', label: 'Unlock', show: (r) => r.locked === true },
      { key: 'lock', label: 'Lock', show: (r) => r.locked !== true },
    ]
    const unlocked = mountActions(actions, {}, { id: '1', locked: false })
    expect(unlocked.text()).toContain('Lock')
    expect(unlocked.text()).not.toContain('Unlock')
    const locked = mountActions(actions, {}, { id: '1', locked: true })
    expect(locked.text()).toContain('Unlock')
  })

  it('translates i18n-key labels and built-in keys via the translate prop', () => {
    const translate = (k: string) => ({ 'admin.crud.edit': '编辑', 'actions.run': '运行' })[k] ?? k
    const w = mountActions([{ key: 'edit' }, { key: 'run', label: 'actions.run' }], { translate })
    expect(w.text()).toContain('编辑')
    expect(w.text()).toContain('运行')
  })

  it('applies the disabled predicate per row', () => {
    const w = mountActions([{ key: 'go', label: 'Go', disabled: () => true }])
    expect(w.find('button').attributes('disabled')).toBeDefined()
  })
})
