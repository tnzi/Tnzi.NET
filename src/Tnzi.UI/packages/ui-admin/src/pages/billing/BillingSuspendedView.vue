<template>
  <div class="t-billing-suspended t-page-scroll" data-testid="billing-suspended">
    <div class="t-billing-suspended__head t-surface-card">
      <TSvgIcon icon="mdi:pause-circle-outline" :size="36" color="var(--tnzi-error)" />
      <div class="flex flex-col gap-4px min-w-0">
        <h2 class="t-billing-suspended__title">{{ t('suspended.title') }}</h2>
        <p class="t-billing-suspended__text" data-testid="billing-suspended-text">
          {{ standing.canManage ? t('suspended.managerBody') : t('suspended.memberBody') }}
        </p>
      </div>
      <NButton v-if="!standing.canManage" class="ml-auto" data-testid="billing-sign-out" @click="signOut">
        {{ t('actions.signOut') }}
      </NButton>
    </div>
    <template v-if="standing.canManage">
      <div class="t-billing-suspended__panel t-surface-card">
        <BillingSubscriptionSection />
      </div>
      <div class="t-billing-suspended__panel t-surface-card">
        <BillingPaymentMethodsSection />
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
/**
 * Takes the place of the shell's content while the billing gate refuses this
 * user's requests. A billing manager pays from here (the subscription and
 * payment-method sections, inline); a successful payment refreshes the
 * standing and the shell renders its pages again without a reload. Anyone
 * else is told who can restore the service, and may sign out.
 */
import { useRouter } from 'vue-router'
import { NButton } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import type { BillingStandingDto } from '@tnzi/core/services/payment'
import { useAdminLoginConfig } from '../../plugin/login-config'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import BillingSubscriptionSection from './BillingSubscriptionSection.vue'
import BillingPaymentMethodsSection from './BillingPaymentMethodsSection.vue'
import { billingT as t } from './billing-context'

defineProps<{ standing: BillingStandingDto }>()

const router = useRouter()
const loginConfig = useAdminLoginConfig()

async function signOut(): Promise<void> {
  const onLogout = loginConfig.user?.onLogout
  if (onLogout) {
    await onLogout()
    return
  }
  useAdminAuthStore().logout()
  await router.replace({ name: 'login' }).catch(() => undefined)
}
</script>

<style scoped>
.t-billing-suspended {
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: 100%;
  height: 100%;
  min-height: 0;
}
.t-billing-suspended__head.t-surface-card {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 16px;
  padding: 20px 24px;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: var(--tnzi-surface-card-bg, var(--tnzi-container-bg));
  border: var(--tnzi-surface-card-border);
  box-shadow: var(--tnzi-surface-card-shadow);
}
.t-billing-suspended__title {
  margin: 0;
  font-size: var(--tnzi-admin-title-page-size, 18px);
  font-weight: var(--tnzi-admin-title-page-weight, 600);
}
.t-billing-suspended__text {
  margin: 0;
  color: var(--tnzi-base-text-muted);
}
.t-billing-suspended__panel.t-surface-card {
  flex: none;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  overflow: hidden;
  background: var(--tnzi-surface-card-bg, var(--tnzi-container-bg));
  border: var(--tnzi-surface-card-border);
  box-shadow: var(--tnzi-surface-card-shadow);
}
</style>
