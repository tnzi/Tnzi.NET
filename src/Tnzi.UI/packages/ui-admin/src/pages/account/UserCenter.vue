<template>
  <!--
    UserCenter - self-service personal center.

    Section-registry-driven shell (the same shape as the Settings Center): six
    built-in sections live in a registry and are merged with the consumer's
    `defineAdminApp({ userCenter })` config (hide / regroup / override / append
    custom sections). A slim header (avatar + name + roles + refresh) sits over
    a left vertical menu (grouped sections) + a right panel that renders the
    active section component. Reached via the avatar dropdown; hidden from menus.
  -->
  <div class="t-user-center">
    <TDetailHost :state="pageDetail" layout="side" :sections="sections" :back="false" :translate="t">
      <!-- Slim header: avatar + name + roles -->
      <template #title>
        <div class="t-user-center__head">
          <TAvatar
            :src="ctx.resolvedAvatarUrl.value"
            :name="ctx.profile.value?.nickname || ctx.profile.value?.userName || t('title')"
            :size="36"
            color="rgb(var(--tnzi-primary-rgb) / 0.12)"
            text-color="var(--tnzi-primary)"
          />
          <div class="t-user-center__head-text">
            <span class="t-user-center__head-name">
              {{ ctx.profile.value?.nickname || ctx.profile.value?.userName || t('title') }}
            </span>
            <span class="t-user-center__head-meta">
              <NTag v-for="r in ctx.profile.value?.roles ?? []" :key="r" size="tiny" :bordered="false">{{ r }}</NTag>
            </span>
          </div>
        </div>
      </template>

      <template #actions>
        <NButton size="small" tertiary :loading="ctx.loadingProfile.value" @click="ctx.reload">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('refresh') }}
        </NButton>
      </template>

      <template #default>
        <div class="t-user-center__panel">
          <NSpin :show="ctx.loadingProfile.value && !ctx.profile.value">
            <component :is="activeComponent" v-if="activeComponent" :key="activeSection ?? ''" />
          </NSpin>
        </div>
      </template>
    </TDetailHost>
  </div>
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, provide, type Component } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NSpin, NTag } from 'naive-ui'
import { TSvgIcon, TAvatar } from '@tnzi/ui'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { useDetail, type DetailSection } from '../../headless/useDetail'
import { useSafeMessage } from '../_shared/safeMessage'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { makePageTranslator } from '../_shared/translate'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { useModuleAvailability } from '../../headless/useModuleAvailability'
import { useAdminUserCenterConfig } from '../../plugin/userCenterConfig'
import { createUserCenterState, provideUserCenterContext } from './userCenterContext'
import { DETAIL_ACTIVE_SECTION_ICON } from '../../components/detail/activeSectionIcon'
import { resolveUserCenterSections, type UserCenterBuiltInDef } from './resolveSections'
import ProfileSection from './sections/ProfileSection.vue'
import SecuritySection from './sections/SecuritySection.vue'
import SessionsSection from './sections/SessionsSection.vue'
import HistorySection from './sections/HistorySection.vue'
import LinkedSection from './sections/LinkedSection.vue'
import DangerSection from './sections/DangerSection.vue'

const client = useAdminClient()
const bridge = createIdentityBridge({ client })
const storageBridge = createStorageBridge({ client })
const message = useSafeMessage()
const router = useRouter()
const authStore = useAdminAuthStore()
const t = makePageTranslator('account.userCenter')
const config = useAdminUserCenterConfig()
const { can } = usePermissionGuard()
const moduleAvail = useModuleAvailability()

/**
 * Common post-action when the current user's session becomes invalid
 * (deactivate / delete / revoke all sessions). Clears auth state, drops any
 * cached state, and hard-navigates to login so the next API call doesn't 401
 * against a half-rendered profile.
 */
function logoutAndRedirect(): void {
  authStore.logout()
  void router.replace({ name: 'login' }).catch(() => undefined)
}

// Shared state provided to the section subtree (header + sections read profile;
// sections reach the bridge / reload bus / field config through this context).
const ctx = createUserCenterState({
  bridge,
  storage: storageBridge,
  authStore,
  message,
  t,
  config,
  logoutAndRedirect,
})
provideUserCenterContext(ctx)

