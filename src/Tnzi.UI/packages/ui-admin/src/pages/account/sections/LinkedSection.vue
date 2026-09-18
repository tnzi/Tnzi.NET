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
          @click="void linkProvider(p.provider)"
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
import { createGuardedLoader } from '../guarded-loader'
import { useUserCenterContext } from '../user-center-context'

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

async function linkProvider(provider: string): Promise<void> {
  // The OAuth start endpoint is anonymous and the navigation carries no bearer,
  // so "already signed in" is invisible to it: without a link token the backend
  // runs the login flow and, when the provider's email differs from ours,
  // creates a brand-new account instead of linking. Fetch the one-time link
  // token first (authenticated call), then hand it over on the URL; the
  // callback links the provider to this account and redirects back to
  // `returnUrl` without issuing tokens.
  if (typeof window === 'undefined') return
  try {
    const linkToken = await ctx.bridge.me.issueOAuthLinkToken(provider)
    const url = ctx.bridge.oauthLoginUrl(provider, window.location.href, linkToken.token)
    if (url) window.location.assign(url)
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  }
}

onMounted(() => void load())
watch(() => ctx.reloadKey.value, () => void load())
</script>
