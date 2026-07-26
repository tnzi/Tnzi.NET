<template>
  <TCrudPage :state="crud" :all-columns="columns" :title="title" :row-actions="rowActions" :translate="t">
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="bankAccountFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="fieldRenderers"
        :columns="2"
        :translate="t"
      />
    </template>
  </TCrudPage>

  <!-- Set next check number - per-account register advance (jump = new book). -->
  <TDetailHost :state="checkDetail" :title="t('setCheck.title')" :width="420" :footer="false" :translate="t">
    <div class="fin-bank-acct__set-check">
      <p class="fin-bank-acct__set-check-hint">{{ t('setCheck.hint') }}</p>
      <NInputNumber v-model:value="nextCheckValue" :min="1" class="fin-bank-acct__set-check-input" />
      <div class="fin-bank-acct__set-check-actions">
        <NButton size="small" @click="checkDetail.close()">{{ t('setCheck.cancel') }}</NButton>
        <NButton size="small" type="primary" :loading="savingCheck" :disabled="savingCheck" @click="submitNextCheck">
          {{ t('setCheck.submit') }}
        </NButton>
      </div>
    </div>
  </TDetailHost>
</template>

<script setup lang="ts">
import { h, ref, watch } from 'vue'
import { NButton, NInput, NInputNumber } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import {
  createFinanceBridge,
  BankNumberScheme,
  CheckStockType,
  CheckLayout,
  type BankAccountDto,
  type CreateBankAccountDto,
  type UpdateBankAccountDto,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer, type FieldRenderContext } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { createFinanceOptionSources } from './options'
import { buildBankAccountColumns, bankAccountFormSchema, type BankAccountRow } from './bank-account-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.bankAccounts')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const columns = buildBankAccountColumns(t)

function str(v: unknown): string | null {
  return typeof v === 'string' && v.trim() ? v.trim() : null
}
function upper(v: unknown): string | null {
  const s = str(v)
  return s ? s.toUpperCase() : null
}
function num(v: unknown, fallback: number): number {
  return v == null || v === '' ? fallback : Number(v)
}

function commonFields(d: Record<string, unknown>) {
  const scheme = (d.scheme as BankNumberScheme) ?? BankNumberScheme.UsAba
  const isCa = scheme === BankNumberScheme.CaEft
  return {
    name: String(d.name ?? '').trim(),
    bankName: str(d.bankName),
    scheme,
    routingNumber: isCa ? null : str(d.routingNumber),
    institutionNumber: isCa ? str(d.institutionNumber) : null,
    transitNumber: isCa ? str(d.transitNumber) : null,
    currency: upper(d.currency),
    checkStockType: (d.checkStockType as CheckStockType) ?? CheckStockType.PrePrinted,
    checkLayout: (d.checkLayout as CheckLayout) ?? CheckLayout.Voucher,
    offsetXMm: num(d.offsetXMm, 0),
    offsetYMm: num(d.offsetYMm, 0),
    feedProviderKey: str(d.feedProviderKey),
    externalAccountId: str(d.externalAccountId),
    eftOriginatorId: str(d.eftOriginatorId),
    eftOriginatorName: str(d.eftOriginatorName),
  }
}

function toCreatePayload(d: Record<string, unknown>): CreateBankAccountDto {
  return {
    accountId: String(d.accountId ?? ''),
    // Account number is optional; only send it when provided (write-only).
    accountNumber: str(d.accountNumber) ?? undefined,
    nextCheckNumber: num(d.nextCheckNumber, 1),
    ...commonFields(d),
  }
}

function toUpdatePayload(d: Record<string, unknown>): UpdateBankAccountDto {
  // Leave accountNumber blank to keep the current (encrypted) number unchanged.
  return {
    accountNumber: str(d.accountNumber) ?? undefined,
    ...commonFields(d),
  }
}

