<script setup lang="ts">
/**
 * `TLoginPage` — soybean-style login page shell (Phase I.7.2 unocss rewrite).
 *
 * Mirrors `soybean-admin-example/src/views/_builtin/login/index.vue` (90 lines)
 * 1:1, including the unocss atom vocabulary so visual parity holds without
 * post-translation drift. Single layout: NCard centered on a brand-tinted
 * background, `TWaveBg` underneath, brand header (logo + title + toolbar
 * slot stacked top-right), Transition-wrapped `<component :is>`.
 *
 * The shell exposes a {@link LoginContext} via `provide()` so each module can
 * `useLoginContext()` to translate, switch sibling modules, and call the
 * consumer-supplied auth callbacks.
 */
import { computed, ref, watch, type Component } from 'vue'
import { NCard } from 'naive-ui'
import { useTheme, getPaletteColorByNumber, mixColor } from '@tnzi/ui'
import TWaveBg from '../display/TWaveBg.vue'
import TSvgIcon from '../display/TSvgIcon.vue'
import TThemeSchemaSwitch from '../utility/TThemeSchemaSwitch.vue'
import TLangSwitch from '../utility/TLangSwitch.vue'
import {
  provideLoginContext,
  type LoginContext,
  type LoginModule,
  type LoginCallbacks,
  type LoginDemoAccount,
  type TwoFactorChallenge,
} from '../../pages/login/useLoginContext'

interface Props {
  /** Active module. Defaults to `'pwd-login'`. */
  module?: LoginModule
  /** Module components map (5 entries: pwd-login / code-login / register / reset-pwd / bind-wechat). */
  moduleComponents: Record<LoginModule, Component>
  /** Brand title shown next to the logo. */
  brand?: string
  /** Iconify icon name for the brand logo. */
  brandIcon?: string
  /** Pixel size of the brand logo. Default 56 (matches soybean's `size-64px lt-sm:size-48px` baseline). */
  brandIconSize?: number
  /** Translation function — fallback returns `fallback ?? key`. */
  translate?: (key: string, fallback?: string) => string
  /** Auth callbacks passed down to modules via `useLoginContext()`. */
  callbacks?: LoginCallbacks
  /** Demo account quick-fill buttons (rendered by `PwdLoginModule`). */
  demoAccounts?: LoginDemoAccount[]
  /**
   * Module display labels keyed by module name. Each module title renders
   * above the form body. Defaults below match soybean's `loginModuleRecord`.
   */
  moduleLabels?: Partial<Record<LoginModule, string>>
  /**
   * Called by modules (via `useLoginContext().toggleLoginModule`) to switch
   * the active module. Page-level consumers wire this to
   * `router.replace({ path: '/login/' + name })`.
   */
  onToggleModule?: (name: LoginModule) => void
  /** CSS transition `name` for the `<Transition>` wrapper. Default `'fade-slide'`. */
  transitionName?: string
}

