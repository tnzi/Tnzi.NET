import { ref, type Ref } from 'vue'
import type { SelectOption as NaiveSelectOption } from 'naive-ui'
import { createFinanceBridge, CashFlowActivity, type AccountTreeDto } from '../../services/bridges/finance-bridge'
import type { PayrollBridge, SalaryComponentDto } from '../../services/bridges/payroll-bridge'
import { useAdminClient } from '../../plugin/client'

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

  const structures = lazy<SelectOption>(async () => {
    const page = await bridge.structures.fetch({ pageIndex: 1, pageSize: 200, filters: { isActive: true } })
    return page.items.map((s) => ({ label: s.name, value: s.id }))
  })

  // Full active-component list (structure line editor needs code/name/type).
  const components = lazy<SalaryComponentDto>(async () => {
    const page = await bridge.components.fetch({ pageIndex: 1, pageSize: 500, filters: { isActive: true } })
    return page.items
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
    cashAccountOptions: cashAccounts.options,
    ensureCashAccounts: cashAccounts.ensure,
    leafAccountOptions: leafAccounts.options,
    ensureLeafAccounts: leafAccounts.ensure,
  }
}
