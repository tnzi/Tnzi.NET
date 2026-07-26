<script setup lang="ts">
/**
 * Default `/admin` route layout - wraps the child routes in {@link TAdminShell}
 * so the 42-page preset renders inside the standard sidebar + header + content
 * frame without the consumer wiring layout themselves. Also bridges the
 * sidebar's `menuSelect` event onto vue-router so clicks navigate; without
 * this the preset's menu would be inert until the consumer wired it up.
 *
 * In Phase A (0.2.3) this wrapper also owns the Theme Settings Drawer instance
 * and toggles it on the header's 🎨 button - so consumers get a fully working
 * "click → open drawer → tweak → persist" loop out of the box.
 *
 * Consumers can override this by registering a route at `path: '/admin'` with
 * their own component; the route table replacement logic in
 * {@link createTnziUiAdmin} treats consumer-supplied routes as authoritative.
 */
import { computed, inject, ref, watch, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NConfigProvider, darkTheme, type GlobalThemeOverrides } from 'naive-ui'
import { THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui'
import TAdminShell from '../components/layout/TAdminShell.vue'
import TAdminAutoBreadcrumb from '../components/layout/TAdminAutoBreadcrumb.vue'
import TAdminUserAvatar from '../components/layout/TAdminUserAvatar.vue'
import TAdminRouterView from '../components/layout/TAdminRouterView.vue'
import TThemeDrawer from '../components/layout/TThemeDrawer.vue'
import type { AdminMenuItem } from '../stores/useAdminRouteStore'
import { useAdminRouteStore } from '../stores/useAdminRouteStore'
import { useAdminAppStore } from '../stores/useAdminAppStore'
import { useAdminThemeStore } from '../stores/useAdminThemeStore'
import { useAdminTabStore, type AdminTab } from '../stores/useAdminTabStore'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'
import { useChatStore } from '../stores/useChatStore'
import { createChatImBridge } from '../services/bridges/chat-im-bridge'
import type { UserPresenceStatus } from '@tnzi/core/services/chat'
import { useAdminLoginConfig } from '../plugin/loginConfig'
import { useAdminChatConfig } from '../plugin/chatConfig'
import { useAdminSettingsConfig } from '../plugin/settingsConfig'
import { useAdminThemeConfig } from '../plugin/themeConfig'
import { BUILTIN_APPEARANCE_PRESETS } from '../theme/appearancePresets'
import { useAdminClient } from '../plugin/client'
import { usePermissionGuard } from '../headless/usePermissionGuard'
import { useModuleAvailability } from '../headless/useModuleAvailability'
import { useGlobalTheme } from '../headless/useGlobalTheme'
import { useSettingsRealtime } from '../headless/useSettingsRealtime'
import { usePresenceActivity } from '../headless/usePresenceActivity'
import { useStorageApi } from '@tnzi/core/services/storage'
import { resolveAvatarUrl } from '../utils/resolveAvatarUrl'
import { en } from '../locales/en'
import { zhCn } from '../locales/zh-cn'

const router = useRouter()
const route = useRoute()
const appStore = useAdminAppStore()
const themeStore = useAdminThemeStore()
const tabStore = useAdminTabStore()
const routeStore = useAdminRouteStore()
const authStore = useAdminAuthStore()

// Reactively drop persisted tabs the current user can't open - either because
// they lack permission (`deniedRouteNames`) OR because the tab points into a
// framework module the backend didn't load (`unavailableRouteNames`, the
// module-availability twin). This is the shell-level twin of the
// loadPermissions-time prune: it also fires on a page RELOAD, where the auth
// store hydrates from localStorage (userInfo already non-null) so the consumer's
// "load permissions only when userInfo === null" idempotency guard skips
// loadPermissions - without this, a prior session's Diagnostics / MCP / Sandbox
// (or an unloaded module's) tabs would still sit in the bar (clicking one is
// already blocked by the guards, but the ghost tab shouldn't linger). Both sets
// are empty for super users / before the signals load, so this is a no-op except
// on a real privilege downgrade or a module the host stopped loading.
watch(
  () => new Set([...routeStore.deniedRouteNames, ...routeStore.unavailableRouteNames]),
  (denied) => tabStore.pruneTabs(denied),
  { immediate: true },
)

// Resolve the header avatar from the signed-in user (auth store). The store
// only carries an external `avatar` link today, but routing it through
// `resolveAvatarUrl` keeps the door open for an `avatarId`-backed picture
// (storage preview URL) without changing this call site. `useAdminClient(false)`
// → no throw when the shell renders before a client is provided.
const storageClient = useAdminClient(false)
const storageApi = storageClient ? useStorageApi(storageClient) : null
const headerAvatarUrl = computed<string | null>(() =>
  storageApi ? resolveAvatarUrl(authStore.userInfo, storageApi) : null,
)

/** NConfigProvider theme overrides - propagate `themeStore.themeRadius`
 *  into Naive UI's `common.borderRadius` / `borderRadiusSmall` tokens so
 *  every N* component (NCard, NButton, NInput, NSelect, NDataTable rows,
 *  NTag, NModal, NDrawer…) tracks the Theme Drawer's radius slider.
 *  Without this, the slider only moves ~12 ui-admin elements that
 *  reference `var(--tnzi-admin-radius-*)` - the bulk of the UI (~80%
 *  Naive UI primitives) stays at the library default.
 *  Inner NConfigProviders inherit + override outer ones, so any
 *  Acme-side `<NConfigProvider :theme-overrides="{ common: { primaryColor } }">`
 *  upstream of this still wins for primary colour. */
const naiveOverrides = computed<GlobalThemeOverrides>(() => {
  const r = themeStore.themeRadius
  return {
    common: {
      borderRadius: `${r}px`,
      borderRadiusSmall: `${Math.max(0, Math.floor(r * 0.5))}px`,
    },
  }
  // NOTE: the "Card / List" surface repaint (naive NCard + NDataTable + the
  // dark-card auto-match for inputs/buttons/borders) lives in TAdminContent's
  // content-scoped NConfigProvider, NOT here - so it reaches the page content
  // but leaves the chrome (sider/header/tab) and teleported modals/popovers on
  // the outer theme.
})

/**
 * Defensive dark-theme binding - when consumers wrap their top-level
 * `<App>` with `<NConfigProvider :theme="...">` (the Acme pattern),
 * Naive UI's inner providers inherit the theme automatically and this
 * binding is redundant. When they DON'T (the bare-bones "drop ui-admin
 * in and go" pattern), NConfigProvider here has `theme: undefined`
 * which forces every NCard / NInput / NDataTable inside the admin
 * shell to render with the light palette - even when `<html class="dark">`
 * is set. Reading `THEME_CONTEXT_KEY` (provided by `createTnziUi()`)
 * and applying `darkTheme` ourselves makes the admin shell self-sufficient
 * without breaking the Acme inheritance flow (consumers' outer provider
 * still wins for any token they explicitly override).
 */
const themeCtx = inject<ThemeContext | undefined>(THEME_CONTEXT_KEY, undefined)
const naiveTheme = computed(() => (themeCtx?.isDark.value ? darkTheme : null))
// Phase I.7.6: reuse the login-page brand config for the sidebar's
// header logo + title. Both surfaces want the same brand identity, and
// consumers already configured this for the login page.
const loginConfig = useAdminLoginConfig()
const chatConfig = useAdminChatConfig()

// Built-in chat - enabled by consumer config (default on) AND the backend
// actually loaded the Chat module. `canActivate` (not `has`) because TChatHost
// is side-effectful on mount (fetches conversations, opens the SignalR hub):
// it must not race the availability probe, so it defers while the signal is
// in flight and only mounts once the signal settles (fail-open on old
// backends without the endpoint). Reactive: if a later refresh drops Chat,
// the host unmounts and tears the socket down.
const moduleAvailability = useModuleAvailability()
const chatStore = useChatStore()

// Whether chat COULD run here: consumer opted in (default on) AND the backend
// loaded the Chat module. This gates the lightweight GET /chat/config probe.
const builtinChatPossible = computed(
  () => chatConfig?.enabled !== false && moduleAvailability.canActivate('chat'),
)

// Load the per-user config independently of TChatHost. Chat is deny-by-default:
// the launcher only appears once /chat/config confirms this user holds
// `chat.use`. Probing here (not inside TChatHost) means a disabled user never
// mounts TChatHost at all - no conversation fetch, no SignalR socket, no icon.
//
// ★ The chat store's bridge MUST be initialised HERE (not only in TChatHost's
// setup) so `loadConfig()` actually runs `getConfig()`. Previously the bridge
// was init'd ONLY by TChatHost, so this `loadConfig()` threw at `requireBridge()`
// and fell into the fail-open catch → enabled:true → TChatHost mounted for EVERY
// user (even those without chat.use), which then 403'd on /conversations. Wiring
// the bridge up front lets `getConfig()` return the real per-user `enabled`
// (false for a denied user) so TChatHost never mounts for them. TChatHost
// re-inits the same (stateless) bridge idempotently when it does mount.
watch(
  builtinChatPossible,
  (possible) => {
    if (possible && storageClient) {
      chatStore.init(createChatImBridge({ client: storageClient }))
      void chatStore.loadConfig()
    }
  },
  { immediate: true },
)

// The actual launcher/host gate: possible AND this user may use chat.
// `config.enabled` starts false (no icon flash for a disabled user) and flips
// true only after the probe confirms the grant. Deny-by-default: a config that
// can't be confirmed (null/forbidden/throw) stays `enabled:false`, so a denied
// user never mounts TChatHost or hits its 403-guarded endpoints.
const builtinChatEnabled = computed(
  () => builtinChatPossible.value && chatStore.config.enabled === true,
)

// Presence in the header avatar - only when the built-in chat is live AND a
// client is present (TChatHost then inits the chat bridge + loads my status).
const presenceEnabled = computed(() => builtinChatEnabled.value && !!storageClient)
function onSetPresence(status: UserPresenceStatus): void {
  void chatStore.setMyStatus(status).catch(() => undefined)
}

// Phase I.7.8: auto-push a tab into the tab store whenever the route
// changes. Without this watcher the tab bar stays empty until the user
// clicks a sidebar item, and even then `switchRouteByTab` was a stub
// that only flipped `activeTabId` without navigating. `immediate: true`
// seeds the initial tab on the first page load.
watch(
  () => route.fullPath,
  () => {
    // Only track routes that have a name (built-in shell entries do).
    // The bare `/admin` redirect is named `admin-root` and has no
    // dedicated page - skip it.
    if (!route.name || route.name === 'admin-root') return
    tabStore.addTab({
      name: typeof route.name === 'string' ? route.name : String(route.name),
      fullPath: route.fullPath,
      path: route.path,
      query: route.query as Record<string, unknown>,
      params: route.params as Record<string, unknown>,
      meta: {
        title: (route.meta?.title as string | undefined) ?? undefined,
        keepAlive: route.meta?.keepAlive as boolean | undefined,
        fixedIndexInTab: route.meta?.fixedIndexInTab as number | undefined,
        multiTab: route.meta?.multiTab as boolean | undefined,
        icon: route.meta?.icon as string | undefined,
      },
    })
  },
  { immediate: true },
)

function onTabClick(tab: AdminTab): void {
  if (route.fullPath === tab.fullPath) return
  void router.push(tab.fullPath)
}

// Phase I.7.9: default footer copyright when consumer didn't supply one.
// Format: `Copyright © {year} {brand}` - matches soybean's default.
const footerCopyright = computed<string>(() => {
  if (loginConfig.footer?.copyright !== undefined) return loginConfig.footer.copyright
  const brand = loginConfig.brand ?? 'Tnzi Admin'
  const year = new Date().getFullYear()
  return `Copyright © ${year} ${brand}`
})

const themeDrawerOpen = ref(false)

// ── Global admin theme ─────────────────────────────────────────────────────
// The super admin's theme configuration applies to EVERY user: the shell
// loads the server snapshot after sign-in and applies it over the locally
// cached theme. Privileged users (system.appearance.update - super admins by
// default under deny-by-default) get the full drawer and edit the global
// snapshot; everyone else gets a preset color-scheme picker whose visibility
// the admin controls via the drawer's General → Global section.
const themeConfig = useAdminThemeConfig()
const { can } = usePermissionGuard()
const canEditGlobalTheme = computed(() => can('system.appearance.update'))
const globalTheme = useGlobalTheme({
  client: storageClient,
  themeContext: themeCtx,
  enabled: themeConfig?.globalSync !== false,
  // Personal preset colors only overlay for preset-picker users. Privileged
  // users edit the global theme directly - overlaying THEIR lingering
  // personal color would leak it into the next "save for all users".
  shouldOverlayUserPreset: () => !canEditGlobalTheme.value,
  // Lets a non-privileged user's whole chosen look (`userPresetLook`) re-apply
  // after the global snapshot lands on boot.
  appearancePresets: themeConfig?.appearancePresets ?? BUILTIN_APPEARANCE_PRESETS,
})
// Load once signed in AND the System module (which owns the appearance
// endpoints) is actually loaded - `canActivate` defers while the module
// signal is in flight, so a host without Tnzi.System never sees a doomed
// GET /appearance/admin-theme on boot. Fail-open on old backends (no signal).
watch(
  () => authStore.isLogin && moduleAvailability.canActivate('system'),
  (ready) => {
    if (ready) void globalTheme.load()
  },
  { immediate: true },
)
const themeDrawerMode = computed<'full' | 'presets'>(() =>
  canEditGlobalTheme.value ? 'full' : 'presets',
)
const themeBtnVisible = computed(
  () => canEditGlobalTheme.value || themeStore.presetPickerVisible,
)

// ── Realtime config push ────────────────────────────────────────────────────
// Subscribe to `/hubs/settings` so a super admin's deployment-config change
// (Settings Center runtime settings, or the global theme) reaches this already-
// open session live - no manual page reload. The backend broadcasts only the
// changed key; we route it to the matching re-fetch:
//   Appearance:AdminTheme → reload + re-apply the global theme
//   Chat:*                → re-fetch chat config (the chat window reads
//                           store.config reactively, so the invisible option /
//                           presence / attachment gating update in place)
// Gated on `system` module availability (the hub lives in Tnzi.System) so a host
// without it never opens a doomed socket. Fail-open on old backends (no signal).
// Presence auto-away reporter (declared before settingsRealtime, whose onChanged reloads it
// on a `Presence:*` runtime config change).
const presenceActivity = usePresenceActivity(storageClient)

const settingsConfig = useAdminSettingsConfig()
const settingsRealtime = useSettingsRealtime({
  // Optional hub URL override (e.g. '/api/hubs/settings' under a sub-path).
  // Undefined when unset, so useSettingsRealtime falls back to '/hubs/settings'.
  hubUrl: settingsConfig?.hubUrl,
  getToken: () => storageClient?.getAccessToken?.() ?? authStore.token ?? '',
  onChanged: (p) => {
    if (p.key === 'Appearance:AdminTheme') {
      void globalTheme.load()
    } else if (p.key.startsWith('Chat:')) {
      // Re-fetch on the could-run gate, not the ready gate: a config change
      // (or a re-grant) must be able to flip `enabled` back on from disabled.
      if (builtinChatPossible.value) void chatStore.loadConfig()
    } else if (p.key.startsWith('Presence:')) {
      // auto-away threshold / toggles changed at runtime → reload so the reporter
      // picks up the new idle minutes / enabled state without a page refresh.
      void presenceActivity.loadConfig()
    }
    // Consumer routes (defineAdminApp({ settings: { realtime } })) run after the
    // built-ins - matched by exact key or prefix, isolated so one bad handler
    // doesn't break the others.
    for (const r of settingsConfig?.realtime ?? []) {
      if (p.key === r.prefix || p.key.startsWith(r.prefix)) {
        try {
          r.handler(p)
        } catch {
          /* consumer handler errors must not break the realtime pipeline */
        }
      }
    }
  },
})
watch(
  () => authStore.isLogin && !!storageClient && moduleAvailability.canActivate('system'),
  (ready) => {
    if (ready) void settingsRealtime.start()
    else void settingsRealtime.stop()
  },
  { immediate: true },
)
onUnmounted(() => { void settingsRealtime.stop() })

// Presence auto-away - report user activity so contacts see this user flip to Away after
// idle and back to Online on return. Only fires on idle transitions (no heartbeat). Gated on
// login + client only: Tnzi.Identity.Presence is an OPTIONAL module, so instead of a module
// gate the reporter SELF-DISABLES when GET /presence/config 404s (usePresenceActivity keeps a
// disabled config on load failure), which is simpler and equally inert when presence is absent.
watch(
  () => authStore.isLogin && !!storageClient,
  async (ready) => {
    if (ready) {
      await presenceActivity.loadConfig()
      presenceActivity.start()
    } else {
      presenceActivity.stop()
    }
  },
  { immediate: true },
)
onUnmounted(() => { presenceActivity.stop() })

function onMenuSelect(menu: AdminMenuItem): void {
  // Leaf entries carry an absolute route path; navigate to it. Branch entries
  // (with children) are toggled open by NMenu itself - nothing to do here.
  if (menu.children && menu.children.length > 0) return
  if (!menu.path) return
  void router.push(menu.path)
}

function onOpenThemeDrawer(): void {
  themeDrawerOpen.value = true
}

function onLocaleChange(locale: 'en' | 'zh-cn'): void {
  appStore.setLocale(locale)
}

/**
 * Default fallback for the avatar's "User Center" menu item - pushes the
 * built-in `/admin/user-center` route. Consumers can still override by
 * passing `defineAdminApp({ login: { user: { onUserCenter } } })`.
 */
function goUserCenter(): void {
  router.push({ name: 'user-center' }).catch(() => undefined)
}

/**
 * Default translator for the bundled drawer / shell components. Looks up
 * dotted keys against the `@tnzi/ui-admin/locales/{en,zh-cn}` packs;
 * returns the raw key on miss so consumers can see exactly which keys
 * still need translation. Consumers using their own i18n stack can
 * still override by rendering `<TThemeDrawer :translate="..." />` directly.
 */
function defaultTranslate(key: string, fallback?: string): string {
  if (!key) return key
  const messages = (appStore.locale === 'zh-cn' ? zhCn : en) as Record<string, unknown>
  // Strip optional `tnzi.` prefix - bundled locales are rooted at `admin.*`
  // (mirrors translatePageKey / resolveI18nKey).
  const normalised = key.startsWith('tnzi.') ? key.slice(5) : key
  let node: unknown = messages
  for (const part of normalised.split('.')) {
    if (typeof node === 'object' && node !== null && part in (node as Record<string, unknown>)) {
      node = (node as Record<string, unknown>)[part]
    } else {
      return fallback ?? key
    }
  }
  return typeof node === 'string' ? node : (fallback ?? key)
}
</script>

<template>
  <NConfigProvider :theme="naiveTheme" :theme-overrides="naiveOverrides" inline-theme-disabled>
    <TAdminShell
      :title="loginConfig.brand ?? 'Tnzi Admin'"
      :sider="{ brand: loginConfig.brand, brandSubtitle: loginConfig.brandSubtitle, brandIcon: loginConfig.brandIcon }"
      :header="{ showThemeBtn: themeBtnVisible }"
      :footer="{ copyright: footerCopyright, links: loginConfig.footer?.links }"
      :builtin-chat="builtinChatEnabled"
      @menu-select="onMenuSelect"
      @open-theme-drawer="onOpenThemeDrawer"
      @locale-change="onLocaleChange"
      @tab-click="onTabClick"
    >
    <!-- Header breadcrumb derived from the current route (Phase I.7.7+).
         `:show-icon` reads from themeStore so the Theme Drawer's
         "show breadcrumb icon" switch actually takes effect. -->
    <template #header-breadcrumb>
      <TAdminAutoBreadcrumb
        :show-icon="themeStore.breadcrumbShowIcon"
        :translate="defaultTranslate"
      />
    </template>
    <!-- Header user avatar (Phase I.7.7+) - wired from `useAdminLoginConfig().user`. -->
    <template #header-user>
      <TAdminUserAvatar
        :user-name="authStore.userInfo?.shortName || authStore.userInfo?.displayName || authStore.userInfo?.username || loginConfig.user?.userName"
        :avatar-url="headerAvatarUrl"
        :avatar-icon="loginConfig.user?.avatarIcon"
        :on-user-center="loginConfig.user?.onUserCenter ?? goUserCenter"
        :on-logout="loginConfig.user?.onLogout"
        :signed-in="loginConfig.user?.signedIn ?? true"
        :on-sign-in="loginConfig.user?.onSignIn"
        :presence="presenceEnabled ? chatStore.myStatus : null"
        :on-set-presence="onSetPresence"
        :allow-invisible="chatStore.config.allowInvisible"
        :translate="loginConfig.translate ?? defaultTranslate"
      />
    </template>
    <!-- Header notification bell - a consumer component (e.g. THeaderBell)
         mounted in the shell's real header-notification slot via
         defineAdminApp({ login: { headerNotification } }). Replaces the old
         Teleport-into-header-internals hack. -->
    <template v-if="loginConfig.headerNotification" #header-notification>
      <component :is="loginConfig.headerNotification" />
    </template>
    <!-- Phase A (post-0.2.52): wrap router outlet in TAdminRouterView so route -->
    <!-- transitions actually trigger. A bare <router-view> is mounted inside -->
    <!-- TAdminContent's <Transition> wrapper but the component identity that -->
    <!-- swaps on navigation lives inside the slot - Vue can't see it from -->
    <!-- the outer transition. TAdminRouterView uses the canonical -->
    <!-- <RouterView v-slot> + <Transition> + <component :is> pattern. -->
    <TAdminRouterView :exclude="['login', '403', '404']" />
    </TAdminShell>
    <TThemeDrawer
      v-model:show="themeDrawerOpen"
      :mode="themeDrawerMode"
      :global-theme="globalTheme"
      :presets="themeConfig?.presets"
      :appearance-presets="themeConfig?.appearancePresets"
      :translate="defaultTranslate"
    />
  </NConfigProvider>
</template>
