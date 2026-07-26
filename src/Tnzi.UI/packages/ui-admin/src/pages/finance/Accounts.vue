<template>
  <TCrudPage :state="crud" :all-columns="accountColumns" :title="title" :row-actions="rowActions" :translate="t">
    <template #toolbarRight>
      <NPopconfirm v-if="can('finance.account.create')" @positive-click="seedDefault">
        <template #trigger>
          <NButton size="small">{{ t('actions.seedDefault') }}</NButton>
        </template>
        {{ t('seedConfirm') }}
      </NPopconfirm>
    </template>
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="accountFormSchema"
        :sections="accountFormSections"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="fieldRenderers"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import { NButton, NPopconfirm, NSelect, NSwitch } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import {
  createFinanceBridge,
  AccountRootType,
  AccountSystemRole,
  CashFlowActivity,
  type AccountTreeDto,
  type FinancePagedResult,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer, type FieldRenderContext } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import {
  accountColumns,
  accountFormSchema,
  accountFormSections,
  isSystemRoleAccount,
  type AccountRow,
} from './account-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.accounts')
const message = useSafeMessage()
const { can } = usePermissionGuard()

/** Form model → backend payload (empty-string sentinel selects → null, blanks → null). */
function toPayload(d: Record<string, unknown>) {
  const currency = typeof d.currency === 'string' && d.currency.trim() ? d.currency.trim().toUpperCase() : null
  return {
    code: String(d.code ?? ''),
    name: String(d.name ?? ''),
    description: (d.description as string | null) ?? null,
    rootType: (d.rootType as AccountRootType) || AccountRootType.Asset,
    subType: typeof d.subType === 'string' && d.subType.trim() ? d.subType.trim() : null,
    parentId: (d.parentId as string | null) || null,
    isGroup: d.isGroup === true,
    currency,
    systemRole: (d.systemRole as AccountSystemRole) || null,
    cashFlowActivity: (d.cashFlowActivity as CashFlowActivity) || null,
    isActive: d.isActive !== false,
  }
}

/**
 * Merge each page's base-currency balances in from the dedicated endpoint (one
 * set-based call per page, group accounts skipped since only leaves carry lines).
 * A balance failure must not blank the account list, so it degrades to no column
 * value rather than propagating.
 */
async function withBalances(page: FinancePagedResult<AccountRow>): Promise<FinancePagedResult<AccountRow>> {
  const ids = page.items.filter((a) => !a.isGroup && a.id).map((a) => String(a.id))
  if (ids.length === 0) return page
  try {
    const balances = await bridge.accounts.balances(ids)
    const byId = new Map(balances.map((b) => [b.accountId, b.balance]))
    return { ...page, items: page.items.map((a) => ({ ...a, balance: byId.get(String(a.id ?? '')) })) }
  } catch {
    return page
  }
}

// Flatten the account tree depth-first, tagging each row with its depth for the name-cell indent
// (the chart of accounts is small - the whole tree renders as one page, parent/child order preserved).
function flattenTree(nodes: AccountTreeDto[], depth: number, into: AccountRow[]) {
  for (const node of nodes) {
    const { children, ...rest } = node
    into.push({ ...(rest as unknown as AccountRow), _depth: depth })
    if (children?.length) flattenTree(children, depth + 1, into)
  }
}

const crud = useCrudPage<AccountRow>({
  pageId: 'finance.accounts',
  permission: 'finance.account',
  columns: accountColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: async () => {
    // Render the CoA as an indented hierarchy (tree endpoint) rather than a flat paginated list.
    const tree = await bridge.accounts.tree(true)
    const items: AccountRow[] = []
    flattenTree(tree, 0, items)
    return withBalances({ items, pageIndex: 1, pageSize: Math.max(items.length, 1), totalCount: items.length, totalPages: 1, hasPreviousPage: false, hasNextPage: false })
  },
  createData: async (d) => {
    const created = await bridge.accounts.create(toPayload(d))
    await loadParentOptions()
    return created
  },
  updateData: async (id, d) => {
    const updated = await bridge.accounts.update(String(id), toPayload(d))
    await loadParentOptions()
    return updated
  },
  deleteData: async (ids) => {
    await bridge.accounts.delete(ids.map(String))
    await loadParentOptions()
  },
})

