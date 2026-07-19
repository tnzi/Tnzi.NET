import { describe, it, expect, vi } from 'vitest'
import { createPayrollBridge } from '../../../src/services/bridges/payroll-bridge'
import type { HttpClient } from '@tnzi/core/http'

/** Mock HttpClient wrapping every response in the standard success envelope. */
function mockClient() {
  const ok = <T>(data: T) => ({ data, succeeded: true, success: true, code: 200, message: '' })
  return {
    get: vi.fn(async (url: string) =>
      url.includes('/payslips') || url.includes('/assignments') || url.endsWith('/country-packs')
        ? ok([])
        : ok({ items: [{ id: 'x1' }], totalCount: 1, pageIndex: 1, pageSize: 20 }),
    ),
    post: vi.fn(async () => ok({ id: 'x1' })),
    put: vi.fn(async () => ok({ id: 'x1' })),
    delete: vi.fn(async () => ok(undefined)),
  } as unknown as HttpClient & { get: ReturnType<typeof vi.fn>; post: ReturnType<typeof vi.fn>; put: ReturnType<typeof vi.fn>; delete: ReturnType<typeof vi.fn> }
}

describe('payroll-bridge', () => {
  it('exposes every resource section', () => {
    const bridge = createPayrollBridge({ client: mockClient() })
    expect(typeof bridge.employees.fetch).toBe('function')
    expect(typeof bridge.components.fetch).toBe('function')
    expect(typeof bridge.structures.fetch).toBe('function')
    expect(typeof bridge.brackets.fetch).toBe('function')
    expect(typeof bridge.runs.fetch).toBe('function')
    expect(typeof bridge.countryPacks.fetch).toBe('function')
  })

  it('employees.fetch calls GET /admin/payroll/employees and returns a paged list', async () => {
    const client = mockClient()
    const bridge = createPayrollBridge({ client })
    const result = await bridge.employees.fetch({ pageIndex: 1, pageSize: 20, searchText: 'a', filters: { isActive: true } })
    expect(client.get).toHaveBeenCalledWith('/admin/payroll/employees', expect.anything())
    expect(result.items).toHaveLength(1)
    expect(result.totalCount).toBe(1)
  })

  it('employees.ensureVendor POSTs the ensure-vendor sub-route', async () => {
    const client = mockClient()
    const bridge = createPayrollBridge({ client })
    await bridge.employees.ensureVendor('e1')
    expect(client.post).toHaveBeenCalledWith('/admin/payroll/employees/e1/ensure-vendor')
  })

  it('brackets.resolve GETs resolve with code + asOf params', async () => {
    const client = mockClient()
    const bridge = createPayrollBridge({ client })
    await bridge.brackets.resolve('CN_IIT', '2026-07-01')
    expect(client.get).toHaveBeenCalledWith('/admin/payroll/brackets/resolve', { params: { code: 'CN_IIT', asOf: '2026-07-01' } })
  })

  it('runs lifecycle actions hit their nested routes', async () => {
    const client = mockClient()
    const bridge = createPayrollBridge({ client })
    await bridge.runs.calculate('r1')
    await bridge.runs.post('r1')
    await bridge.runs.voidRun('r1')
    await bridge.runs.pay('r1', { paymentAccountId: 'a1', paymentDate: '2026-08-05', employeeIds: null })
    expect(client.post).toHaveBeenCalledWith('/admin/payroll/runs/r1/calculate')
    expect(client.post).toHaveBeenCalledWith('/admin/payroll/runs/r1/post')
    expect(client.post).toHaveBeenCalledWith('/admin/payroll/runs/r1/void')
    expect(client.post).toHaveBeenCalledWith('/admin/payroll/runs/r1/pay', expect.objectContaining({ paymentAccountId: 'a1' }))
  })

  it('runs.updatePayslipInputs PUTs the nested payslip inputs route', async () => {
    const client = mockClient()
    const bridge = createPayrollBridge({ client })
    await bridge.runs.updatePayslipInputs('r1', 'ps1', { workedDays: 20 })
    expect(client.put).toHaveBeenCalledWith('/admin/payroll/runs/r1/payslips/ps1/inputs', { workedDays: 20 })
  })

  it('countryPacks.seed POSTs the seed route', async () => {
    const client = mockClient()
    const bridge = createPayrollBridge({ client })
    await bridge.countryPacks.seed('US')
    expect(client.post).toHaveBeenCalledWith('/admin/payroll/country-packs/US/seed')
  })

  it('a client-less bridge returns stubs that reject on call', async () => {
    const bridge = createPayrollBridge()
    await expect(bridge.employees.fetch({ pageIndex: 1, pageSize: 20 })).rejects.toThrow()
    await expect(bridge.runs.fetch({ pageIndex: 1, pageSize: 20 })).rejects.toThrow()
  })
})
