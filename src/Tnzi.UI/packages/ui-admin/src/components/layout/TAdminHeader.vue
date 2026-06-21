<template>
  <header class="t-admin-header" :class="{ 't-admin-header--fixed': fixed }">
    <!-- Left region: toggler + breadcrumb -->
    <div class="t-admin-header__left">
      <div v-if="$slots.logo" class="t-admin-header__logo">
        <slot name="logo" />
      </div>
      <NTooltip v-if="showToggler" placement="bottom" trigger="hover">
        <template #trigger>
          <button
            class="t-admin-header__icon-btn t-admin-header__toggler"
            :aria-label="appStore.siderCollapse ? 'Expand sider' : 'Collapse sider'"
            @click="appStore.toggleSiderCollapse()"
          >
            <Icon
              :icon="appStore.siderCollapse ? 'mdi:menu' : 'mdi:menu-open'"
              width="20"
              height="20"
            />
          </button>
        </template>
        {{ appStore.siderCollapse ? 'Expand' : 'Collapse' }}
      </NTooltip>
      <div v-if="$slots.breadcrumb" class="t-admin-header__breadcrumb">
        <slot name="breadcrumb" />
      </div>
    </div>

    <!-- Center region: top menu (horizontal & hybrid layout modes) -->
    <div v-if="$slots.menu" class="t-admin-header__menu">
      <slot name="menu" />
    </div>

    <!-- Right region: action buttons + user/notif slots. Phase H2 K1:
         every icon button is wrapped in NTooltip (matches soybean's
         `custom/button-icon.vue` pattern) so new users can discover
         what each button does on hover.

         Responsive: below `overflowMenuBreakpoint` (default `xs`, <640)
         all icon buttons except language switch + user/notification
         slots collapse into a single "···" overflow dropdown. Language
         stays inline because dropdown-inside-dropdown trips Naive UI's
         focus trap, and user/notification are the highest-frequency
         affordances. -->
    <div class="t-admin-header__right">
      <template v-if="!shouldUseOverflow">
        <NTooltip v-if="showSearch" placement="bottom" trigger="hover">
          <template #trigger>
            <button
              class="t-admin-header__icon-btn t-admin-header__search"
              aria-label="Search"
              @click="emit('openSearch')"
            >
              <Icon icon="mdi:magnify" width="20" height="20" />
            </button>
          </template>
          Search (Ctrl+K)
        </NTooltip>
        <NTooltip v-if="showReload" placement="bottom" trigger="hover">
          <template #trigger>
            <button
              class="t-admin-header__icon-btn t-admin-header__reload"
              aria-label="Reload"
              @click="appStore.reloadPage()"
            >
              <Icon icon="mdi:refresh" width="20" height="20" />
            </button>
          </template>
          Reload
        </NTooltip>
        <NTooltip v-if="fullscreenButtonVisible" placement="bottom" trigger="hover">
          <template #trigger>
            <button
              class="t-admin-header__icon-btn t-admin-header__fullscreen"
              :aria-label="isFullscreen ? 'Exit fullscreen' : 'Enter fullscreen'"
              @click="toggleFullscreen()"
            >
              <Icon
                :icon="isFullscreen ? 'mdi:fullscreen-exit' : 'mdi:fullscreen'"
                width="20"
                height="20"
              />
            </button>
          </template>
          {{ isFullscreen ? 'Exit fullscreen' : 'Fullscreen' }}
        </NTooltip>
        <NTooltip v-if="showThemeSchemaBtn" placement="bottom" trigger="hover">
          <template #trigger>
            <button
              class="t-admin-header__icon-btn t-admin-header__theme-schema"
              :aria-label="`Theme: ${themeSchemaTooltip}`"
              @click="cycleThemeSchema"
            >
              <Icon :icon="themeSchemaIcon" width="20" height="20" />
            </button>
          </template>
          Theme: {{ themeSchemaTooltip }}
        </NTooltip>
        <NTooltip v-if="showThemeBtn" placement="bottom" trigger="hover">
          <template #trigger>
            <button
              class="t-admin-header__icon-btn t-admin-header__theme"
              aria-label="Theme settings"
              @click="emit('openThemeDrawer')"
            >
              <Icon icon="mdi:palette-outline" width="20" height="20" />
            </button>
          </template>
          Theme settings
        </NTooltip>
      </template>
      <NDropdown
        v-else
        :options="overflowDropdownOptions"
        trigger="click"
        @select="onOverflowSelect"
      >
        <button
          class="t-admin-header__icon-btn t-admin-header__overflow"
          aria-label="More actions"
        >
          <Icon icon="mdi:dots-vertical" width="20" height="20" />
        </button>
      </NDropdown>
      <NDropdown
        v-if="showLangSwitch"
        :options="langOptions"
        trigger="hover"
        @select="onLangSelect"
      >
        <button
          class="t-admin-header__icon-btn t-admin-header__lang"
          aria-label="Language"
        >
          <Icon icon="mdi:translate" width="20" height="20" />
        </button>
      </NDropdown>
      <div v-if="$slots.chat" class="t-admin-header__chat">
        <slot name="chat" />
      </div>
      <div v-if="$slots.notification" class="t-admin-header__notification">
        <slot name="notification" />
      </div>
      <div v-if="$slots.user" class="t-admin-header__user">
        <slot name="user" />
      </div>
    </div>
  </header>
