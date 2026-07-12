import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useCrudPage } from '../../src/headless/useCrudPage'
import { editAction, deleteAction, viewAction } from '../../src/headless/rowActions'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import type { ColumnDef } from '../../src/headless/useColumnSettings'

interface Row {
  id: string
  name: string
}

const columns: ColumnDef[] = [{ key: 'name', title: 'Name' }]
const row: Row = { id: '1', name: 'Alice' }

function makeCrud(permission?: string | { create?: string; update?: string; delete?: string }) {
  return useCrudPage<Row>({
    pageId: 'perm-test',
    columns,
    rowKey: (r) => r.id,
    fetchData: async () => ({ items: [row], totalCount: 1, pageIndex: 1, pageSize: 20 }),
    createData: async (d) => ({ ...row, ...d }),
    updateData: async (_id, d) => ({ ...row, ...d }),
    deleteData: async () => {},
    autoLoad: false,
    retryFetch: 0,
    permission,
  })
}

function signIn(permissions: string[]): void {
  useAdminAuthStore().setUserInfo({
    id: 'u1',
    username: 'tester',
    roles: [],
    permissions,
  })
}

describe('useCrudPage permission gating', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('without a permission config, callbacks alone drive can*', () => {
    signIn([])
    const crud = makeCrud()
    expect(crud.canCreate).toBe(true)
    expect(crud.canUpdate).toBe(true)
    expect(crud.canDelete).toBe(true)
  })

  it('string base derives the three write codes and denies a view-only user', () => {
    signIn(['user.view'])
    const crud = makeCrud('user')
    expect(crud.canCreate).toBe(false)
    expect(crud.canUpdate).toBe(false)
    expect(crud.canDelete).toBe(false)
  })

  it('grants exactly the actions whose codes the user holds', () => {
    signIn(['user.view', 'user.update'])
    const crud = makeCrud('user')
    expect(crud.canCreate).toBe(false)
    expect(crud.canUpdate).toBe(true)
    expect(crud.canDelete).toBe(false)
  })

  it('permission codes match case-insensitively', () => {
    signIn(['USER.CREATE'])
    const crud = makeCrud('user')
    expect(crud.canCreate).toBe(true)
  })

  it('object form gates only the named actions', () => {
    signIn(['x.special'])
    const crud = makeCrud({ update: 'x.special', delete: 'x.other' })
    expect(crud.canCreate).toBe(true) // ungated action stays callback-driven
    expect(crud.canUpdate).toBe(true)
    expect(crud.canDelete).toBe(false)
  })

  it('super user bypasses every action gate', () => {
    signIn([])
    useAdminAuthStore().setSuperUser(true)
    const crud = makeCrud('user')
    expect(crud.canCreate).toBe(true)
    expect(crud.canUpdate).toBe(true)
    expect(crud.canDelete).toBe(true)
  })

  it('fails open before the user is loaded, then re-evaluates reactively', () => {
    const crud = makeCrud('user')
    // userInfo === null → fail-open (mirrors the sidebar; backend still 403s).
    expect(crud.canCreate).toBe(true)

    signIn(['user.view'])
    // Getters re-read the computed — permission load flips the affordance off.
    expect(crud.canCreate).toBe(false)

    signIn(['user.view', 'user.create'])
    expect(crud.canCreate).toBe(true)
  })

  it('editAction/deleteAction hide per row when the action permission is missing', () => {
    signIn(['user.view'])
    const crud = makeCrud('user')
    expect(editAction(crud).show!(row)).toBe(false)
    expect(deleteAction(crud).show!(row)).toBe(false)
    // view stays ungated — the page itself is already view-gated by the route.
    expect(viewAction(crud).show).toBeUndefined()

    signIn(['user.view', 'user.update', 'user.delete'])
    expect(editAction(crud).show!(row)).toBe(true)
    expect(deleteAction(crud).show!(row)).toBe(true)
  })

  it('caller-supplied show composes with the permission gate (AND)', () => {
    signIn(['user.update', 'user.delete'])
    const crud = makeCrud('user')
    expect(editAction(crud, { show: () => false }).show!(row)).toBe(false)
    expect(editAction(crud, { show: () => true }).show!(row)).toBe(true)

    signIn([])
    // Permission missing → caller show can never resurrect the action.
    expect(editAction(crud, { show: () => true }).show!(row)).toBe(false)
  })
})
