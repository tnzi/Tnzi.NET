<template>
  <div class="fin-remit">
    <div class="fin-remit__head">
      <span class="fin-remit__title">{{ t('remitTo.title') }}</span>
      <NButton v-if="canManage" size="small" type="primary" @click="openAdd">
        <template #icon><TSvgIcon icon="mdi:plus" :size="16" /></template>
        {{ t('remitTo.add') }}
      </NButton>
    </div>

    <TResponsiveTable
      :columns="columns"
      :data="accounts"
      :row-key="(r: PartyBankAccountDto) => r.id"
      size="small"
      mobile="scroll"
      :pagination="false"
      :bordered="false"
      :loading="loading"
      :row-actions="rowActions"
      :translate="t"
    />
    <p v-if="!loading && accounts.length === 0" class="fin-remit__empty">{{ t('remitTo.empty') }}</p>

    <TModalShell v-model:show="formShow" :title="t(editingId ? 'remitTo.edit' : 'remitTo.add')" :width="480">
      <TFormSchemaRenderer :schema="formSchema" :model="form" :translate="t" />
      <template #footer>
        <NButton size="small" @click="formShow = false">{{ t('remitTo.cancel') }}</NButton>
        <NButton size="small" type="primary" :loading="saving" :disabled="saving" @click="submit">
          {{ t('remitTo.save') }}
        </NButton>
      </template>
    </TModalShell>
  </div>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { h, ref, watch } from 'vue'
