import { describe, it, expect, vi } from 'vitest'
import { createFinanceOptionSources } from '../../../src/pages/finance/options'
import { createPayrollOptionSources } from '../../../src/pages/payroll/options'
import type { CrudPageQuery, CrudPageResult } from '../../../src/headless/useCrudPage'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn() }) }))
vi.mock('../../../src/services/bridges/finance-bridge', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../src/services/bridges/finance-bridge')>()),
  createFinanceBridge: () => ({ accounts: { tree: vi.fn(async () => []) } }),
}))

// The document editors render a static filterable NSelect over whatever the
// option loader returns. The backend clamps pageSize to 100 silently, so a
// loader that asked for 200 got the first 100 active customers and a tenant
// with 150 could not invoice customers 101-150 from the UI - the select just
// did not contain them, no error anywhere.

/** A paged server that honours at most 100 rows per page, whatever is asked. */
function clampedPages<T>(rows: T[]) {
  return vi.fn(async (query: CrudPageQuery): Promise<CrudPageResult<T>> => {
    const size = Math.min(query.pageSize, 100)
    const start = (query.pageIndex - 1) * size
    const items = rows.slice(start, start + size)
    const totalPages = Math.ceil(rows.length / size)
    return {
      items, totalCount: rows.length, pageIndex: query.pageIndex, pageSize: size, totalPages,
      hasPreviousPage: query.pageIndex > 1, hasNextPage: query.pageIndex < totalPages,
    }
  })
}

const many = (prefix: string, n: number) => Array.from({ length: n }, (_, i) => ({ id: `${prefix}${i + 1}`, name: `${prefix} ${i + 1}`, code: `${prefix}-${i + 1}` }))

describe('finance option sources', () => {
  it('customers / vendors / items load every active row past the server page clamp', async () => {
    const customers = clampedPages(many('c', 150))
    const vendors = clampedPages(many('v', 120))
    const items = clampedPages(many('i', 230))
    const bridge = { customers: { fetch: customers }, vendors: { fetch: vendors }, items: { fetch: items } }
    const sources = createFinanceOptionSources(bridge as never)

    await Promise.all([sources.ensureCustomers(), sources.ensureVendors(), sources.ensureItems()])

    expect(sources.customerOptions.value).toHaveLength(150)
    expect(sources.customerOptions.value.at(-1)).toEqual({ label: 'c 150', value: 'c150' })
    expect(sources.vendorOptions.value).toHaveLength(120)
    expect(sources.itemOptions.value).toHaveLength(230)
    // The active-only filter must reach every page, not just the first.
    for (const call of customers.mock.calls) expect(call[0].filters).toEqual({ isActive: true })
    expect(customers).toHaveBeenCalledTimes(2)
    expect(items).toHaveBeenCalledTimes(3)
  })
})

describe('payroll option sources', () => {
  it('structures / components / employees load every active row past the server page clamp', async () => {
    const structures = clampedPages(many('s', 110))
    const components = clampedPages(many('k', 260))
    const employees = clampedPages(many('e', 340))
    const bridge = { structures: { fetch: structures }, components: { fetch: components }, employees: { fetch: employees } }
    const sources = createPayrollOptionSources(bridge as never)

    await Promise.all([sources.ensureStructures(), sources.ensureComponents(), sources.ensureEmployees()])

    expect(sources.structureOptions.value).toHaveLength(110)
    expect(sources.componentList.value).toHaveLength(260)
    expect(sources.employeeOptions.value).toHaveLength(340)
    expect(sources.employeeOptions.value.at(-1)).toEqual({ label: 'e-340 · e 340', value: 'e340' })
    expect(employees).toHaveBeenCalledTimes(4)
  })
})
