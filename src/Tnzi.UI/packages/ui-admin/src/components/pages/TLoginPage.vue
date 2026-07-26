<script setup lang="ts">
/**
 * `TLoginPage` - login page shell (2026-06-11 redesign).
 *
 * Two layouts, selected via the `layout` prop (consumers set it through
 * `defineAdminApp({ login: { layout } })`):
 *
 *   - `'wave'` (default, 方案 A) - the direct evolution of the legacy
 *     soybean-style page: brand-tinted background + drifting aurora blobs +
 *     dual-layer animated waves, centered card with staggered entrance.
 *   - `'split'` (方案 B) - brand narrative panel on the left (flowing aurora,
 *     tagline, admin-shell miniature) + white form pane on the right with a
 *     "Welcome back" heading and stacked field labels. Below 768px the panel
 *     collapses to a top brand strip.
 *
 * Optional interactions (both layouts, hidden unless configured):
 *   - `qrComponent` - corner-fold toggle (top-right) that swaps the form for
 *     a consumer-rendered QR sign-in panel.
 *   - `thirdParty` - providers forwarded to `PwdLogin` via the context,
 *     rendered as round icon buttons under an "Or continue with" divider.
 *
 * The shell exposes a {@link LoginContext} via `provide()` so each module can
 * `useLoginContext()` to translate, switch sibling modules, call the
 * consumer-supplied auth callbacks, and read layout style hints
 * ({@link LoginUiStyle}: stacked labels + squared buttons in `split`).
 */
import { computed, onMounted, reactive, ref, watch, type Component } from 'vue'
import { NCard } from 'naive-ui'
import { useTheme, getPaletteColorByNumber, mixColor } from '@tnzi/ui'
import { TSvgIcon } from '@tnzi/ui'
import { TThemeSchemaSwitch } from '@tnzi/ui'
import { TLangSwitch } from '@tnzi/ui'
import LoginWaves from './login/LoginWaves.vue'
import LoginBrandPanel from './login/LoginBrandPanel.vue'
import {
  provideLoginContext,
  DEFAULT_LOGIN_FEATURES,
  type LoginContext,
  type LoginModule,
  type LoginCallbacks,
  type LoginDemoAccount,
  type LoginFeatures,
  type LoginSceneState,
  type LoginThirdPartyProvider,
  type LoginUiStyle,
  type TwoFactorChallenge,
  type LoginCaptchaData,
} from '../../pages/login/useLoginContext'

interface Props {
  /** Active module. Defaults to `'pwd-login'`. */
  module?: LoginModule
  /** Module components map (6 entries: pwd-login / code-login / register / reset-pwd / bind-wechat / two-factor). */
  moduleComponents: Record<LoginModule, Component>
  /** Page layout - `'wave'` (centered card, 方案 A) or `'split'` (brand panel + form, 方案 B). */
  layout?: 'wave' | 'split'
  /** Brand title shown next to the logo. */
  brand?: string
  /** Iconify icon name for the brand logo. */
  brandIcon?: string
  /** Pixel size of the brand logo (wave layout header). Default 56. */
  brandIconSize?: number
  /** Split-layout headline. Defaults to the `admin.login.tagline` locale entry. */
  tagline?: string
  /** Split-layout supporting line. Defaults to `admin.login.taglineSub`. */
  taglineSub?: string
  /** Copyright line. Defaults to `© {year} {brand}`. */
  copyright?: string
  /** Translation function - fallback returns `fallback ?? key`. */
  translate?: (key: string, fallback?: string) => string
  /** Auth callbacks passed down to modules via `useLoginContext()`. */
  callbacks?: LoginCallbacks
  /** Demo account quick-fill buttons (rendered by `PwdLoginModule`). */
  demoAccounts?: LoginDemoAccount[]
  /** Third-party sign-in providers (rendered by `PwdLoginModule`). */
  thirdParty?: LoginThirdPartyProvider[]
  /**
   * Consumer-rendered QR sign-in panel (e.g. a component that fetches and
   * polls a QR token). When provided, a corner-fold toggle appears on the
   * pwd-login / code-login screens.
   */
  qrComponent?: Component
  /**
   * Module display labels keyed by module name. Each module title renders
   * above the form body. Defaults below match soybean's `loginModuleRecord`.
   */
  moduleLabels?: Partial<Record<LoginModule, string>>
  /**
   * Called by modules (via `useLoginContext().toggleLoginModule`) to switch
   * the active module. Page-level consumers wire this to
   * `router.replace({ name: 'login', params: { module: name } })` (by NAME,
   * so it follows any basePath / deployment prefix).
   */
  onToggleModule?: (name: LoginModule) => void
  /** CSS transition `name` for the `<Transition>` wrapper. Default `'fade-slide'`. */
  transitionName?: string
  /**
   * Show the language switcher (`TLangSwitch`) in the toolbar.
   * Defaults to **false** - many deployments are single-locale.
   */
  showLangSwitch?: boolean
  /**
   * Show the theme-schema switcher (`TThemeSchemaSwitch`) in the toolbar.
   * Defaults to **true**.
   */
  showThemeSwitch?: boolean
  /**
   * Feature flags driving module/entry visibility + account field rules,
   * passed down to modules via `useLoginContext().features`. Defaults to
   * everything enabled.
   */
  features?: LoginFeatures
  /**
   * Replace the split-layout left panel (brand / animation area) entirely.
   * Falls back to the `#aside` slot, then the built-in `LoginBrandPanel`.
   */
  asideComponent?: Component
  /**
   * Replace the split-layout right content area (heading + modules). Falls
   * back to the `#content` slot, then the built-in form body.
   */
  contentComponent?: Component
}