</template>

<script setup lang="ts">
import { computed, h, inject } from 'vue'
import { useFullscreen } from '@vueuse/core'
import { Icon } from '@iconify/vue'
import { NTooltip, NDropdown, type DropdownOption } from 'naive-ui'
import { THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { useBreakpoint } from '../../headless/useBreakpoint'

interface Props {
  title?: string
  fixed?: boolean
  showToggler?: boolean
  showSearch?: boolean
  showFullscreen?: boolean
  showThemeBtn?: boolean
  /** Phase H1 B1: Sun/Moon theme schema toggle (cycles light↔dark↔auto). */
  showThemeSchemaBtn?: boolean
  showLangSwitch?: boolean
  showReload?: boolean
  /**
   * Below this breakpoint the right-side action buttons fold into a
   * single "···" overflow dropdown so the header doesn't clip on phones.
   *   'xs'    → fold below 640px (default; tablets keep the inline row)
   *   'sm'    → fold below 768px (more aggressive — phablets fold too)
   *   'never' → always inline (legacy behaviour, may overflow on phones)
   */
  overflowMenuBreakpoint?: 'xs' | 'sm' | 'never'
}

const props = withDefaults(defineProps<Props>(), {
  title: 'Tnzi Admin',
  fixed: true,
  showToggler: true,
  showSearch: true,
  showFullscreen: true,
  showThemeBtn: true,
  showThemeSchemaBtn: true,
  showLangSwitch: true,
  showReload: true,
  overflowMenuBreakpoint: 'xs',
})

const emit = defineEmits<{
  openSearch: []
  openThemeDrawer: []
  localeChange: [locale: 'en' | 'zh-cn']
}>()

const appStore = useAdminAppStore()
const fullscreenAdapter = useFullscreen()
const { isFullscreen, toggle: toggleFullscreen } = fullscreenAdapter
const bp = useBreakpoint()

// Fullscreen API is unreliable on mobile browsers (iOS Safari blocks
// `Element.requestFullscreen`, several Android WebViews silently no-op).
// Hide the button when the API isn't supported OR the device is a
// touchscreen so users don't tap a dead control.
// `isSupported` may be undefined under happy-dom (the test environment
// lacks Fullscreen API entirely) — fall back to `true` so desktop test
// mounts still see the inline button, and trust the touch probe to hide
// it where it actually matters.
const fullscreenButtonVisible = computed<boolean>(() => {
  if (!props.showFullscreen) return false
  if (bp.isTouch.value) return false
  const supported = (fullscreenAdapter as { isSupported?: { value?: boolean } }).isSupported
  if (supported && supported.value === false) return false
  return true
})

function setLocale(locale: 'en' | 'zh-cn') {
  appStore.setLocale(locale)
  emit('localeChange', locale)
}

/** Language options for the NDropdown — mirrors soybean's lang-switch.vue
 *  pattern (NDropdown :options + trigger=hover + @select). When a new
 *  locale is added, extend this list — TAdminHeader picks it up without
 *  template changes. Active option gets a checkmark icon via the `icon`
 *  render function (NDropdown calls it lazily per render). */
const langOptions = computed<DropdownOption[]>(() => [
  {
    key: 'zh-cn',
    label: '中文',
    icon: () =>
      appStore.locale === 'zh-cn'
        ? h(Icon, { icon: 'mdi:check', width: 14, height: 14 })
        : null,
  },
  {
    key: 'en',
    label: 'English',
    icon: () =>
      appStore.locale === 'en'
        ? h(Icon, { icon: 'mdi:check', width: 14, height: 14 })
        : null,
  },
])

function onLangSelect(key: string | number): void {
  const locale = key as 'en' | 'zh-cn'
  setLocale(locale)
}

/** Phase H1 B1: theme-schema toggle wiring. Reads the optional
 *  `@tnzi/ui` theme context; if installed, the button cycles
 *  light → dark → auto → light. If the context isn't installed
 *  (unit tests without the plugin), the button no-ops gracefully. */
const themeContext = inject<ThemeContext | undefined>(THEME_CONTEXT_KEY, undefined)
const themeMode = computed<'light' | 'dark' | 'auto'>(
  () => themeContext?.settings.value.mode ?? 'light',
)
const themeSchemaIcon = computed(() => {
  switch (themeMode.value) {
    case 'dark':
      return 'material-symbols:nightlight-rounded'
    case 'auto':
      return 'material-symbols:hdr-auto'
    case 'light':
    default:
      return 'material-symbols:sunny-rounded'
  }
})
const themeSchemaTooltip = computed(() => {
  switch (themeMode.value) {
    case 'dark':
      return 'Dark'
    case 'auto':
      return 'Auto'
    case 'light':
    default:
      return 'Light'
  }
})
function cycleThemeSchema(): void {
  if (!themeContext) return
  const next: 'light' | 'dark' | 'auto' =
    themeMode.value === 'light' ? 'dark' : themeMode.value === 'dark' ? 'auto' : 'light'
  themeContext.setMode(next)
}

// When the viewport is narrow enough that 5-7 inline icon buttons would
// crowd or wrap the header, collapse the action group into a single
// dropdown trigger. `:user` and `:notification` slots stay inline so the
// most important affordances (who am I, what's pending) remain visible.
const shouldUseOverflow = computed<boolean>(() => {
  if (props.overflowMenuBreakpoint === 'never') return false
  if (props.overflowMenuBreakpoint === 'sm') return bp.isSm.value
  return bp.isXs.value
})

interface OverflowAction {
  key: string
  label: string
  icon: string
  show: boolean
  handler: () => void
}

const overflowActions = computed<OverflowAction[]>(() => [
  {
    key: 'search',
    label: 'Search',
    icon: 'mdi:magnify',
    show: props.showSearch,
    handler: () => emit('openSearch'),
  },
  {
    key: 'reload',
    label: 'Reload',
    icon: 'mdi:refresh',
    show: props.showReload,
    handler: () => appStore.reloadPage(),
  },
  {
    key: 'fullscreen',
    label: isFullscreen.value ? 'Exit fullscreen' : 'Fullscreen',
    icon: isFullscreen.value ? 'mdi:fullscreen-exit' : 'mdi:fullscreen',
    show: fullscreenButtonVisible.value,
    handler: () => toggleFullscreen(),
  },
  {
    key: 'theme-schema',
    label: `Theme: ${themeSchemaTooltip.value}`,
    icon: themeSchemaIcon.value,
    show: props.showThemeSchemaBtn,
    handler: cycleThemeSchema,
  },
  {
    key: 'theme',
    label: 'Theme settings',
    icon: 'mdi:palette-outline',
    show: props.showThemeBtn,
    handler: () => emit('openThemeDrawer'),
  },
])

const overflowDropdownOptions = computed<DropdownOption[]>(() =>
  overflowActions.value
    .filter((a) => a.show)
    .map((a) => ({
      key: a.key,
      label: a.label,
      icon: () => h(Icon, { icon: a.icon, width: 16, height: 16 }),
    })),
)

function onOverflowSelect(key: string | number): void {
  const action = overflowActions.value.find((a) => a.key === key)
  action?.handler()
}

defineExpose({ setLocale })
</script>

<style scoped>
.t-admin-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  /* Phase H1 D1+B7: flex-shrink:0 so the header never collapses; +
     height transition so drawer-driven height changes animate. */
  flex-shrink: 0;
  height: var(--tnzi-admin-header-height, 56px);
  padding: 0 16px;
  background-color: var(--tnzi-admin-header-bg, var(--tnzi-container-bg));
  border-bottom: 1px solid var(--tnzi-border);
  /* Phase H1 B2: soybean header has a subtle shadow that lifts it
     above the content. Earlier annotation claimed "no box-shadow"
     was the parity — that was wrong. soybean uses `shadow-header`
     ≈ `0 1px 2px 0 rgb(0 21 41 / 8%)`. */
  box-shadow: 0 1px 2px 0 rgb(0 21 41 / 0.05);
  z-index: var(--tnzi-admin-z-header, 80);
  transition:
    height var(--tnzi-admin-motion-duration-base, 0.3s) var(--tnzi-admin-motion-ease-in-out, ease),
    background-color var(--tnzi-admin-motion-duration-fast, 0.15s) var(--tnzi-admin-motion-ease-in-out, ease),
    color var(--tnzi-admin-motion-duration-fast, 0.15s) var(--tnzi-admin-motion-ease-in-out, ease);
}
.t-admin-header--fixed {
  position: sticky;
  top: 0;
}
.t-admin-header__left {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-admin-header__logo {
  font-weight: 600;
  font-size: 16px;
  color: var(--tnzi-base-text);
  white-space: nowrap;
  padding-right: 4px;
}
.t-admin-header__icon-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  width: 36px;
  height: 36px;
  border: none;
  background: transparent;
  cursor: pointer;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  color: var(--tnzi-base-text-muted);
  transition:
    background-color var(--tnzi-admin-motion-duration-fast, 0.15s) var(--tnzi-admin-motion-ease-in-out, ease),
    color var(--tnzi-admin-motion-duration-fast, 0.15s) var(--tnzi-admin-motion-ease-in-out, ease),
    transform var(--tnzi-admin-motion-duration-fast, 0.15s) var(--tnzi-admin-motion-ease-out, ease);
}
.t-admin-header__icon-btn--text {
  width: auto;
  padding: 0 10px;
  font-size: 13px;
  font-weight: 500;
}
.t-admin-header__icon-btn:hover {
  background-color: var(--tnzi-admin-menu-item-hover-bg, rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08));
  color: var(--tnzi-primary);
}
.t-admin-header__icon-btn:active {
  transform: scale(0.94);
  background-color: var(--tnzi-admin-menu-item-active-bg, rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.12));
}
.t-admin-header__lang-label {
  font-size: 12px;
  line-height: 1;
}
.t-admin-header__breadcrumb {
  margin-left: 4px;
  min-width: 0;
  overflow: hidden;
}
.t-admin-header__menu {
  display: flex;
  align-items: center;
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  margin: 0 16px;
}
.t-admin-header__right {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}
.t-admin-header__chat,
.t-admin-header__notification,
.t-admin-header__user {
  display: flex;
  align-items: center;
  padding-left: 8px;
}
@media (max-width: 640px) {
  .t-admin-header__breadcrumb {
    display: none;
  }
}
</style>
