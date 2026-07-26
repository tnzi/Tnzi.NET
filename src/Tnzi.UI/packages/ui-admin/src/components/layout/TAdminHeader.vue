<template>
  <header
    class="t-admin-header"
    :class="{
      't-admin-header--fixed': fixed,
      't-admin-header--inverted': surface === 'dark',
      't-admin-header--surface-light': surface === 'light',
    }"
  >
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
            <!-- soybean parity: animated `line-md` fold icon whose stroke
                 draws itself on mount. The `:key` remounts the icon on every
                 collapse toggle so the draw-in animation replays each click
                 (matches soybean's `<ButtonIcon :key="String(collapsed)">`).
                 fold-left = "click to collapse" (expanded), fold-right =
                 "click to expand" (collapsed). -->
            <Icon
              :key="String(appStore.siderCollapse)"
              :icon="appStore.siderCollapse ? 'line-md:menu-fold-right' : 'line-md:menu-fold-left'"
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
      <!-- Overflow trigger sits LEFT of the inline survivors - it holds the
           actions folded away from the left side of the row. -->
      <NDropdown
        v-if="overflowDropdownOptions.length > 0"
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
      <NTooltip v-if="showSearch && inlineActionKeys.has('search')" placement="bottom" trigger="hover">
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
      <NTooltip v-if="showReload && inlineActionKeys.has('reload')" placement="bottom" trigger="hover">
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
      <NTooltip v-if="fullscreenButtonVisible && inlineActionKeys.has('fullscreen')" placement="bottom" trigger="hover">
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
      <NTooltip v-if="showThemeSchemaBtn && inlineActionKeys.has('theme-schema')" placement="bottom" trigger="hover">
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
      <NTooltip v-if="showThemeBtn && inlineActionKeys.has('theme')" placement="bottom" trigger="hover">
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
      <NDropdown
        v-if="showLangSwitch"
        :options="langOptions"
        trigger="click"
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
   *   'sm'    → fold below 768px (more aggressive - phablets fold too)
   *   'never' → always inline (legacy behaviour, may overflow on phones)
   */
  overflowMenuBreakpoint?: 'xs' | 'sm' | 'never'
  /**
   * Cap on how many action buttons (everything except the user /
   * notification / chat slots) render on DESKTOP - the "···" overflow
   * trigger COUNTS toward the cap, so when folding is needed the row shows
   * `max - 1` real buttons plus the "···" holding the surplus (same dropdown
   * the mobile breakpoint uses). Survivors are picked right-to-left (the
   * ones closest to the user info win). The language switch hosts its own
   * dropdown and cannot nest inside the overflow (naive focus trap), so it
   * always renders inline - but it still occupies a slot so the cap holds.
   * `undefined` = no cap (classic behaviour). The shell passes `2` for the
   * horizontal / hybrid layouts where the top menu needs the header width.
   */
  maxInlineActions?: number
  /**
   * Surface tone when the header carries a custom background color.
   *   - `'dark'`  → dark surface + light foreground (inverted)
   *   - `'light'` → light surface + dark foreground (only needed under the
   *                 global dark mode, so a light header stays readable)
   *   - undefined → follow the global light/dark mode (no override)
   */
  surface?: 'dark' | 'light'
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
  maxInlineActions: undefined,
  surface: undefined,
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
// lacks Fullscreen API entirely) - fall back to `true` so desktop test
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

/** Language options for the NDropdown - mirrors soybean's lang-switch.vue
 *  pattern (NDropdown :options + trigger=hover + @select). When a new
 *  locale is added, extend this list - TAdminHeader picks it up without
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

/** Which action keys render inline. Mobile fold (shouldUseOverflow) keeps
 *  only the language switch inline (existing behaviour); a desktop
 *  `maxInlineActions` cap keeps the RIGHTMOST survivors (language included
 *  in the count - it always sits at the tail). When everything fits within
 *  the cap there is no "···" trigger and all render inline; once folding is
 *  needed the trigger itself occupies one slot, so `max - 1` buttons stay. */
const inlineActionKeys = computed<Set<string>>(() => {
  if (shouldUseOverflow.value) return new Set<string>()
  const enabled = overflowActions.value.filter((a) => a.show).map((a) => a.key)
  if (props.showLangSwitch) enabled.push('lang')
  const max = props.maxInlineActions
  if (max == null || enabled.length <= max) return new Set(enabled)
  const keep = Math.max(0, max - 1)
  return new Set(keep === 0 ? [] : enabled.slice(-keep))
})

const overflowDropdownOptions = computed<DropdownOption[]>(() =>
  overflowActions.value
    .filter((a) => a.show && !inlineActionKeys.value.has(a.key))
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
     was the parity - that was wrong. soybean uses `shadow-header`
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
/* Adaptive surface - a custom header background flips the header's foreground
   token set so its chrome (breadcrumb, action icons, user name) stays legible.
   Mirrors the sider's inverted/surface-light treatment. */
.t-admin-header--inverted {
  --tnzi-base-text: var(--tnzi-admin-header-fg, var(--tnzi-admin-inverted-text, rgba(255, 255, 255, 0.92)));
  --tnzi-base-text-muted: var(--tnzi-admin-inverted-text-muted, rgba(255, 255, 255, 0.6));
  --tnzi-border: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
  color: var(--tnzi-base-text);
  border-bottom-color: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
}
.t-admin-header--surface-light {
  --tnzi-base-text: var(--tnzi-admin-header-fg, var(--tnzi-admin-surface-light-text, rgba(0, 0, 0, 0.88)));
  --tnzi-base-text-muted: var(--tnzi-admin-surface-light-text-muted, rgba(0, 0, 0, 0.5));
  --tnzi-border: var(--tnzi-admin-surface-light-border, rgba(0, 0, 0, 0.1));
  color: var(--tnzi-base-text);
  border-bottom-color: var(--tnzi-admin-surface-light-border, rgba(0, 0, 0, 0.1));
}
/* The breadcrumb is a naive NBreadcrumb, which reads its own theme tokens
   (near-black in light mode) instead of --tnzi-base-text - on a dark custom
   header its labels would melt into the chrome (espresso/aubergine unified
   looks). Remap the breadcrumb tokens alongside the surface variant. */
.t-admin-header--inverted :deep(.n-breadcrumb) {
  --n-item-text-color: var(--tnzi-admin-header-fg, var(--tnzi-admin-inverted-text-muted, rgba(255, 255, 255, 0.65)));
  --n-item-text-color-hover: #ffffff;
  --n-item-text-color-pressed: #ffffff;
  --n-item-text-color-active: var(--tnzi-admin-header-fg, var(--tnzi-admin-inverted-text, rgba(255, 255, 255, 0.92)));
  --n-separator-color: var(--tnzi-admin-inverted-text-muted, rgba(255, 255, 255, 0.45));
  --n-item-color-hover: rgba(255, 255, 255, 0.08);
  --n-item-color-pressed: rgba(255, 255, 255, 0.12);
}
.t-admin-header--surface-light :deep(.n-breadcrumb) {
  --n-item-text-color: var(--tnzi-admin-header-fg, var(--tnzi-admin-surface-light-text-muted, rgba(0, 0, 0, 0.5)));
  --n-item-text-color-hover: rgba(0, 0, 0, 0.8);
  --n-item-text-color-pressed: rgba(0, 0, 0, 0.8);
  --n-item-text-color-active: var(--tnzi-admin-header-fg, var(--tnzi-admin-surface-light-text, rgba(0, 0, 0, 0.88)));
  --n-separator-color: var(--tnzi-admin-surface-light-text-muted, rgba(0, 0, 0, 0.35));
  --n-item-color-hover: rgba(0, 0, 0, 0.06);
  --n-item-color-pressed: rgba(0, 0, 0, 0.09);
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
/* Leading toggler: an icon button centers its 20px glyph in a 36px box, so on
   top of the header's 16px padding the glyph reads ~8px further in than the
   content below it. Pull the button back by that centering inset so its glyph
   lines up with the content's left edge. Scoped to `:first-child` so it only
   applies when the toggler leads the row (vertical / vertical-mix); in hybrid
   the brand logo precedes it and the spacing stays intact. */
.t-admin-header__left > .t-admin-header__toggler:first-child {
  margin-left: -8px;
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
/* Phone: reclaim horizontal room so the right cluster (overflow ··· + language
   + chat + bell + avatar) doesn't overflow and get clipped by the shell's
   `overflow:hidden` on narrow screens (≤360px). Tighter side gutter + no
   per-item left padding; the coarse-pointer rule keeps each target ≥44px. */
@media (max-width: 767px) {
  .t-admin-header {
    padding: 0 8px;
  }
  .t-admin-header__right {
    gap: 0;
  }
  .t-admin-header__chat,
  .t-admin-header__notification,
  .t-admin-header__user {
    padding-left: 0;
  }
}
@media (max-width: 640px) {
  .t-admin-header__breadcrumb {
    display: none;
  }
}
</style>
