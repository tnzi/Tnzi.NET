<script setup lang="ts">
/**
 * Default `/admin` route layout — wraps the child routes in {@link TAdminShell}
 * so the 42-page preset renders inside the standard sidebar + header + content
 * frame without the consumer wiring layout themselves. Also bridges the
 * sidebar's `menuSelect` event onto vue-router so clicks navigate; without
 * this the preset's menu would be inert until the consumer wired it up.
 *
 * In Phase A (0.2.3) this wrapper also owns the Theme Settings Drawer instance
 * and toggles it on the header's 🎨 button — so consumers get a fully working
 * "click → open drawer → tweak → persist" loop out of the box.
 *
 * Consumers can override this by registering a route at `path: '/admin'` with
 * their own component; the route table replacement logic in
 * {@link createTnziUiAdmin} treats consumer-supplied routes as authoritative.
 */
import { computed, inject, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NConfigProvider, darkTheme, type GlobalThemeOverrides } from 'naive-ui'
import { THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui'
import TAdminShell from '../components/layout/TAdminShell.vue'
import TAdminAutoBreadcrumb from '../components/layout/TAdminAutoBreadcrumb.vue'
import TAdminUserAvatar from '../components/layout/TAdminUserAvatar.vue'
import TAdminRouterView from '../components/layout/TAdminRouterView.vue'
import TThemeDrawer from '../components/layout/TThemeDrawer.vue'
import type { AdminMenuItem } from '../stores/useAdminRouteStore'
import { useAdminAppStore } from '../stores/useAdminAppStore'
import { useAdminThemeStore } from '../stores/useAdminThemeStore'
import { useAdminTabStore, type AdminTab } from '../stores/useAdminTabStore'
import { useAdminLoginConfig } from '../plugin/loginConfig'
import { en } from '../locales/en'
import { zhCn } from '../locales/zh-cn'

const router = useRouter()
const route = useRoute()
const appStore = useAdminAppStore()
const themeStore = useAdminThemeStore()
const tabStore = useAdminTabStore()

/** NConfigProvider theme overrides — propagate `themeStore.themeRadius`
 *  into Naive UI's `common.borderRadius` / `borderRadiusSmall` tokens so
 *  every N* component (NCard, NButton, NInput, NSelect, NDataTable rows,
 *  NTag, NModal, NDrawer…) tracks the Theme Drawer's radius slider.
 *  Without this, the slider only moves ~12 ui-admin elements that
 *  reference `var(--tnzi-admin-radius-*)` — the bulk of the UI (~80%
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
})

/**
 * Defensive dark-theme binding — when consumers wrap their top-level
 * `<App>` with `<NConfigProvider :theme="...">` (the Acme pattern),
 * Naive UI's inner providers inherit the theme automatically and this
 * binding is redundant. When they DON'T (the bare-bones "drop ui-admin
 * in and go" pattern), NConfigProvider here has `theme: undefined`
 * which forces every NCard / NInput / NDataTable inside the admin
 * shell to render with the light palette — even when `<html class="dark">`
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
    // dedicated page — skip it.
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
// Format: `Copyright © {year} {brand}` — matches soybean's default.
const footerCopyright = computed<string>(() => {
  if (loginConfig.footer?.copyright !== undefined) return loginConfig.footer.copyright
  const brand = loginConfig.brand ?? 'Tnzi Admin'
  const year = new Date().getFullYear()
  return `Copyright © ${year} ${brand}`
})

const themeDrawerOpen = ref(false)

function onMenuSelect(menu: AdminMenuItem): void {
  // Leaf entries carry an absolute route path; navigate to it. Branch entries
  // (with children) are toggled open by NMenu itself — nothing to do here.
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
 * Default fallback for the avatar's "User Center" menu item — pushes the
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
  // Strip optional `tnzi.` prefix — bundled locales are rooted at `admin.*`
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
      :sider="{ brand: loginConfig.brand, brandIcon: loginConfig.brandIcon }"
      :footer="{ copyright: footerCopyright, links: loginConfig.footer?.links }"
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
    <!-- Header user avatar (Phase I.7.7+) — wired from `useAdminLoginConfig().user`. -->
    <template #header-user>
      <TAdminUserAvatar
        :user-name="loginConfig.user?.userName"
        :avatar-icon="loginConfig.user?.avatarIcon"
        :on-user-center="loginConfig.user?.onUserCenter ?? goUserCenter"
        :on-logout="loginConfig.user?.onLogout"
        :signed-in="loginConfig.user?.signedIn ?? true"
        :on-sign-in="loginConfig.user?.onSignIn"
        :translate="loginConfig.translate ?? defaultTranslate"
      />
    </template>
    <!-- Phase A (post-0.2.52): wrap router outlet in TAdminRouterView so route -->
    <!-- transitions actually trigger. A bare <router-view> is mounted inside -->
    <!-- TAdminContent's <Transition> wrapper but the component identity that -->
    <!-- swaps on navigation lives inside the slot — Vue can't see it from -->
    <!-- the outer transition. TAdminRouterView uses the canonical -->
    <!-- <RouterView v-slot> + <Transition> + <component :is> pattern. -->
    <TAdminRouterView :exclude="['login', '403', '404']" />
    </TAdminShell>
    <TThemeDrawer v-model:show="themeDrawerOpen" :translate="defaultTranslate" />
  </NConfigProvider>
</template>
