import { ref, type Ref } from 'vue'
import type { SelectOption as NaiveSelectOption } from 'naive-ui'
import { CashFlowActivity, type AccountTreeDto, type FinanceBridge, type TaxRateDto } from '../../services/bridges/finance-bridge'

/** naive-ui 的 SelectOption 别名（保证 NSelect :options 直接可绑）。 */
export type SelectOption = NaiveSelectOption

/**
 * Lazily-loaded option sources shared by the finance pages (leaf accounts,
 * parties, items, tax codes/rates). Each `ensureXxx` loads once per page
 * instance and swallows failures into an empty list (the page still mounts).
 */
export function createFinanceOptionSources(bridge: FinanceBridge) {
  function lazy<T>(load: () => Promise<T[]>): { options: Ref<T[]>; ensure: () => Promise<void> } {
    const options = ref<T[]>([]) as Ref<T[]>
    let loaded = false
    return {
      options,
      ensure: async () => {
        if (loaded) return
        try {
          options.value = await load()
          loaded = true
        } catch {
          options.value = []
        }
      },
    }
  }

  function flattenLeaves(nodes: AccountTreeDto[], into: SelectOption[]) {
    for (const node of nodes) {
      if (!node.isGroup && node.isActive) {
        into.push({ label: `${node.code} ${node.name}`, value: node.id })
      }
      flattenLeaves(node.children ?? [], into)
    }
  }

  const leafAccounts = lazy<SelectOption>(async () => {
    const tree = await bridge.accounts.tree(false)
    const options: SelectOption[] = []
    flattenLeaves(tree, options)
    return options
  })

  function flattenFundsLeaves(nodes: AccountTreeDto[], into: SelectOption[]) {
    for (const node of nodes) {
      if (!node.isGroup && node.isActive && node.cashFlowActivity === CashFlowActivity.CashEquivalent) {
        into.push({ label: `${node.code} ${node.name}`, value: node.id })
      }
      flattenFundsLeaves(node.children ?? [], into)
    }
  }

  // Cash / bank funds accounts only (CashEquivalent) — bank account profiles
  // and bank-feed selection require a funds account, not any leaf.
  const fundsAccounts = lazy<SelectOption>(async () => {
    const tree = await bridge.accounts.tree(false)
    const options: SelectOption[] = []
    flattenFundsLeaves(tree, options)
    return options
  })

  const customers = lazy<SelectOption>(async () => {
    const page = await bridge.customers.fetch({ pageIndex: 1, pageSize: 200, filters: { isActive: true } })
    return page.items.map((c) => ({ label: c.name, value: c.id }))
  })

  const vendors = lazy<SelectOption>(async () => {
    const page = await bridge.vendors.fetch({ pageIndex: 1, pageSize: 200, filters: { isActive: true } })
    return page.items.map((v) => ({ label: v.name, value: v.id }))
  })

  const items = lazy<SelectOption>(async () => {
    const page = await bridge.items.fetch({ pageIndex: 1, pageSize: 200, filters: { isActive: true } })
    return page.items.map((i) => ({ label: i.code ? `${i.code} ${i.name}` : i.name, value: i.id }))
  })

  const taxCodes = lazy<SelectOption>(async () => {
    const codes = await bridge.taxes.codes()
    return codes.filter((c) => c.isActive).map((c) => ({ label: c.name, value: c.id }))
  })

  const rates = lazy<TaxRateDto>(async () => bridge.taxes.rates())

  const agencies = lazy<SelectOption>(async () => {
    const list = await bridge.taxes.agencies()
    return list.filter((a) => a.isActive).map((a) => ({ label: a.name, value: a.id }))
  })

  return {
    leafAccountOptions: leafAccounts.options,
    ensureLeafAccounts: leafAccounts.ensure,
    fundsAccountOptions: fundsAccounts.options,
    ensureFundsAccounts: fundsAccounts.ensure,
    customerOptions: customers.options,
    ensureCustomers: customers.ensure,
    vendorOptions: vendors.options,
    ensureVendors: vendors.ensure,
    itemOptions: items.options,
    ensureItems: items.ensure,
    taxCodeOptions: taxCodes.options,
    ensureTaxCodes: taxCodes.ensure,
    rateOptions: rates.options,
    ensureRates: rates.ensure,
    agencyOptions: agencies.options,
    ensureAgencies: agencies.ensure,
  }
}
