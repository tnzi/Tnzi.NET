<template>
  <TDetailBlock :title="t('sections.paymentMethods')" icon="mdi:credit-card-outline">
    <template v-if="subscription" #titleExtra>
      <THint :content="t('card.renewalHint')" :size="13" />
    </template>
    <template v-if="canManage" #actions>
      <NButton size="small" data-testid="billing-add-card" @click="openSetup(false)">
        {{ t('actions.addCard') }}
      </NButton>
    </template>

    <!-- Only when the card renewals are charged to is not one of the rows below; otherwise that row says so. -->
    <div v-if="renewalLine" class="t-billing-cards__renewal" data-testid="billing-renewal-card">
      <span>{{ renewalLine }}</span>
      <NButton v-if="canReplace" size="small" quaternary data-testid="billing-update-card" @click="openSetup(true)">
        {{ t('actions.updateCard') }}
      </NButton>
    </div>

    <NAlert v-if="loadError" type="error" :show-icon="true" data-testid="billing-methods-error">
      {{ t('card.loadFailed', { error: loadError }) }}
    </NAlert>
    <div v-else-if="loading && !methods.length" class="text-13px text-muted">{{ t('loading') }}</div>
    <TEmpty v-else-if="!methods.length" :text="t('card.empty')" icon="mdi:credit-card-off-outline" />
    <ul v-else class="t-billing-cards" data-testid="billing-cards">
      <li v-for="m in methods" :key="m.id" class="t-billing-cards__row" :data-testid="`billing-card-${m.id}`">
        <TSvgIcon icon="mdi:credit-card-outline" :size="20" />
        <div class="flex flex-col min-w-0 flex-1">
          <span class="font-500">{{ methodLabel(m) }}</span>
          <span class="text-12px text-muted">{{ expiryText(m) }}</span>
        </div>
        <NTag v-if="isRenewal(m)" size="small" type="info" :bordered="false" data-testid="billing-card-renewal-tag">
          {{ t('card.renewal') }}
        </NTag>
        <NTag v-if="m.isDefault" size="small" type="success" :bordered="false">{{ t('card.default') }}</NTag>
        <NTag v-if="m.isExpired" size="small" type="error" :bordered="false">{{ t('card.expired') }}</NTag>
        <template v-if="canManage">
          <NButton
            v-if="canReplace && isRenewal(m)"
            size="small"
            quaternary
            data-testid="billing-update-card"
            @click="openSetup(true)"
          >
            {{ t('actions.updateCard') }}
          </NButton>
          <NButton
            v-if="!m.isDefault"
            size="small"
            quaternary
            :loading="busyId === m.id"
            :data-testid="`billing-default-${m.id}`"
            @click="setDefault(m)"
          >
            {{ t('actions.setDefault') }}
          </NButton>
          <NPopconfirm @positive-click="remove(m)">
            <template #trigger>
              <NButton size="small" quaternary type="error" :disabled="busyId === m.id" :data-testid="`billing-remove-${m.id}`">
                {{ t('actions.remove') }}
              </NButton>
            </template>
            {{ isRenewal(m) ? t('card.removeRenewalConfirm') : t('card.removeConfirm') }}
          </NPopconfirm>
        </template>
      </li>
    </ul>

    <BillingCardSetupModal
      v-model:show="setupOpen"
      :title="replacing ? t('card.title') : undefined"
      :subscription-id="replacing ? (subscription?.id ?? null) : null"
      @saved="afterChange"
    />
  </TDetailBlock>
</template>

<script setup lang="ts">
/**
 * Billing: the account's saved cards, as one block of a billing section.
 *
 * Billing managers (the server's `canManage`) add a card, choose the default and remove cards; other readers see
 * the list only. Given the subscription in force, the block also says which card its renewals are charged to and
 * replaces that card (setup, then attach to the subscription). That card is a property of the subscription, not
 * the account default: making another card the default does not move an existing subscription to it, which is
 * why the renewal card is marked on its own.
 */
