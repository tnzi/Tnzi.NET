import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TPermissionMatrix from '../../src/components/forms/TPermissionMatrix.vue'
import type { FunctionModuleDto, ModuleFunctionDto } from '@tnzi/core/services/authorization'
import { PermissionCategory } from '@tnzi/core/services/authorization'

const modules: FunctionModuleDto[] = [
  { id: 'm1', code: 'identity', name: 'Identity', order: 1, isEnabled: true },
]

function fn(id: string, code: string, name: string, opts: Partial<ModuleFunctionDto> = {}): ModuleFunctionDto {
  return { id, code, name, moduleId: 'm1', isEnabled: true, order: 1, ...opts }
}

const functions: ModuleFunctionDto[] = [
  fn('p1', 'user.view', 'View Users'),
  fn('p2', 'user.create', 'Create Users'),
  fn('p3', 'user.update', 'Update Users'),
  fn('p4', 'user.delete', 'Delete Users'),
  fn('p5', 'session.view', 'View Sessions', { category: PermissionCategory.Technical }),
  fn('p6', 'ai.sql.execute', 'Execute AI SQL Queries', { category: PermissionCategory.Technical }),
  fn('p7', 'blog.publish', 'Publish Blog Posts'),
]

function mountMatrix(props: Partial<InstanceType<typeof TPermissionMatrix>['$props']> = {}) {
  return mount(TPermissionMatrix, {
    props: {
      modules,
      functionsByModule: new Map([['m1', functions]]),
      checkedIds: [],
      translate: (k: string) => k,
      // Most assertions inspect surface rows; sections default to a
      // collapsed overview, so tests opt into the expanded state unless
      // they exercise the collapse behaviour itself.
      defaultExpanded: true,
      ...props,
    },
  })
}

