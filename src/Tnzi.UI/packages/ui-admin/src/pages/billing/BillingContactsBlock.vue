<template>
  <div class="t-billing-contacts" data-testid="billing-contacts">
    <div class="flex items-center justify-between gap-8px">
      <span class="font-600 text-14px">{{ t('contacts.title') }}</span>
      <NButton v-if="account?.canManage && !editing" size="small" quaternary data-testid="billing-contacts-edit" @click="startEdit">
        {{ t('contacts.edit') }}
      </NButton>
    </div>
    <template v-if="!editing">
      <p v-if="emails.length" class="m-0 text-13px" data-testid="billing-contacts-list">{{ emails.join(', ') }}</p>
      <p v-else class="m-0 text-13px text-muted" data-testid="billing-contacts-fallback">{{ t('contacts.fallback') }}</p>
    </template>
    <template v-else>
      <NInput
        v-model:value="draft"
        type="textarea"
        :autosize="{ minRows: 2, maxRows: 6 }"
        :placeholder="t('contacts.placeholder')"
        data-testid="billing-contacts-input"
      />
      <p class="m-0 text-12px text-muted">{{ t('contacts.hint') }}</p>
      <div class="flex justify-end gap-8px">
        <NButton size="small" :disabled="saving" @click="editing = false">{{ t('actions.cancel') }}</NButton>
        <NButton size="small" type="primary" :loading="saving" data-testid="billing-contacts-save" @click="save">{{ t('contacts.save') }}</NButton>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
/**
 * Who receives the account's receipts, invoices, reminders and dunning email.
 * Without any billing contact the email goes to the account of the person who
 * paid (or started the subscription), which is said here so nobody wonders why
 * the manager's own inbox gets them. Billing managers edit the list; the PUT
 * overwrites the whole profile, so the other fields are sent back unchanged.
 */
import { computed, onMounted, ref } from 'vue'
import { NButton, NInput } from 'naive-ui'
import type { BillingAccountDto } from '@tnzi/core/services/payment'
import { useSafeMessage } from '../_shared/safe-message'
import { billingT as t, useBillingBridge } from './billing-context'

const bridge = useBillingBridge()
const message = useSafeMessage()

const account = ref<BillingAccountDto | null>(null)
const editing = ref(false)
const saving = ref(false)
const draft = ref('')

const emails = computed(() => account.value?.billingEmails ?? [])

onMounted(async () => {
  // Optional: without the account read the history still lists the documents.
  account.value = await bridge.getAccount().catch(() => null)
})

function startEdit(): void {
  draft.value = emails.value.join('\n')
  editing.value = true
}

async function save(): Promise<void> {
  const current = account.value
  if (!current || saving.value) return
  saving.value = true
  try {
    const billingEmails = draft.value
      .split(/[\n,;]+/)
      .map((e) => e.trim())
      .filter(Boolean)
    account.value = await bridge.updateAccount({
      displayName: current.displayName ?? null,
      address: current.address ?? null,
      taxId: current.taxId ?? null,
      billingEmails,
    })
    editing.value = false
    message.success(t('contacts.saved'))
  } catch (err) {
    message.error(t('contacts.saveFailed', { error: err instanceof Error ? err.message : String(err) }))
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.t-billing-contacts {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 12px;
}
</style>