const props = withDefaults(defineProps<Props>(), {
  module: 'pwd-login',
  layout: 'wave',
  brand: 'Tnzi Admin',
  brandIcon: undefined,
  brandIconSize: 56,
  tagline: undefined,
  taglineSub: undefined,
  copyright: undefined,
  translate: undefined,
  callbacks: () => ({}),
  demoAccounts: () => [],
  thirdParty: () => [],
  qrComponent: undefined,
  moduleLabels: () => ({}),
  onToggleModule: undefined,
  transitionName: 'fade-slide',
  showLangSwitch: false,
  showThemeSwitch: true,
  features: () => DEFAULT_LOGIN_FEATURES,
  asideComponent: undefined,
  contentComponent: undefined,
})

const theme = useTheme()

function t(key: string, fallback?: string): string {
  if (props.translate) return props.translate(key, fallback)
  return fallback ?? key
}

const DEFAULT_LABELS: Record<LoginModule, { key: string; fallback: string }> = {
  'pwd-login': { key: 'admin.login.label.pwdLogin', fallback: 'Password Login' },
  'code-login': { key: 'admin.login.label.codeLogin', fallback: 'Code Login' },
  register: { key: 'admin.login.label.register', fallback: 'Register' },
  'reset-pwd': { key: 'admin.login.label.resetPwd', fallback: 'Reset Password' },
  'bind-wechat': { key: 'admin.login.label.bindWechat', fallback: 'Bind WeChat' },
  'two-factor': { key: 'admin.login.label.twoFactor', fallback: 'Two-Factor Verification' },
}

const activeLabel = computed(() => {
  const override = props.moduleLabels[props.module]
  if (override) return override
  const def = DEFAULT_LABELS[props.module]
  return t(def.key, def.fallback)
})

const activeComponent = computed(() => props.moduleComponents[props.module])

const isDark = computed(() => theme.resolvedMode.value === 'dark')

// Soybean's bg recipe: mixColor('#ffffff', themeColor, dark ? 0.5 : 0.2).
// In dark mode soybean additionally substitutes `themeColor` with the 600-step
// palette tint of the primary so the wash stays distinguishable from background.
const bgPrimary = computed(() => theme.settings.value.colors.primary)
const bgColor = computed(() =>
  mixColor('#ffffff', bgPrimary.value, isDark.value ? 0.5 : 0.2),
)
const waveColor = computed(() =>
  isDark.value ? getPaletteColorByNumber(bgPrimary.value, 600) : bgPrimary.value,
)