describe('TPermissionMatrix', () => {
  it('groups codes into surface rows with per-action cells', () => {
    const wrapper = mountMatrix()
    const rows = wrapper.findAll('.t-perm-matrix__surface-row')
    // user + session + ai.sql + blog (custom no-view code) = 4 surfaces.
    expect(rows.length).toBe(4)

    const userRow = rows.find((r) => r.text().includes('Users'))!
    // Four crud checkboxes on the user surface (view/create/update/delete)
    // plus the row-level tri-state one.
    expect(userRow.findAll('.n-checkbox').length).toBe(5)

    // ai.sql has only the execute action → lands in the special column.
    const sqlRow = rows.find((r) => r.text().includes('ai.sql'))!
    expect(sqlRow.text()).toContain('matrix.execute')
  })

  it('renders the technical badge on technical surfaces', () => {
    const wrapper = mountMatrix()
    const sessionRow = wrapper
      .findAll('.t-perm-matrix__surface-row')
      .find((r) => r.text().includes('Sessions'))!
    expect(sessionRow.find('.t-perm-matrix__badge').exists()).toBe(true)
  })

  it('row toggle checks every action of the surface', async () => {
    const wrapper = mountMatrix()
    const userRow = wrapper
      .findAll('.t-perm-matrix__surface-row')
      .find((r) => r.text().includes('Users'))!
    // First checkbox in the surface cell is the row tri-state toggle.
    await userRow.find('.t-perm-matrix__surface-cell .n-checkbox').trigger('click')
    const emitted = wrapper.emitted('update:checkedIds')!
    const ids = emitted[emitted.length - 1]![0] as string[]
    expect([...ids].sort()).toEqual(['p1', 'p2', 'p3', 'p4'])
  })

  it('grantable filtering renders blocked cells as hatch boxes and skips them in toggles', async () => {
    const wrapper = mountMatrix({
      grantableCodes: ['user.view', 'user.update'],
    })
    const userRow = wrapper
      .findAll('.t-perm-matrix__surface-row')
      .find((r) => r.text().includes('Users'))!
    // create + delete blocked → hatched boxes instead of checkboxes
    // (visually distinct from "not granted"); view/update stay toggleable.
    expect(userRow.findAll('.t-perm-matrix__hatch-box').length).toBe(2)

    await userRow.find('.t-perm-matrix__surface-cell .n-checkbox').trigger('click')
    const emitted = wrapper.emitted('update:checkedIds')!
    const ids = emitted[emitted.length - 1]![0] as string[]
    expect([...ids].sort()).toEqual(['p1', 'p3'])
  })

  it('keyword filters surfaces and hides empty modules', () => {
    const wrapper = mountMatrix({ keyword: 'session' })
    const rows = wrapper.findAll('.t-perm-matrix__surface-row')
    expect(rows.length).toBe(1)
    expect(rows[0]!.text()).toContain('Sessions')

    const none = mountMatrix({ keyword: 'no-such-surface' })
    expect(none.findAll('.t-perm-matrix__surface-row').length).toBe(0)
    expect(none.find('.t-perm-matrix__empty').exists()).toBe(true)
  })

  it('collapses to a module overview by default and expands on header click', async () => {
    const wrapper = mountMatrix({ defaultExpanded: false })
    // Overview: module row with granted count, no surface rows yet.
    expect(wrapper.findAll('.t-perm-matrix__surface-row').length).toBe(0)
    const moduleRow = wrapper.find('.t-perm-matrix__module-row')
    expect(moduleRow.find('.t-perm-matrix__module-count').text().replace(/\s/g, '')).toBe('0/7')

    await moduleRow.trigger('click')
    expect(wrapper.findAll('.t-perm-matrix__surface-row').length).toBe(4)

    await moduleRow.trigger('click')
    expect(wrapper.findAll('.t-perm-matrix__surface-row').length).toBe(0)
  })

  it('an active keyword force-expands collapsed sections', () => {
    const wrapper = mountMatrix({ defaultExpanded: false, keyword: 'user' })
    expect(wrapper.findAll('.t-perm-matrix__surface-row').length).toBe(1)
    expect(wrapper.find('.t-perm-matrix__surface-row').text()).toContain('Users')
  })

  it('column headers show catalogue-wide granted counts', () => {
    const wrapper = mountMatrix({ checkedIds: ['p1'] })
    // Two surfaces expose a view code (user.view + session.view), one granted.
    const viewHeader = wrapper.findAll('.t-perm-matrix__action-col')[0]!
    expect(viewHeader.find('.t-perm-matrix__col-sub').text().replace(/\s/g, '')).toContain('1/2')
  })

  it('module All / Clear buttons bulk-toggle the whole section', async () => {
    const wrapper = mountMatrix()
    const ops = wrapper.find('.t-perm-matrix__module-ops')
    const [allBtn, clearBtn] = ops.findAll('button')
    await allBtn!.trigger('click')
    let emitted = wrapper.emitted('update:checkedIds')!
    expect(([...emitted[emitted.length - 1]![0] as string[]]).sort()).toEqual([
      'p1', 'p2', 'p3', 'p4', 'p5', 'p6', 'p7',
    ])

    const checkedAll = mountMatrix({ checkedIds: ['p1', 'p2', 'p3', 'p4', 'p5', 'p6', 'p7'] })
    const ops2 = checkedAll.find('.t-perm-matrix__module-ops')
    await ops2.findAll('button')[1]!.trigger('click')
    emitted = checkedAll.emitted('update:checkedIds')!
    expect(emitted[emitted.length - 1]![0]).toEqual([])
  })

  it('tags module access codes as menu entries and pins them first', () => {
    const accessFns = [
      fn('a1', 'blog.view', 'View Blog'),
      fn('a2', 'blog.post.view', 'View Blog Posts'),
      fn('a3', 'blog.post.create', 'Create Blog Posts'),
    ]
    const wrapper = mountMatrix({ functionsByModule: new Map([['m1', accessFns]]) })
    const rows = wrapper.findAll('.t-perm-matrix__surface-row')
    expect(rows.length).toBe(2)
    // `blog` gates the sidebar group (parent prefix of blog.post) → pinned
    // first with the menu-entry tag; the entity surface carries no tag.
    expect(rows[0]!.classes()).toContain('is-access')
    expect(rows[0]!.text()).toContain('matrix.menuEntry')
    expect(rows[1]!.classes()).not.toContain('is-access')
  })

  it('sorts consumer modules before built-in and renders origin sections', () => {
    // Built-in listed FIRST in input + a lower order number - the matrix must
    // still surface the consumer module first and split the two behind section
    // sub-headers driven by the backend `isBuiltIn` flag.
    const builtin: FunctionModuleDto = {
      id: 'mb', code: 'identity', name: 'Identity', order: 1, isEnabled: true, isBuiltIn: true,
    }
    const consumer: FunctionModuleDto = {
      id: 'mc', code: 'acme', name: 'Acme', order: 5, isEnabled: true,
    }
    const wrapper = mount(TPermissionMatrix, {
      props: {
        modules: [builtin, consumer],
        functionsByModule: new Map([
          ['mb', [fn('b1', 'user.view', 'View Users')]],
          ['mc', [fn('c1', 'acme.blog.view', 'View Blog')]],
        ]),
        checkedIds: [],
        translate: (k: string) => k,
        defaultExpanded: false,
      },
    })

    // Both origins present → two section sub-headers, application first.
    const sections = wrapper.findAll('.t-perm-matrix__section-row')
    expect(sections.length).toBe(2)
    expect(sections[0]!.text()).toContain('matrix.section.app')
    expect(sections[1]!.text()).toContain('matrix.section.builtin')

    // Consumer module row precedes the built-in one despite input/order.
    const names = wrapper.findAll('.t-perm-matrix__module-name').map((n) => n.text())
    expect(names[0]).toBe('Acme')
    expect(names[1]).toBe('Identity')
  })

  it('omits origin sections when every module is built-in', () => {
    const wrapper = mountMatrix({
      modules: [{ id: 'm1', code: 'identity', name: 'Identity', order: 1, isEnabled: true, isBuiltIn: true }],
    })
    // A single origin (no consumer modules) → no lone section header.
    expect(wrapper.findAll('.t-perm-matrix__section-row').length).toBe(0)
  })

  it('applies label overrides for surfaces and module headers', () => {
    const wrapper = mountMatrix({
      labelOverrides: { user: '用户', 'module:identity': '身份管理' },
    })
    expect(wrapper.find('.t-perm-matrix__module-name').text()).toBe('身份管理')
    const userRow = wrapper
      .findAll('.t-perm-matrix__surface-row')
      .find((r) => r.text().includes('用户'))!
    expect(userRow.exists()).toBe(true)
  })

  it('renders the special column as a labelled checkbox (not a colour-only pill)', () => {
    const wrapper = mountMatrix()
    const sqlRow = wrapper
      .findAll('.t-perm-matrix__surface-row')
      .find((r) => r.text().includes('ai.sql'))!
    // Special actions (execute/assign/use) render as a checkbox with a visible
    // label so granted vs not is obvious at a glance - the old bare pill is gone.
    const special = sqlRow.find('.t-perm-matrix__special-check')
    expect(special.exists()).toBe(true)
    expect(special.classes()).toContain('is-execute')
    expect(sqlRow.find('.t-perm-matrix__special-pill').exists()).toBe(false)
  })

  it('readonly locks the whole matrix and renders everything granted', async () => {
    const wrapper = mountMatrix({ readonly: true, checkedIds: [] })
    const userRow = wrapper
      .findAll('.t-perm-matrix__surface-row')
      .find((r) => r.text().includes('Users'))!
    // Every checkbox reads as granted even with an empty checkedIds …
    const boxes = userRow.findAll('.n-checkbox')
    expect(boxes.length).toBe(5)
    expect(boxes.every((b) => b.classes().includes('n-checkbox--disabled'))).toBe(true)
    expect(userRow.findAll('.n-checkbox--checked').length).toBe(5)

    // … and clicks never mutate the selection.
    await userRow.find('.t-perm-matrix__surface-cell .n-checkbox').trigger('click')
    await userRow.find('.t-perm-matrix__cell').trigger('click')
    expect(wrapper.emitted('update:checkedIds')).toBeUndefined()
  })
})