import { computed, onMounted, ref } from 'vue'
import { NAlert, NButton, NPopconfirm, NTag } from 'naive-ui'
import { TEmpty, THint, TSvgIcon } from '@tnzi/ui'
import type { StoredPaymentMethodDto, SubscriptionDto } from '@tnzi/core/services/payment'
import TDetailBlock from '../../components/detail/TDetailBlock.vue'
import { useSafeMessage } from '../_shared/safe-message'
import BillingCardSetupModal from './BillingCardSetupModal.vue'
import { billingT as t, isPending, useBillingBridge } from './billing-context'

const props = withDefaults(defineProps<{ canManage: boolean; subscription?: SubscriptionDto | null }>(), {
  subscription: null,
})

/** A card was added, replaced, made default or removed: the subscription it may belong to has to be read again. */
const emit = defineEmits<{ changed: [] }>()

const bridge = useBillingBridge()
const message = useSafeMessage()

const methods = ref<StoredPaymentMethodDto[]>([])
const loading = ref(false)
const loaded = ref(false)
const loadError = ref<string | null>(null)
const setupOpen = ref(false)
const replacing = ref(false)
const busyId = ref<string | null>(null)

// A sign-up whose first payment is still open takes its card through "finish payment", not here.
const canReplace = computed(() => props.canManage && !!props.subscription && !isPending(props.subscription))

function isRenewal(m: StoredPaymentMethodDto): boolean {
  return !!props.subscription?.storedPaymentMethodId && m.id === props.subscription.storedPaymentMethodId
}

const renewalLine = computed(() => {
  const s = props.subscription
  if (!s || !loaded.value || methods.value.some(isRenewal)) return null
  if (!s.hasPaymentMethod) return t('card.renewalNone')
  const card = s.paymentMethodLast4 ? `${s.paymentMethodBrand || t('card.card')} **** ${s.paymentMethodLast4}` : t('card.onFile')
  return t('card.renewalLine', { card })
})

function methodLabel(m: StoredPaymentMethodDto): string {
  if (m.last4) return `${m.brand || t('card.card')} **** ${m.last4}`
  return m.accountLabel || m.brand || t('card.card')
}

function expiryText(m: StoredPaymentMethodDto): string {
  if (!m.expiryMonth || !m.expiryYear) return m.channelCode
  return t('card.expires', { month: String(m.expiryMonth).padStart(2, '0'), year: m.expiryYear })
}

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    methods.value = await bridge.listPaymentMethods()
  } catch (err) {
    methods.value = []
    loadError.value = err instanceof Error ? err.message : String(err)
  }
  loading.value = false
  loaded.value = true
}

onMounted(() => {
  void load()
})

async function afterChange(): Promise<void> {
  await load()
  emit('changed')
}

async function run(id: string, action: () => Promise<void>, successKey: string): Promise<void> {
  busyId.value = id
  try {
    await action()
    message.success(t(successKey))
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    busyId.value = null
  }
  await afterChange()
}

function setDefault(m: StoredPaymentMethodDto): Promise<void> {
  return run(m.id, () => bridge.setDefaultPaymentMethod(m.id), 'card.defaultSet')
}

function remove(m: StoredPaymentMethodDto): Promise<void> {
  return run(m.id, () => bridge.removePaymentMethod(m.id), 'card.removed')
}

function openSetup(replace: boolean): void {
  replacing.value = replace
  setupOpen.value = true
}

defineExpose({ reload: load })
</script>

<style scoped>
.t-billing-cards {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.t-billing-cards__row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border: var(--tnzi-surface-inset-border);
  box-shadow: var(--tnzi-surface-inset-shadow);
  border-radius: var(--tnzi-admin-radius-md, 8px);
}
.t-billing-cards__renewal {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
  font-size: 14px;
}
@media (max-width: 767px) {
  .t-billing-cards__row {
    flex-wrap: wrap;
  }
}
</style>