const resolvedTagline = computed(
  () => props.tagline ?? t('admin.login.tagline', 'The batteries-included admin framework'),
)
const resolvedTaglineSub = computed(
  () => props.taglineSub ?? t('admin.login.taglineSub', 'Preset pages · Flexible layouts · Themeable'),
)
const resolvedCopyright = computed(
  () => props.copyright ?? `© ${new Date().getFullYear()} ${props.brand}`,
)

// ---- QR sign-in toggle (corner fold, only when a qrComponent is supplied) ----
const qrMode = ref(false)
const qrCornerVisible = computed(
  () => !!props.qrComponent && (props.module === 'pwd-login' || props.module === 'code-login'),
)
watch(
  () => props.module,
  () => {
    qrMode.value = false
  },
)
const qrToggleTitle = computed(() =>
  qrMode.value
    ? t('admin.login.qr.toForm', 'Account login')
    : t('admin.login.qr.toQr', 'QR code login'),
)

// ---- split-layout heading (welcome copy on pwd-login, module label elsewhere) ----
const splitHeading = computed(() => {
  if (qrMode.value) return t('admin.login.qr.title', 'QR Code Login')
  if (props.module === 'pwd-login') return t('admin.login.welcome', 'Welcome back!')
  return activeLabel.value
})
const splitHeadingSub = computed(() => {
  if (qrMode.value) return t('admin.login.qr.hint', 'Scan with your mobile app to sign in')
  if (props.module === 'pwd-login') {
    return t('admin.login.welcomeSub', 'Please enter your details to continue')
  }
  return ''
})
const waveHeading = computed(() =>
  qrMode.value ? t('admin.login.qr.title', 'QR Code Login') : activeLabel.value,
)

// Phase I.7.5 - outstanding 2FA challenge state. PwdLogin / CodeLogin
// callbacks push into this via `helpers.setTwoFactorRequired(...)`, and
// the watcher below auto-toggles the shell to the `two-factor` module
// so the consumer's callback doesn't have to know about route names.
const pendingTwoFactor = ref<TwoFactorChallenge | null>(null)
// Adaptive login captcha - the `pwdLogin` callback pushes into this via
// `helpers.setCaptchaRequired(...)` when the backend replies
// `IDENTITY_CAPTCHA_REQUIRED`; PwdLogin reveals + seeds its captcha field.
const pendingCaptcha = ref<LoginCaptchaData | null>(null)
const helpers = {
  setTwoFactorRequired: (c: TwoFactorChallenge) => {
    pendingTwoFactor.value = c
  },
  clearTwoFactor: () => {
    pendingTwoFactor.value = null
  },
  setCaptchaRequired: (c: LoginCaptchaData) => {
    pendingCaptcha.value = c
  },
  clearCaptcha: () => {
    pendingCaptcha.value = null
  },
}
watch(pendingTwoFactor, (challenge) => {
  if (challenge) props.onToggleModule?.('two-factor')
})

// Layout-derived form chrome hints - `reactive` unwraps the computeds so
// modules can read `ui.labeled` / `ui.pill` as plain reactive booleans.
const ui = reactive({
  labeled: computed(() => props.layout === 'split'),
  pill: computed(() => props.layout !== 'split'),
}) as LoginUiStyle

// Live form-interaction signals - PwdLogin writes, the split layout's
// animated characters react (lean in while typing, look away from a
// visible password, sneak the occasional peek).
const scene: LoginSceneState = reactive({
  typing: false,
  passwordVisible: false,
  passwordLength: 0,
})

// The staggered entrance animation must only play on first load. Without
// this flag the `.t-login-stagger > *` animation re-runs on every module
// switch (the Transition creates a fresh DOM node), stacking its 0.12s
// delay + 0.55s duration on top of the fade-slide and making "Code login"
// feel ~1s laggy. After the longest stagger chain finishes we disable it.
const entered = ref(false)
onMounted(() => {
  window.setTimeout(() => {
    entered.value = true
  }, 1100)
})