const props = withDefaults(defineProps<Props>(), {
  module: 'pwd-login',
  brand: 'Tnzi Admin',
  brandIcon: undefined,
  brandIconSize: 56,
  translate: undefined,
  callbacks: () => ({}),
  demoAccounts: () => [],
  moduleLabels: () => ({}),
  onToggleModule: undefined,
  transitionName: 'fade-slide',
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

// Soybean's bg recipe: mixColor('#ffffff', themeColor, dark ? 0.5 : 0.2).
// In dark mode soybean additionally substitutes `themeColor` with the 600-step
// palette tint of the primary so the wash stays distinguishable from background.
const bgPrimary = computed(() => theme.settings.value.colors.primary)
const bgColor = computed(() =>
  mixColor('#ffffff', bgPrimary.value, theme.resolvedMode.value === 'dark' ? 0.5 : 0.2),
)
const waveColor = computed(() =>
  theme.resolvedMode.value === 'dark'
    ? getPaletteColorByNumber(bgPrimary.value, 600)
    : bgPrimary.value,
)

// Phase I.7.5 — outstanding 2FA challenge state. PwdLogin / CodeLogin
// callbacks push into this via `helpers.setTwoFactorRequired(...)`, and
// the watcher below auto-toggles the shell to the `two-factor` module
// so the consumer's callback doesn't have to know about route names.
const pendingTwoFactor = ref<TwoFactorChallenge | null>(null)
const helpers = {
  setTwoFactorRequired: (c: TwoFactorChallenge) => {
    pendingTwoFactor.value = c
  },
  clearTwoFactor: () => {
    pendingTwoFactor.value = null
  },
}
watch(pendingTwoFactor, (challenge) => {
  if (challenge) props.onToggleModule?.('two-factor')
})

// Provide the login context so modules can translate / toggle / submit.
const loginContext: LoginContext = {
  translate: t,
  toggleLoginModule: (name: LoginModule) => {
    // Clearing the 2FA flag when the user navigates away from the
    // two-factor module mid-flow keeps the next attempt clean.
    if (name !== 'two-factor') pendingTwoFactor.value = null
    props.onToggleModule?.(name)
  },
  callbacks: props.callbacks,
  demoAccounts: props.demoAccounts,
  pendingTwoFactor,
  helpers,
}
provideLoginContext(loginContext)
</script>

<template>
  <div
    data-test="t-login-page"
    class="relative size-full min-h-screen flex-center overflow-hidden transition-colors duration-300"
    :style="{ backgroundColor: bgColor }"
  >
    <TWaveBg data-test="t-login-page-waves" :theme-color="waveColor" />
    <NCard :bordered="false" class="relative z-4 w-auto rd-12px shadow-lg t-login-page__card">
      <!-- Width steps across breakpoints so the form fits common phone
           widths cleanly:
             - default (>= sm) → 400px
             - sm (640-767)    → 360px
             - lt-sm (< 640)   → calc(100vw - 32px) capped at 340
           The old binary 400/300 split left an awkward 16-40px gap on
           360-400px phones (iPhone 14 = 390). -->
      <div class="t-login-page__form">
        <header class="flex-y-center justify-between gap-12px">
          <slot name="brand-icon">
            <TSvgIcon :icon="brandIcon" :size="brandIconSize" class="text-primary" />
          </slot>
          <h3 data-test="t-login-page-brand" class="m-0 text-28px text-primary font-500 tracking-tight lt-sm:text-22px">
            {{ brand }}
          </h3>
          <div data-test="t-login-page-toolbar" class="i-flex-col items-end gap-1">
            <slot name="toolbar">
              <TThemeSchemaSwitch :translate="t" class="text-20px lt-sm:text-18px" />
              <TLangSwitch :translate="t" class="text-20px lt-sm:text-18px" />
            </slot>
          </div>
        </header>
        <main class="pt-24px">
          <h3 data-test="t-login-page-module-label" class="m-0 text-18px text-primary font-500">{{ activeLabel }}</h3>
          <div class="pt-24px">
            <Transition :name="transitionName" mode="out-in" appear>
              <component :is="activeComponent" :key="module" />
            </Transition>
          </div>
        </main>
      </div>
    </NCard>
  </div>
</template>

<style scoped>
.t-login-page__card {
  /* On mobile portrait the NCard default ~28px inner padding eats
     into the form width; tighten so the inputs still get >280px of
     usable space at iPhone SE (375). */
  margin: 0 16px;
}
.t-login-page__form {
  width: 400px;
  max-width: 100%;
}
@media (max-width: 767px) {
  .t-login-page__form {
    width: 360px;
  }
}
@media (max-width: 639px) {
  .t-login-page__form {
    width: min(340px, calc(100vw - 64px));
  }
  .t-login-page__card :deep(.n-card__content) {
    padding: 18px;
  }
}
</style>
