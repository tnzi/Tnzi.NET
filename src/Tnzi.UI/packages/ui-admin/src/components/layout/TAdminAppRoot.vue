<script setup lang="ts">
import { computed, defineComponent, h, inject, resolveComponent, useSlots } from 'vue'
import {
  NConfigProvider,
  NDialogProvider,
  NLoadingBarProvider,
  NMessageProvider,
  NNotificationProvider,
  darkTheme,
  type GlobalThemeOverrides,
} from 'naive-ui'
import { THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui'
import TAdminWindowHandles from './TAdminWindowHandles'

interface Props {
  /**
   * Override the auto-resolved naive theme. When omitted, the component
   * reads `useTheme().isDark` from the @tnzi/ui context (installed by
   * `createTnziUiAdmin()`) and picks `darkTheme` / null.
   */
  theme?: typeof darkTheme | null
  /**
   * Override the auto-resolved theme tokens. When omitted, the component
   * forwards `useTheme().naiveOverrides` so admin palette tokens
   * (primary / info / success / ...) get applied to all naive-ui
   * components rendered inside.
   */
  themeOverrides?: GlobalThemeOverrides
  /**
   * Render `<router-view />` inside the provider stack. Default `true` —
   * fits the typical admin App.vue use case. Set to `false` if you want
   * to provide your own content via the default slot (e.g. for non-router
   * shells or wrapped layouts).
   */
  routerView?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  theme: undefined,
  themeOverrides: undefined,
  routerView: true,
})

// Inject the theme context directly (not via useTheme()) so that this
// component still renders when no `@tnzi/ui` plugin is installed
// upstream — falling back to a null theme + empty overrides instead of
// throwing. `createTnziUiAdmin()` always installs a fallback context, so
// in the typical setup the injection succeeds.
const ctx = inject<ThemeContext | null>(THEME_CONTEXT_KEY, null)

const resolvedTheme = computed(() => {
  if (props.theme !== undefined) return props.theme
  if (!ctx) return null
  return ctx.isDark.value ? darkTheme : null
})

const resolvedOverrides = computed<GlobalThemeOverrides>(() => {
  if (props.themeOverrides) return props.themeOverrides
  if (!ctx) return {}
  return ctx.naiveOverrides.value
})

// Use a functional component for the inner content so the fallback path
// resolves `RouterView` at runtime. The naïve approach
// `<slot><router-view v-if="..." /></slot>` in the template triggers a
// Vue compile-time quirk that turns the v-if expression into a boolean
// VNode child, producing "Invalid VNode type: true (boolean)" warnings.
const slots = useSlots()
const Inner = defineComponent({
  name: 'TAdminAppRootInner',
  setup() {
    return () => {
      if (slots.default) return slots.default()
      if (!props.routerView) return null
      return h(resolveComponent('RouterView'))
    }
  },
})
</script>

<template>
  <NConfigProvider :theme="resolvedTheme" :theme-overrides="resolvedOverrides">
    <NLoadingBarProvider>
      <NMessageProvider>
        <NNotificationProvider>
          <NDialogProvider>
            <!-- Registers window.$message / $dialog / $notification /
                 $loadingBar so useCrudPage error toasts and the @tnzi/ui
                 window-handle adapters work without app-side wiring. -->
            <TAdminWindowHandles />
            <Inner />
          </NDialogProvider>
        </NNotificationProvider>
      </NMessageProvider>
    </NLoadingBarProvider>
  </NConfigProvider>
</template>
