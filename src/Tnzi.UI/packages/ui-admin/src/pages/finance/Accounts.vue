<template>
  <TCrudPage :state="crud" :all-columns="accountColumns" :title="title" :row-actions="rowActions" :translate="t">
    <template #toolbarRight>
      <NPopconfirm @positive-click="seedDefault">
        <template #trigger>
          <NButton size="small">{{ t('actions.seedDefault') }}</NButton>
        </template>
        {{ t('seedConfirm') }}
      </NPopconfirm>
    </template>
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="accountFormSchema"
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
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge, AccountRootType, AccountSystemRole, CashFlowActivity, type AccountTreeDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer, type FieldRenderContext } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { accountColumns, accountFormSchema, type AccountRow } from './account-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.accounts')
const message = useSafeMessage()

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

const crud = useCrudPage<AccountRow>({
  pageId: 'finance.accounts',
  columns: accountColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.accounts.fetch(q),
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
const rowActions: RowAction<AccountRow>[] = [editAction(crud), deleteAction(crud)]

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
// neither), so grey them out when editing — otherwise the user could change a
// disabled-on-the-server field and see it silently ignored.
const isEditingAccount = () => crud.formModal.mode.value === 'edit'

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