// Provide the login context so modules can translate / toggle / submit.
const loginContext: LoginContext = {
  translate: t,
  toggleLoginModule: (name: LoginModule) => {
    // Clearing the 2FA flag when the user navigates away from the
    // two-factor module mid-flow keeps the next attempt clean.
    if (name !== 'two-factor') pendingTwoFactor.value = null
    // The adaptive login captcha only lives on pwd-login; a module switch
    // starts fresh (a stale captchaId would be rejected on return anyway).
    pendingCaptcha.value = null
    props.onToggleModule?.(name)
  },
  callbacks: props.callbacks,
  demoAccounts: props.demoAccounts,
  ui,
  thirdParty: props.thirdParty,
  features: props.features,
  scene,
  pendingTwoFactor,
  pendingCaptcha,
  helpers,
}
provideLoginContext(loginContext)
</script>

<template>
  <!-- ======================== wave layout (方案 A) ======================== -->
  <div
    v-if="layout === 'wave'"
    data-test="t-login-page"
    class="t-login t-login--wave relative size-full min-h-screen flex-center overflow-hidden transition-colors duration-300"
    :class="{ 't-login--dark': isDark, 't-login--entered': entered }"
    :style="{ backgroundColor: bgColor }"
  >
    <span class="t-login__blob t-login__blob--a" aria-hidden="true" />
    <span class="t-login__blob t-login__blob--b" aria-hidden="true" />
    <div data-test="t-login-page-waves" class="absolute inset-0 z-1 pointer-events-none">
      <LoginWaves :color="waveColor" :opacity="isDark ? 0.7 : 1" />
    </div>
    <NCard :bordered="false" class="t-login__card relative z-4 w-auto rd-16px shadow-lg t-login-page__card">
      <button
        v-if="qrCornerVisible"
        type="button"
        data-test="t-login-page-qr-toggle"
        class="t-login__qr-corner"
        :title="qrToggleTitle"
        :aria-label="qrToggleTitle"
        @click="qrMode = !qrMode"
      >
        <TSvgIcon :icon="qrMode ? 'mdi:monitor' : 'mdi:qrcode'" :size="18" />
      </button>
      <div class="t-login-page__form t-login-stagger">
        <header class="flex-y-center justify-between gap-12px" :class="{ 'pr-40px': qrCornerVisible }">
          <slot name="brand-icon">
            <TSvgIcon :icon="brandIcon" :size="brandIconSize" class="text-primary" />
          </slot>
          <h3 data-test="t-login-page-brand" class="m-0 text-28px text-primary font-500 tracking-tight lt-sm:text-22px">
            {{ brand }}
          </h3>
          <div data-test="t-login-page-toolbar" class="i-flex-col items-end gap-1">
            <slot name="toolbar">
              <TThemeSchemaSwitch v-if="showThemeSwitch" :translate="t" class="text-20px lt-sm:text-18px" />
              <TLangSwitch v-if="showLangSwitch" :translate="t" class="text-20px lt-sm:text-18px" />
            </slot>
          </div>
        </header>
        <main class="pt-24px">
          <h3 data-test="t-login-page-module-label" class="m-0 text-18px text-primary font-500">{{ waveHeading }}</h3>
          <div class="pt-24px">
            <div v-if="qrMode && qrComponent" class="t-login__qr" data-test="t-login-page-qr-panel">
              <div class="t-login__qr-frame">
                <component :is="qrComponent" />
              </div>
              <p class="t-login__qr-hint">{{ t('admin.login.qr.hint', 'Scan with your mobile app to sign in') }}</p>
            </div>
            <Transition v-else :name="transitionName" mode="out-in" appear>
              <component :is="activeComponent" :key="module" />
            </Transition>
          </div>
        </main>
      </div>
    </NCard>
    <p v-if="resolvedCopyright" class="t-login__copyright">{{ resolvedCopyright }}</p>
  </div>

  <!-- ======================== split layout (方案 B) ======================== -->
  <div
    v-else
    data-test="t-login-page"
    class="t-login t-login--split"
    :class="{ 't-login--dark': isDark, 't-login--entered': entered }"
  >
    <div class="t-login__panel">
      <component :is="asideComponent" v-if="asideComponent" />
      <slot v-else name="aside">
        <LoginBrandPanel
          :brand="brand"
          :brand-icon="brandIcon"
          :tagline="resolvedTagline"
          :tagline-sub="resolvedTaglineSub"
          :copyright="resolvedCopyright"
          :scene="scene"
        />
      </slot>
    </div>
    <div
      data-test="t-login-page-toolbar"
      class="t-login__pane-toolbar"
      :class="{ 't-login__pane-toolbar--offset': qrCornerVisible }"
    >
      <slot name="toolbar">
        <TThemeSchemaSwitch v-if="showThemeSwitch" :translate="t" class="text-20px lt-sm:text-18px" />
        <TLangSwitch v-if="showLangSwitch" :translate="t" class="text-20px lt-sm:text-18px" />
      </slot>
    </div>
    <div class="t-login__pane">
      <button
        v-if="qrCornerVisible"
        type="button"
        data-test="t-login-page-qr-toggle"
        class="t-login__qr-corner t-login__qr-corner--square"
        :title="qrToggleTitle"
        :aria-label="qrToggleTitle"
        @click="qrMode = !qrMode"
      >
        <TSvgIcon :icon="qrMode ? 'mdi:monitor' : 'mdi:qrcode'" :size="18" />
      </button>
      <component :is="contentComponent" v-if="contentComponent" class="t-login__form" />
      <slot v-else name="content">
        <div class="t-login__form t-login-stagger">
          <div class="t-login__form-head">
            <h2 data-test="t-login-page-module-label" class="t-login__form-title">{{ splitHeading }}</h2>
            <p v-if="splitHeadingSub" class="t-login__form-sub">{{ splitHeadingSub }}</p>
          </div>
          <div v-if="qrMode && qrComponent" class="t-login__qr" data-test="t-login-page-qr-panel">
            <div class="t-login__qr-frame">
              <component :is="qrComponent" />
            </div>
          </div>
          <Transition v-else :name="transitionName" mode="out-in" appear>
            <component :is="activeComponent" :key="module" />
          </Transition>
        </div>
      </slot>
    </div>
  </div>