import { NButton, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import type { RowAction } from '../../../headless/row-actions'
import { TModalShell } from '@tnzi/ui'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import TFormSchemaRenderer from '../../_shared/form-schema'
import type { FormSchemaItem } from '../../_shared/form-schema'
import { useSafeMessage } from '../../_shared/safe-message'
import {
  BankNumberScheme,
  BankAccountType,
  type FinanceBridge,
  type FinancePartyType,
  type PartyBankAccountDto,
  type SavePartyBankAccountDto,
} from '../../../services/bridges/finance-bridge'

const props = defineProps<{
  bridge: FinanceBridge
  partyType: FinancePartyType
  partyId: string
  /** Page-scoped translator (keys resolve under `remitTo.*`). */
  t: (key: string, params?: Record<string, unknown>) => string
  /** Gate add/edit/delete on the party's write permission. */
  canManage: boolean
}>()

const message = useSafeMessage()

const accounts = ref<PartyBankAccountDto[]>([])
const loading = ref(false)
const formShow = ref(false)
const saving = ref(false)
const editingId = ref<string | null>(null)
const form = ref<Record<string, unknown>>({})

async function load() {
  if (!props.partyId) return
  loading.value = true
  try {
    accounts.value = await props.bridge.partyBankAccounts.byParty(props.partyType, props.partyId)
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    loading.value = false
  }
}

watch(() => props.partyId, () => void load(), { immediate: true })

function maskedCell(masked?: string | null): string {
  if (!masked) return EMPTY_DASH
  return masked.length <= 4 ? `••••${masked}` : masked
}

const SCHEME_LABEL: Record<string, string> = {
  [BankNumberScheme.UsAba]: 'remitTo.scheme.usAba',
  [BankNumberScheme.CaEft]: 'remitTo.scheme.caEft',
}
const TYPE_LABEL: Record<string, string> = {
  [BankAccountType.Checking]: 'remitTo.type.checking',
  [BankAccountType.Savings]: 'remitTo.type.savings',
}

const columns: DataTableColumns<PartyBankAccountDto> = [
  { key: 'label', title: props.t('remitTo.columns.label'), minWidth: 140, render: (r) => r.label ?? r.bankName ?? EMPTY_DASH },
  { key: 'bankName', title: props.t('remitTo.columns.bankName'), minWidth: 120, render: (r) => r.bankName ?? EMPTY_DASH },
  { key: 'scheme', title: props.t('remitTo.columns.scheme'), width: 100, render: (r) => props.t(SCHEME_LABEL[String(r.scheme)] ?? '') || String(r.scheme) },
  { key: 'accountNumberMasked', title: props.t('remitTo.columns.accountNumber'), width: 120, render: (r) => maskedCell(r.accountNumberMasked) },
  { key: 'accountType', title: props.t('remitTo.columns.type'), width: 100, render: (r) => props.t(TYPE_LABEL[String(r.accountType)] ?? '') || String(r.accountType) },
  {
    key: 'isDefault',
    title: props.t('remitTo.columns.default'),
    width: 90,
    render: (r) => (r.isDefault ? h(TStatusBadge, { value: 'default', type: 'success', label: props.t('remitTo.default') }) : EMPTY_DASH),
  },
]

// 声明式行操作（C5）：破坏性删除 + 改资金路由的设默认均带确认；手搓 h(NButton) 无确认已收口。
const rowActions: RowAction<PartyBankAccountDto>[] = [
  {
    key: 'setDefault',
    label: 'remitTo.setDefault',
    show: (r) => props.canManage && !r.isDefault,
    confirm: 'remitTo.setDefaultConfirm',
    onClick: (r) => void setDefault(r),
  },
  {
    key: 'edit',
    label: 'remitTo.editAction',
    show: () => props.canManage,
    onClick: (r) => openEdit(r),
  },
  {
    key: 'delete',
    label: 'remitTo.deleteAction',
    type: 'error',
    show: () => props.canManage,
    confirm: 'remitTo.deleteConfirm',
    onClick: (r) => void remove(r),
  },
]

function isCaEft(model: Record<string, unknown>): boolean {
  return model.scheme === BankNumberScheme.CaEft
}
function isUsAba(model: Record<string, unknown>): boolean {
  return model.scheme !== BankNumberScheme.CaEft
}

const formSchema: FormSchemaItem[] = [
  { key: 'label', labelKey: 'remitTo.form.label', label: 'Label', type: 'text' },
  { key: 'bankName', labelKey: 'remitTo.form.bankName', label: 'Bank Name', type: 'text' },
  {
    key: 'scheme',
    labelKey: 'remitTo.form.scheme',
    label: 'Number Scheme',
    type: 'select',
    options: [
      { value: BankNumberScheme.UsAba, label: 'US ABA', labelKey: 'remitTo.scheme.usAba' },
      { value: BankNumberScheme.CaEft, label: 'Canada EFT', labelKey: 'remitTo.scheme.caEft' },
    ],
  },
  { key: 'routingNumber', labelKey: 'remitTo.form.routingNumber', label: 'Routing Number', type: 'text', visible: isUsAba },
  { key: 'institutionNumber', labelKey: 'remitTo.form.institutionNumber', label: 'Institution Number', type: 'text', visible: isCaEft },
  { key: 'transitNumber', labelKey: 'remitTo.form.transitNumber', label: 'Transit Number', type: 'text', visible: isCaEft },
  { key: 'accountNumber', labelKey: 'remitTo.form.accountNumber', label: 'Account Number', type: 'text', placeholderKey: 'remitTo.form.accountNumberPlaceholder' },
  {
    key: 'accountType',
    labelKey: 'remitTo.form.accountType',
    label: 'Account Type',
    type: 'select',
    options: [
      { value: BankAccountType.Checking, label: 'Checking', labelKey: 'remitTo.type.checking' },
      { value: BankAccountType.Savings, label: 'Savings', labelKey: 'remitTo.type.savings' },
    ],
  },
  { key: 'currency', labelKey: 'remitTo.form.currency', label: 'Currency', type: 'text' },
  { key: 'isDefault', labelKey: 'remitTo.form.isDefault', label: 'Default', type: 'switch' },
  { key: 'isActive', labelKey: 'remitTo.form.isActive', label: 'Active', type: 'switch' },
  { key: 'notes', labelKey: 'remitTo.form.notes', label: 'Notes', type: 'textarea' },
]

function openAdd() {
  editingId.value = null
  form.value = { scheme: BankNumberScheme.UsAba, accountType: BankAccountType.Checking, isActive: true, isDefault: false }
  formShow.value = true
}

function openEdit(row: PartyBankAccountDto) {
  editingId.value = row.id
  // Account number is never echoed back - leave blank to keep the current one.
  form.value = {
    label: row.label,
    bankName: row.bankName,
    scheme: row.scheme,
    routingNumber: row.routingNumber,
    institutionNumber: row.institutionNumber,
    transitNumber: row.transitNumber,
    accountNumber: '',
    accountType: row.accountType,
    currency: row.currency,
    isDefault: row.isDefault,
    isActive: row.isActive,
    notes: row.notes,
  }
  formShow.value = true
}

function toPayload(): SavePartyBankAccountDto {
  const d = form.value
  const scheme = (d.scheme as BankNumberScheme) ?? BankNumberScheme.UsAba
  const isCa = scheme === BankNumberScheme.CaEft
  const str = (v: unknown) => (typeof v === 'string' && v.trim() ? v.trim() : null)
  return {
    partyType: props.partyType,
    partyId: props.partyId,
    label: str(d.label),
    bankName: str(d.bankName),
    scheme,
    routingNumber: isCa ? null : str(d.routingNumber),
    institutionNumber: isCa ? str(d.institutionNumber) : null,
    transitNumber: isCa ? str(d.transitNumber) : null,
    accountNumber: str(d.accountNumber) ?? undefined,
    accountType: (d.accountType as BankAccountType) ?? BankAccountType.Checking,
    currency: str(d.currency)?.toUpperCase() ?? null,
    isDefault: d.isDefault === true,
    isActive: d.isActive !== false,
    notes: str(d.notes),
  }
}

async function submit() {
  saving.value = true
  try {
    const payload = toPayload()
    if (editingId.value) {
      await props.bridge.partyBankAccounts.update(editingId.value, payload)
    } else {
      await props.bridge.partyBankAccounts.save(payload)
    }
    message.success(props.t('remitTo.saved'))
    formShow.value = false
    await load()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    saving.value = false
  }
}

async function setDefault(row: PartyBankAccountDto) {
  try {
    await props.bridge.partyBankAccounts.setDefault(row.id)
    await load()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

async function remove(row: PartyBankAccountDto) {
  try {
    await props.bridge.partyBankAccounts.delete(row.id)
    message.success(props.t('remitTo.deleted'))
    await load()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}
</script>

<style scoped>
.fin-remit {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-remit__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.fin-remit__title {
  font-size: 14px;
  font-weight: 600;
}

.fin-remit__empty {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-text-3, #999);
  text-align: center;
}
</style>