const title = 'tnzi.admin.modules.finance.accounts.title'
// Delete is dropped for role-bearing accounts: postings resolve those BY ROLE, so
// the backend 409s. Clearing the role (via Edit) releases the account.
const rowActions: RowAction<AccountRow>[] = [
  editAction(crud),
  deleteAction(crud, { show: (row) => !isSystemRoleAccount(row) }),
]

// Group-account options for the parent picker (flattened tree, groups only).
const parentOptions = ref<Array<{ label: string; value: string }>>([])

function flattenGroups(nodes: AccountTreeDto[], depth: number, into: Array<{ label: string; value: string }>) {
  for (const node of nodes) {
    if (node.isGroup) {
      into.push({ label: `${'　'.repeat(depth)}${node.code} ${node.name}`, value: node.id })
      flattenGroups(node.children ?? [], depth + 1, into)
    }
  }
}

async function loadParentOptions() {
  try {
    const tree = await bridge.accounts.tree(true)
    const options: Array<{ label: string; value: string }> = []
    flattenGroups(tree, 0, options)
    parentOptions.value = options
  } catch {
    parentOptions.value = []
  }
}

// rootType/isGroup are immutable after creation (UpdateAccountDto carries
// neither), so grey them out when editing - otherwise the user could change a
// disabled-on-the-server field and see it silently ignored.
const isEditingAccount = () => crud.formModal.mode.value === 'edit'

// A role-bearing account cannot be deactivated (the posting pipeline resolves it
// by role AND requires it active, so the backend 409s). Mirror that gate on the
// switch rather than letting the user flip it and eat the error - and key it on
// the form's CURRENT role, so clearing the role in the same edit re-enables it,
// exactly like the backend guard (which keys on the resulting state).
const editedSystemRole = () => (crud.formModal.formData.value as Record<string, unknown> | undefined)?.systemRole
const isRoleBearing = () => {
  const role = editedSystemRole()
  return typeof role === 'string' && role !== ''
}

const fieldRenderers = {
  'finance-parent': selectRenderer(() => parentOptions.value, { placeholder: t('form.parentIdPlaceholder') }),
  'finance-root-type': (ctx: FieldRenderContext) =>
    h(NSelect, {
      value: (ctx.value as string | null) ?? null,
      options: (ctx.item.options ?? []).map((o) => ({ label: ctx.translate(o.labelKey, o.label), value: o.value })),
      disabled: ctx.readonly || isEditingAccount(),
      'onUpdate:value': (v: unknown) => ctx.onUpdate(v),
    }),
  'finance-is-group': (ctx: FieldRenderContext) =>
    h(NSwitch, {
      value: ctx.value === true,
      disabled: ctx.readonly || isEditingAccount(),
      'onUpdate:value': (v: unknown) => ctx.onUpdate(v),
    }),
  'finance-is-active': (ctx: FieldRenderContext) =>
    h('div', { class: 'fin-accounts__is-active' }, [
      h(NSwitch, {
        value: ctx.value !== false,
        disabled: ctx.readonly || isRoleBearing(),
        'onUpdate:value': (v: unknown) => ctx.onUpdate(v),
      }),
      isRoleBearing() ? h('span', { class: 'fin-accounts__hint' }, t('form.isActiveSystemRoleHint')) : null,
    ]),
}

async function seedDefault() {
  try {
    const count = await bridge.accounts.seedDefault()
    message.success(t('seedSuccess', { count }))
    await Promise.all([crud.refresh(), loadParentOptions()])
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

onMounted(() => {
  void loadParentOptions()
})
</script>

<style scoped>
.fin-accounts__is-active {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.fin-accounts__hint {
  font-size: 12px;
  color: var(--tnzi-text-3, #999);
}
</style>