</template>

<style scoped>
/* ---------------- wave layout ---------------- */
.t-login--wave .t-login__card {
  position: relative;
  overflow: hidden;
  margin: 0 16px;
  z-index: 4;
  animation: t-login-enter 0.7s cubic-bezier(0.16, 1, 0.3, 1) both;
}
.t-login-page__form {
  width: 400px;
  max-width: 100%;
}
.t-login__blob {
  position: absolute;
  border-radius: 50%;
  pointer-events: none;
}
.t-login__blob--a {
  width: 420px;
  height: 420px;
  left: 6%;
  top: -12%;
  background: rgba(255, 255, 255, 0.55);
  filter: blur(60px);
  animation: t-login-drift 13s ease-in-out infinite;
}
.t-login__blob--b {
  width: 360px;
  height: 360px;
  right: 4%;
  top: 14%;
  background: rgb(var(--tnzi-primary-rgb) / 0.22);
  filter: blur(70px);
  animation: t-login-drift 17s ease-in-out infinite reverse;
}
.t-login--dark .t-login__blob--a {
  background: rgb(var(--tnzi-primary-rgb) / 0.1);
}
.t-login--dark .t-login__blob--b {
  background: rgb(var(--tnzi-primary-rgb) / 0.16);
}
.t-login__copyright {
  position: absolute;
  bottom: 12px;
  left: 0;
  right: 0;
  z-index: 2;
  margin: 0;
  text-align: center;
  font-size: 12px;
  color: rgb(var(--tnzi-base-text-rgb) / 0.5);
  pointer-events: none;
}

