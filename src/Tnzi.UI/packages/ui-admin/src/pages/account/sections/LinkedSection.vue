<template>
  <TUserCenterSection :title="t('linked.title')">
    <p class="t-uc-hint">{{ t('linked.hint') }}</p>
    <NSpin :show="loading">
      <ul v-if="linked.length" class="t-uc-linked-list">
        <li v-for="acc in linked" :key="acc.loginProvider" class="t-uc-linked-item">
          <div>
            <div class="t-uc-row-label">{{ acc.providerDisplayName || acc.loginProvider }}</div>
            <div class="t-uc-hint">{{ acc.providerKey }}</div>
          </div>
          <NPopconfirm @positive-click="unlink(acc.loginProvider)">
            <template #trigger>
              <NButton size="small" type="error" ghost>{{ t('linked.unlink') }}</NButton>
            </template>
            {{ t('linked.confirmUnlink') }}
          </NPopconfirm>
        </li>
      </ul>
      <div v-else class="t-uc-empty">{{ t('linked.empty') }}</div>
    </NSpin>

    <!-- Link a new account - follows the backend's enabled OAuth providers
         (GET /auth/config). Only providers not already linked are offered;
         nothing renders when the deployment has no OAuth providers. -->
    <template v-if="linkableProviders.length">
      <NDivider />
      <h4 class="t-uc-sub-title">{{ t('linked.linkTitle') }}</h4>
      <p class="t-uc-hint">{{ t('linked.linkHint') }}</p>
      <div class="t-uc-link-providers">
        <NButton
          v-for="p in linkableProviders"
          :key="p.provider"
          size="small"
          tertiary
          @click="linkProvider(p.provider)"
        >
          <template #icon><TSvgIcon icon="mdi:link-variant" :size="14" /></template>
          {{ p.displayName || p.provider }}
        </NButton>
      </div>
    </template>
  </TUserCenterSection>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { NButton, NDivider, NPopconfirm, NSpin } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import type { UserLoginDto } from '@tnzi/core/services/identity'
import TUserCenterSection from './TUserCenterSection.vue'
import { createGuardedLoader } from '../guardedLoader'
import { useUserCenterContext } from '../userCenterContext'

const ctx = useUserCenterContext()
const t = ctx.t

const linked = ref<UserLoginDto[]>([])
const loading = ref(false)

const load = createGuardedLoader<UserLoginDto[]>({
  flag: loading,
  fetch: () => ctx.bridge.me.getLinkedAccounts(),
  apply: (rows) => {
    linked.value = rows ?? []
  },
  // Linked accounts are optional (external providers may be disabled) - keep the
  // fail-silent behaviour, just with a guaranteed flag reset.
  onError: () => {
    linked.value = []
  },
  timeoutMessage: t('loadTimeout'),
})

/** Enabled OAuth providers that the user hasn't already linked. */
const linkableProviders = computed(() => {
  const alreadyLinked = new Set(linked.value.map((a) => a.loginProvider?.toLowerCase()))
  return ctx.capabilities.value.oauthProviders.filter(
    (p) => !alreadyLinked.has(p.provider.toLowerCase()),
  )
})

async function unlink(provider: string): Promise<void> {
  try {
    await ctx.bridge.me.unlinkAccount(provider)
    ctx.message.success(t('linked.unlinked'))
    await load()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  }
}

function linkProvider(provider: string): void {
  // Already-authenticated hit on the OAuth login endpoint links the provider to
  // the current account; the backend redirects back to `returnUrl` when done.
  const returnUrl = typeof window !== 'undefined' ? window.location.href : undefined
  const url = ctx.bridge.oauthLoginUrl(provider, returnUrl)
  if (url && typeof window !== 'undefined') window.location.assign(url)
}

onMounted(() => void load())
watch(() => ctx.reloadKey.value, () => void load())
</script>