const crud = useCrudPage<BankAccountRow>({
  pageId: 'finance.bankAccounts',
  permission: 'finance.bankAccount',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.bankAccounts.fetch(q),
  loadDetailById: (id) => bridge.bankAccounts.getById(String(id)),
  createData: (d) => bridge.bankAccounts.create(toCreatePayload(d)),
  updateData: (id, d) => bridge.bankAccounts.update(String(id), toUpdatePayload(d)),
  deleteData: (ids) => bridge.bankAccounts.delete(ids.map(String)),
})

const title = 'tnzi.admin.modules.finance.bankAccounts.title'

/**
 * Whether this deployment can store account numbers at all (the backend refuses
 * to when `Finance:Encryption:EncryptionKey` is unset - an account number must
 * never land in the database unencrypted).
 *
 * Fail-open: only an explicit `false` disables the field. If the capability read
 * fails or the backend predates the endpoint, leave the field usable and let the
 * server's 400 be the wall - wrongly disabling it would block a legitimate write
 * on a properly configured deployment.
 */
const canStoreAccountNumber = ref(true)

async function loadCapabilities() {
  try {
    const capabilities = await bridge.bankAccounts.capabilities()
    canStoreAccountNumber.value = capabilities?.canStoreAccountNumber !== false
  } catch {
    canStoreAccountNumber.value = true
  }
}

const fieldRenderers = {
  'finance-account': selectRenderer(() => sources.fundsAccountOptions.value, { placeholder: t('form.accountPlaceholder'), clearable: false }),
  // Say "you cannot store one here" up front instead of letting the user type an
  // account number and eat a 400 on save.
  'finance-account-number': (ctx: FieldRenderContext) =>
    h('div', { class: 'fin-bank-acct__acct-number' }, [
      h(NInput, {
        value: (ctx.value as string | null) ?? null,
        disabled: ctx.readonly || !canStoreAccountNumber.value,
        placeholder: canStoreAccountNumber.value
          ? ctx.translate(ctx.item.placeholderKey, '')
          : t('form.accountNumberUnavailablePlaceholder'),
        'onUpdate:value': (v: unknown) => ctx.onUpdate(v),
      }),
      !canStoreAccountNumber.value
        ? h('span', { class: 'fin-bank-acct__hint' }, t('form.accountNumberUnavailableHint'))
        : null,
    ]),
}

watch(
  () => crud.formModal.visible.value,
  (open) => {
    if (open) {
      void sources.ensureFundsAccounts()
      void loadCapabilities()
    }
  },
  { immediate: true },
)

// ── Set next check number modal ─────────────────────────────────
const checkDetail = useDetail<BankAccountDto>({
  mode: 'modal',
  url: 'setCheck',
  loadData: (id) => bridge.bankAccounts.getById(String(id)),
})
const nextCheckValue = ref<number | null>(1)
const savingCheck = ref(false)

watch(
  () => checkDetail.data.value?.id,
  () => {
    nextCheckValue.value = checkDetail.data.value?.nextCheckNumber ?? 1
  },
)

async function submitNextCheck() {
  const id = checkDetail.data.value?.id
  if (!id || nextCheckValue.value == null) return
  savingCheck.value = true
  try {
    await bridge.bankAccounts.setNextCheckNumber(String(id), Number(nextCheckValue.value))
    message.success(t('setCheck.success'))
    checkDetail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    savingCheck.value = false
  }
}

const rowActions: RowAction<BankAccountRow>[] = [
  editAction(crud),
  {
    key: 'setCheck',
    label: 'actions.setCheck',
    type: 'primary',
    show: () => can('finance.bankAccount.update'),
    onClick: (row) => checkDetail.open('view', String(row.id ?? '')),
  },
  deleteAction(crud, { confirm: 'confirmDelete' }),
]
</script>

<style scoped>
.fin-bank-acct__acct-number {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.fin-bank-acct__hint {
  font-size: 12px;
  line-height: 1.5;
  color: var(--tnzi-text-3, #999);
}

.fin-bank-acct__set-check {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-bank-acct__set-check-hint {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-text-3, #999);
}

.fin-bank-acct__set-check-input {
  width: 100%;
}

.fin-bank-acct__set-check-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}
</style>
