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

  function flattenFundsLeaves(nodes: AccountTreeDto[], into: SelectOption[], roles: Record<string, string>) {
    for (const node of nodes) {
      if (!node.isGroup && node.isActive && node.cashFlowActivity === CashFlowActivity.CashEquivalent) {
        into.push({ label: `${node.code} ${node.name}`, value: node.id })
        if (node.systemRole) roles[String(node.systemRole)] = node.id
      }
      flattenFundsLeaves(node.children ?? [], into, roles)
    }
  }

  /**
   * `systemRole` → account id, for the funds accounts above.
   *
   * A page that needs "the undeposited-funds account" must ask the tree for it.
   * The only other handle is the option `label`, which is a localised
   * `code name` string - matching a role out of it works on an English chart of
   * accounts and silently finds nothing on any other. Filled by the same load
   * that builds `fundsAccountOptions`, so it costs no extra request.
   */
  const fundsAccountRoles = ref<Record<string, string>>({})

  // Cash / bank funds accounts only (CashEquivalent) - bank account profiles
  // and bank-feed selection require a funds account, not any leaf.
  const fundsAccounts = lazy<SelectOption>(async () => {
    const tree = await bridge.accounts.tree(false)
    const options: SelectOption[] = []
    const roles: Record<string, string> = {}
    flattenFundsLeaves(tree, options, roles)
    fundsAccountRoles.value = roles
    return options
  })

  // Expense-rooted leaves - the categories a spend can be coded to. Offering
  // the whole leaf list here (assets, income, equity) invites miscoding, and
  // "which account does a coffee go to" is the single most repeated decision
  // in the reconcile flow.
  const expenseAccounts = lazy<SelectOption>(async () => {
    const tree = await bridge.accounts.tree(false)
    const options: SelectOption[] = []
    const walk = (nodes: AccountTreeDto[]) => {
      for (const node of nodes) {
        if (!node.isGroup && node.isActive && String(node.rootType) === 'Expense') {
          options.push({ label: `${node.code} ${node.name}`, value: node.id })
        }
        walk(node.children ?? [])
      }
    }
    walk(tree)
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

  /**
   * Cheque layouts for the bank-account form.
   *
   * The label carries the geometry the operator actually chooses on ("3 per
   * page", "cheque on the bottom") because the template NAME is a slug and two
   * of the shipped layouts differ only in where the cheque band sits. Inactive
   * templates are dropped: selecting one would make every print fail.
   */
  const checkTemplates = lazy<SelectOption>(async () => {
    const templates = await bridge.checks.templates()
    return templates
      .filter((t) => t.isActive)
      .map((t) => ({
        label: t.checksPerPage > 1 ? `${t.displayName} (${t.checksPerPage}/page)` : t.displayName,
        value: t.name,
      }))
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
    checkTemplateOptions: checkTemplates.options,
    ensureCheckTemplates: checkTemplates.ensure,
    fundsAccountRoles,
    expenseAccountOptions: expenseAccounts.options,
    ensureExpenseAccounts: expenseAccounts.ensure,
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