/* ---------------- split layout ---------------- */
.t-login--split {
  position: relative;
  display: flex;
  width: 100%;
  min-height: 100vh;
  overflow: hidden;
  background: var(--tnzi-container-bg, #fff);
}
.t-login__panel {
  /* Equal split - the brand panel and the form pane each take half. */
  width: 50%;
  min-width: 360px;
  flex: none;
}
.t-login__pane {
  position: relative;
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 48px 24px;
  box-sizing: border-box;
}
.t-login__pane-toolbar {
  /* Root-level absolute: sits over the form pane's top-right on desktop,
     over the brand strip (white icons) on phones. */
  position: absolute;
  top: 24px;
  right: 32px;
  z-index: 6;
  display: inline-flex;
  align-items: center;
  gap: 12px;
}
.t-login__pane-toolbar--offset {
  right: 76px;
}
.t-login__form {
  width: 380px;
  max-width: 100%;
  display: flex;
  flex-direction: column;
  gap: 18px;
}
.t-login__form-head {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-login__form-title {
  margin: 0;
  font-size: 28px;
  font-weight: 650;
  letter-spacing: -0.5px;
  color: var(--tnzi-base-text);
}
.t-login__form-sub {
  margin: 0;
  font-size: 14.5px;
  color: var(--tnzi-base-text-muted);
}

@media (max-width: 767px) {
  .t-login--split {
    flex-direction: column;
  }
  .t-login__panel {
    width: 100%;
    min-width: 0;
    height: auto;
    flex: none;
  }
  .t-login__pane {
    align-items: flex-start;
    padding: 28px 20px 24px;
  }
  /* The toolbar sits on the brand strip's gradient - flip icons to white. */
  .t-login__pane-toolbar {
    top: 14px;
    right: 14px;
    color: #fff;
  }
  .t-login__pane-toolbar--offset {
    right: 14px;
  }
  .t-login__qr-corner--square {
    display: none;
  }
  .t-login__form {
    padding-top: 4px;
  }
}

/* ---------------- corner QR toggle + panel (both layouts) ---------------- */
.t-login__qr-corner {
  position: absolute;
  top: 0;
  right: 0;
  z-index: 5;
  width: 56px;
  height: 56px;
  border: none;
  padding: 0;
  cursor: pointer;
  background: linear-gradient(45deg, transparent 50%, var(--tnzi-primary) 50%);
  border-radius: 0 16px 0 0;
  transition: filter 0.2s;
}
.t-login__qr-corner--square {
  border-radius: 0;
}
.t-login__qr-corner:hover {
  filter: brightness(0.92);
}
.t-login__qr-corner :deep(svg),
.t-login__qr-corner :deep(.iconify) {
  position: absolute;
  top: 7px;
  right: 7px;
  color: #fff;
}
.t-login__qr {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  padding: 10px 0 4px;
}
.t-login__qr-frame {
  padding: 14px;
  background: #fff;
  border-radius: 12px;
  border: 1px solid var(--tnzi-border);
  box-shadow: 0 4px 16px rgba(0, 21, 41, 0.06);
}
.t-login__qr-hint {
  margin: 0;
  font-size: 13.5px;
  text-align: center;
  color: var(--tnzi-base-text-muted);
}

/* ---------------- responsive form width (wave card) ---------------- */
@media (max-width: 767px) {
  .t-login-page__form {
    width: 360px;
  }
}
@media (max-width: 639px) {
  .t-login-page__form {
    width: min(340px, calc(100vw - 64px));
  }
  .t-login--wave .t-login__card :deep(.n-card__content) {
    padding: 18px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .t-login__blob,
  .t-login--wave .t-login__card {
    animation: none;
  }
}
</style>

<!-- Shared entrance stagger - unscoped so LoginBrandPanel can reuse it. -->
<style>
.t-login-stagger > * {
  animation: t-login-enter 0.55s cubic-bezier(0.16, 1, 0.3, 1) both;
}
.t-login-stagger > *:nth-child(1) {
  animation-delay: 0.05s;
}
.t-login-stagger > *:nth-child(2) {
  animation-delay: 0.12s;
}
.t-login-stagger > *:nth-child(3) {
  animation-delay: 0.19s;
}
.t-login-stagger > *:nth-child(4) {
  animation-delay: 0.26s;
}
.t-login-stagger > *:nth-child(5) {
  animation-delay: 0.33s;
}
.t-login-stagger > *:nth-child(6) {
  animation-delay: 0.4s;
}
@keyframes t-login-enter {
  from {
    opacity: 0;
    transform: translateY(16px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}
/* First-load only: once the page has entered, module switches must not
   replay the stagger (its delay made form switches feel laggy). */
.t-login--entered .t-login-stagger > * {
  animation: none;
}
@media (prefers-reduced-motion: reduce) {
  .t-login-stagger > * {
    animation: none;
  }
}
</style>