// ── Built-in section registry ──────────────────────────────────────────────
// Sections are grouped into three buckets in the left menu: Account
// (profile/security), Activity (sessions/history), Advanced (linked/danger).
const BUILTINS: UserCenterBuiltInDef[] = [
  { key: 'profile', component: ProfileSection, group: 'account', order: 10, icon: 'mdi:account-outline', labelKey: 'nav.profile' },
  { key: 'security', component: SecuritySection, group: 'account', order: 20, icon: 'mdi:shield-lock-outline', labelKey: 'nav.security' },
  { key: 'sessions', component: SessionsSection, group: 'activity', order: 30, icon: 'mdi:devices', labelKey: 'nav.sessions' },
  { key: 'history', component: HistorySection, group: 'activity', order: 40, icon: 'mdi:history', labelKey: 'nav.history' },
  { key: 'linked', component: LinkedSection, group: 'advanced', order: 50, icon: 'mdi:link-variant', labelKey: 'nav.linked' },
  { key: 'danger', component: DangerSection, group: 'advanced', order: 60, icon: 'mdi:alert-circle-outline', labelKey: 'nav.danger' },
]
const BUILTIN_GROUP_KEYS = new Set(['account', 'activity', 'advanced'])

function groupLabel(groupKey: string): string {
  // Built-in groups resolve their i18n label; custom groups pass through as-is
  // (consumers supply a plain label). TDetailLayout still runs it through
  // maybeTranslateKey, so a consumer i18n key that happens to match resolves too.
  return BUILTIN_GROUP_KEYS.has(groupKey) ? t(`nav.groups.${groupKey}`) : groupKey
}

/** Merge built-ins with the consumer config: hide / regroup / reorder /
 *  override built-ins, then append permission+module-gated custom sections,
 *  finally drop hidden groups and sort. (Pure logic in `resolveSections.ts`.) */
const resolvedSections = computed(() =>
  resolveUserCenterSections(BUILTINS, config, {
    t,
    can,
    hasModule: (m) => moduleAvail.has(m),
    groupLabel,
  }),
)

const sections = computed<DetailSection[]>(() =>
  resolvedSections.value.map((s) => ({ key: s.key, label: s.label, icon: s.icon, group: s.group })),
)

// Active section is two-way bound to `?section=` (deep-linkable + Back/Forward)
// via the shared composable; defaults to the first available section (built-ins
// may be hidden, so a static default would break).
const pageDetail = useDetail({
  mode: 'page',
  sectionUrl: true,
  sections,
  defaultSection: () => sections.value[0]?.key,
})
const activeSection = pageDetail.activeSection

// Mirror the active nav item's icon in the section header (side layout): the
// shell provides it, TUserCenterSection / TDetailSection injects + renders it
// before the title. Shares the generic DETAIL_ACTIVE_SECTION_ICON key.
const activeSectionIcon = computed<string | undefined>(
  () => sections.value.find((s) => s.key === activeSection.value)?.icon,
)
provide(DETAIL_ACTIVE_SECTION_ICON, activeSectionIcon)

// Resolve the active section's component. A loader (`() => import(...)`) is
// wrapped once in defineAsyncComponent and cached by section key so switching
// away and back doesn't produce a fresh definition (which would remount).
const componentCache = new Map<string, Component>()
function resolveComponent(key: string, source: Component | (() => Promise<unknown>)): Component {
  const cached = componentCache.get(key)
  if (cached) return cached
  // Contract (mirrors AdminSettingsSection.component): a plain loader function
  // (`() => import('./Section.vue')`) is wrapped in defineAsyncComponent; a
  // component object (incl. a built-in SFC / a defineAsyncComponent result) is
  // used as-is. Cached per key so switching away and back doesn't remount.
  const resolved =
    typeof source === 'function'
      ? defineAsyncComponent(source as () => Promise<{ default: Component }>)
      : (source as Component)
  componentCache.set(key, resolved)
  return resolved
}

const activeComponent = computed<Component | null>(() => {
  const s = resolvedSections.value.find((x) => x.key === activeSection.value)
  if (!s) return null
  return resolveComponent(s.key, s.component)
})

onMounted(() => {
  void ctx.loadProfile()
  void ctx.loadAuthConfig()
})
</script>

<style scoped>
.t-user-center {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

/* Slim header (avatar + name + roles) rendered in TDetailLayout's #title. */
.t-user-center__head {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}
.t-user-center__head-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.t-user-center__head-name {
  font-size: 16px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  line-height: 1.2;
}
.t-user-center__head-meta {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

/* Right content panel - fills TDetailLayout's white panel card. The panel never
   scrolls; each section owns a fixed bar + a scrolling (or flex-height-filling)
   body. */
.t-user-center__panel {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-user-center__panel :deep(.n-spin-container),
.t-user-center__panel :deep(.n-spin-content) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}
</style>
