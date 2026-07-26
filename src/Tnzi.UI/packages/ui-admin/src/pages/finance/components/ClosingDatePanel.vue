<template>
  <div class="fin-lock">
    <div class="fin-lock__head">
      <div class="fin-lock__state">
        <TSvgIcon :icon="locked ? 'mdi:lock-outline' : 'mdi:lock-open-variant-outline'" :size="18" :class="locked ? 'fin-lock__icon--on' : 'fin-lock__icon--off'" />
        <div>
          <div class="fin-lock__value">
            <template v-if="locked">{{ t('closingDate.closedThrough') }} <strong>{{ formatAccountingDate(lock?.closingDate) }}</strong></template>
            <template v-else>{{ t('closingDate.notClosed') }}</template>
          </div>
          <div class="fin-lock__hint">{{ locked ? t('closingDate.lockedHint') : t('closingDate.openHint') }}</div>
        </div>
      </div>

      <div class="fin-lock__actions">
        <NTag v-if="lock?.isPasswordProtected" size="small" type="warning" :bordered="false">
          <template #icon><TSvgIcon icon="mdi:key-outline" :size="13" /></template>
          {{ t('closingDate.passwordSet') }}
        </NTag>
        <NButton v-if="canEdit" size="small" :type="locked ? 'default' : 'primary'" @click="openEditor">
          {{ locked ? t('closingDate.change') : t('closingDate.set') }}
        </NButton>
      </div>
    </div>

    <p v-if="lock?.note" class="fin-lock__note">
      <TSvgIcon icon="mdi:note-text-outline" :size="13" /> {{ lock.note }}
      <span v-if="lock.lastChangedTime" class="fin-lock__stamp">· {{ formatAccountingDate(lock.lastChangedTime) }}</span>
    </p>

    <TModalShell v-model:show="editing" :title="t('closingDate.editTitle')" :width="520">
      <NForm label-placement="top" size="small">
        <NFormItem :label="t('closingDate.field')">
          <NDatePicker v-model:value="draftDate" type="date" clearable class="fin-lock__picker" :is-date-disabled="isFuture" />
        </NFormItem>
        <p class="fin-lock__explain">{{ t('closingDate.explain') }}</p>

        <NFormItem v-if="lock?.isPasswordProtected" :label="t('closingDate.currentPassword')">
          <NInput
            v-model:value="password"
            type="password"
            show-password-on="click"
            :input-props="{ autocomplete: 'off', name: 'tnzi-ledger-lock-current' }"
            :placeholder="t('closingDate.currentPasswordHint')"
          />
        </NFormItem>

        <NFormItem :label="t('closingDate.newPassword')">
          <!-- `new-password` + a non-guessable name: without it the browser
               autofills a saved credential here and silently sets a closing-date
               password the operator never chose (and then cannot guess). -->
          <NInput
            v-model:value="newPassword"
            type="password"
            show-password-on="click"
            :input-props="{ autocomplete: 'new-password', name: 'tnzi-ledger-lock-new' }"
            :placeholder="passwordPlaceholder"
          />
        </NFormItem>
        <p class="fin-lock__explain">{{ t('closingDate.passwordExplain') }}</p>

        <NFormItem :label="t('closingDate.note')">
          <NInput v-model:value="note" :placeholder="t('closingDate.notePlaceholder')" />
        </NFormItem>
      </NForm>

      <template #footer>
        <NButton size="small" @click="editing = false">{{ t('closingDate.cancel') }}</NButton>
        <NButton size="small" type="primary" :loading="saving" @click="save">{{ t('closingDate.save') }}</NButton>
      </template>
    </TModalShell>
  </div>
</template>

<script setup lang="ts">
/**
 * The rolling closing date, sitting above the fiscal-year list.
 *
 * The two locks belong on the same screen because the operator's question is
 * one question ("can this period still be touched?"), but they are NOT the
 * same control: a fiscal year locks a whole range and is closed once a year,
 * while this date is advanced every month after the books are reconciled.
 *
 * The password is deliberately presented as a guard on *changing the date*,
 * not as a per-transaction override: moving the line back, fixing, and moving
 * it forward leaves three audited events, which is a better trail than a
 * silent per-entry bypass.
 */
import { computed, onMounted, ref } from 'vue'
import { NButton, NDatePicker, NForm, NFormItem, NInput, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TModalShell from '../../../components/overlay/TModalShell.vue'
import { formatAccountingDate, isoDateToLocalTs, tsToIsoDate } from '../../../utils/finance-format'
import type { FinanceBridge, LedgerLockDto } from '../../../services/bridges/finance-bridge'

const props = defineProps<{
  bridge: FinanceBridge
  canEdit: boolean
  t: (key: string) => string
}>()

const emit = defineEmits<{ changed: [] }>()

const lock = ref<LedgerLockDto | null>(null)
const editing = ref(false)
const saving = ref(false)
const draftDate = ref<number | null>(null)
const password = ref('')
const newPassword = ref('')
const note = ref('')
const error = ref<string | null>(null)

const locked = computed(() => Boolean(lock.value?.closingDate))

const passwordPlaceholder = computed(() =>
  lock.value?.isPasswordProtected ? props.t('closingDate.newPasswordChange') : props.t('closingDate.newPasswordSet'),
)

/** A closing date in the future would block ordinary current-period posting. */
function isFuture(ts: number): boolean {
  return ts > Date.now()
}

async function load() {
  try {
    lock.value = await props.bridge.fiscalYears.getClosingDate()
  } catch {
    // Read failure must not blank the fiscal-year list underneath: leave the
    // panel in its "unknown" state rather than asserting "not closed".
    lock.value = null
  }
}

function openEditor() {
  draftDate.value = lock.value?.closingDate ? isoDateToLocalTs(lock.value.closingDate) : null
  password.value = ''
  newPassword.value = ''
  note.value = lock.value?.note ?? ''
  error.value = null
  editing.value = true
}

async function save() {
  saving.value = true
  try {
    lock.value = await props.bridge.fiscalYears.setClosingDate({
      closingDate: draftDate.value === null ? null : tsToIsoDate(draftDate.value),
      password: password.value || null,
      // `null` leaves it alone; the field is only sent when the user typed one.
      newPassword: newPassword.value === '' ? null : newPassword.value,
      note: note.value || null,
    })
    editing.value = false
    emit('changed')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
    throw e
  } finally {
    saving.value = false
  }
}

onMounted(load)
defineExpose({ reload: load })
</script>

<style scoped>
.fin-lock {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 12px 14px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 6px);
  background: var(--tnzi-container-bg);
}

.fin-lock__head {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.fin-lock__state {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.fin-lock__icon--on {
  color: var(--tnzi-warning);
}

.fin-lock__icon--off {
  color: var(--tnzi-base-text-muted);
}

.fin-lock__value {
  font-size: 14px;
}

.fin-lock__hint,
.fin-lock__explain,
.fin-lock__note {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  margin: 0;
}

.fin-lock__explain {
  margin: -6px 0 10px;
}

.fin-lock__actions {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 8px;
}

.fin-lock__note {
  display: flex;
  align-items: center;
  gap: 6px;
}

.fin-lock__stamp {
  font-variant-numeric: tabular-nums;
}

.fin-lock__picker {
  width: 100%;
}

@media (max-width: 767px) {
  .fin-lock__actions {
    margin-left: 0;
    width: 100%;
  }
}
</style>
