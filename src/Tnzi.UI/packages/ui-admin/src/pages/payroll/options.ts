import { ref, type Ref } from 'vue'
import type { SelectOption as NaiveSelectOption } from 'naive-ui'
import { createFinanceBridge, CashFlowActivity, type AccountTreeDto } from '../../services/bridges/finance-bridge'
import type { PayrollBridge, SalaryComponentDto } from '../../services/bridges/payroll-bridge'
import { useAdminClient } from '../../plugin/client'
import { fetchAllPages } from '../../headless/fetchAllPages'

export type SelectOption = NaiveSelectOption

/**
 * Lazily-loaded option sources shared by the payroll pages (active structures,
 * salary components, and the funds accounts the pay step draws from). Each
 * `ensureXxx` loads once per page instance and swallows failures into an empty
 * list (the page still mounts). Cash accounts come from the Finance chart of
 * accounts (Payroll hard-depends on Finance), filtered to postable
 * CashEquivalent leaves - the backend re-validates on pay.
 */
export function createPayrollOptionSources(bridge: PayrollBridge) {
  const financeBridge = createFinanceBridge({ client: useAdminClient() })

  function lazy<T>(load: () => Promise<T[]>): { options: Ref<T[]>; ensure: () => Promise<void>; refresh: () => Promise<void> } {
    const options = ref<T[]>([]) as Ref<T[]>
    let loaded = false
    const refresh = async () => {
      try {
        options.value = await load()
        loaded = true
      } catch {
        options.value = []
      }
    }
    return {
      options,
      ensure: async () => {
        if (loaded) return
        await refresh()
      },
      // 强制重取(country pack 播种等写路径让缓存失效后立即可用)
      refresh,
    }
  }

  // All three page until the server runs out: the backend clamps pageSize to
  // 100 silently, so the former `pageSize: 200 / 500` calls stopped at the
  // 100th structure / component / employee with no error anywhere.
  const structures = lazy<SelectOption>(async () => {
    const rows = await fetchAllPages((q) => bridge.structures.fetch(q), { filters: { isActive: true } })
    return rows.map((s) => ({ label: s.name, value: s.id }))
  })

  // Full active-component list (structure line editor needs code/name/type).
  const components = lazy<SalaryComponentDto>(async () =>
    fetchAllPages((q) => bridge.components.fetch(q), { filters: { isActive: true } }),
  )

  // 一次性输入的录入面要按人选：只列在册员工，后端会再核他在不在这个批次里。
  const employees = lazy<SelectOption>(async () => {
    const rows = await fetchAllPages((q) => bridge.employees.fetch(q), { filters: { isActive: true } })
    return rows.map((e) => ({ label: `${e.code} · ${e.name}`, value: e.id }))
  })

  const cashAccounts = lazy<SelectOption>(async () => {
    const tree = await financeBridge.accounts.tree(false)
    const options: SelectOption[] = []
    const walk = (nodes: AccountTreeDto[]) => {
      for (const node of nodes) {
        if (!node.isGroup && node.isActive && node.cashFlowActivity === CashFlowActivity.CashEquivalent) {
          options.push({ label: `${node.code} ${node.name}`, value: node.id })
        }
        walk(node.children ?? [])
      }
    }
    walk(tree)
    return options
  })

  // Every active postable leaf account (component expense / liability pickers).
  const leafAccounts = lazy<SelectOption>(async () => {
    const tree = await financeBridge.accounts.tree(false)
    const options: SelectOption[] = []
    const walk = (nodes: AccountTreeDto[]) => {
      for (const node of nodes) {
        if (!node.isGroup && node.isActive) {
          options.push({ label: `${node.code} ${node.name}`, value: node.id })
        }
        walk(node.children ?? [])
      }
    }
    walk(tree)
    return options
  })

  return {
    structureOptions: structures.options,
    ensureStructures: structures.ensure,
    componentList: components.options,
    ensureComponents: components.ensure,
    refreshComponents: components.refresh,
    employeeOptions: employees.options,
    ensureEmployees: employees.ensure,
    cashAccountOptions: cashAccounts.options,
    ensureCashAccounts: cashAccounts.ensure,
    leafAccountOptions: leafAccounts.options,
    ensureLeafAccounts: leafAccounts.ensure,
  }
}
