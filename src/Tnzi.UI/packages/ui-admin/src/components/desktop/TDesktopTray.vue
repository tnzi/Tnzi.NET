<script setup lang="ts">
/**
 * `TDesktopTray` - the notification-area end of the taskbar.
 *
 * The desktop layout suppresses the admin header (a desktop that renders under
 * a page header is a widget, not a shell), so everything the header carried on
 * its right has to land somewhere. This is that somewhere, and it is the same
 * action set gated by the same `themeStore` flags - an app that hid the
 * language switch in `vertical` mode keeps it hidden here.
 *
 * The consumer-supplied affordances (chat, notification bell, user avatar) come
 * through the `default` slot rather than being imported: they are constructed
 * by whoever mounts the shell, out of that app's own config, and this component
 * has no business knowing what a bell is.
 *
 * Search is deliberately NOT here - it lives in the centre dock next to the
 * start button, where a desktop metaphor puts it.
 */
import { computed, h, inject, onBeforeUnmount, onMounted, ref } from 'vue'
import { useFullscreen } from '@vueuse/core'
import { Icon } from '@iconify/vue'
import { NDatePicker, NDropdown, NPopover, NTooltip, type DropdownOption } from 'naive-ui'
import { THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { useAdminThemeStore } from '../../stores/useAdminThemeStore'
import { useAdminDesktopStore } from '../../stores/useAdminDesktopStore'
import { useAdminShellActions } from '../../headless/admin-shell-actions'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { useAdminLocale } from '../../headless/useAdminLocale'

const props = defineProps<{
  /** Theme-drawer button visibility, forwarded from the shell's own gate so
   *  an app that hides theming does not get it back through the tray. */
  showThemeBtn?: boolean
  translate?: (key: string, fallback?: string) => string
}>()

const t = (key: string, fallback: string): string => props.translate?.(key, fallback) ?? fallback

const appStore = useAdminAppStore()
const themeStore = useAdminThemeStore()
const desktop = useAdminDesktopStore()
const shell = useAdminShellActions()
const bp = useBreakpoint()

// ── Clock ───────────────────────────────────────────────────────────────────
// 30s, not 60s: the display is minute-resolution, and a 60s tick landing just
// after a minute boundary would show a stale time for almost a full minute.
const now = ref(new Date())
let clockTimer: ReturnType<typeof setInterval> | undefined

onMounted(() => {
  clockTimer = setInterval(() => {
    now.value = new Date()
  }, 30_000)
})
onBeforeUnmount(() => {
  if (clockTimer) clearInterval(clockTimer)
})

// Tag comes from the locale registry, so a consumer-registered language
// formats its own clock and calendar rather than falling back to US English.
const locales = useAdminLocale()
const localeTag = computed(() => locales.intlTag.value)
const clock = computed(() =>
  new Intl.DateTimeFormat(localeTag.value, { hour: '2-digit', minute: '2-digit' }).format(now.value),
)
/** The tray shows only the time; the date is one hover or one click away. */
const clockFullDate = computed(() =>
  new Intl.DateTimeFormat(localeTag.value, { dateStyle: 'full' }).format(now.value),
)

/**
 * Month shown in the flyout.
 *
 * Its own state, seeded from today: paging to next month must not move the
 * clock, and the panel's selection is a browsing affordance, not a value the
 * shell does anything with.
 */
const calendarOpen = ref(false)
const calendarValue = ref<number>(Date.now())

function onCalendarOpen(open: boolean): void {
  calendarOpen.value = open
  // Reopening always lands on today rather than wherever it was left weeks ago.
  if (open) calendarValue.value = now.value.getTime()
}

// ── Theme schema ────────────────────────────────────────────────────────────
// Same wiring as the header's button: the `@tnzi/ui` theme context is optional,
// and without it the button no-ops rather than throwing in a bare mount.
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
    default:
      return 'material-symbols:sunny-rounded'
  }
})
const themeSchemaLabel = computed(() => {
  switch (themeMode.value) {
    case 'dark':
      return t('admin.desktop.tray.themeDark', 'Dark')
    case 'auto':
      return t('admin.desktop.tray.themeAuto', 'Auto')
    default:
      return t('admin.desktop.tray.themeLight', 'Light')
  }
})
function cycleThemeSchema(): void {
  if (!themeContext) return
  const next: 'light' | 'dark' | 'auto' =
    themeMode.value === 'light' ? 'dark' : themeMode.value === 'dark' ? 'auto' : 'light'
  themeContext.setMode(next)
}

// ── Fullscreen ──────────────────────────────────────────────────────────────
const fullscreenAdapter = useFullscreen()
const { isFullscreen, toggle: toggleFullscreen } = fullscreenAdapter

/** Mirrors the header's probe: the Fullscreen API is unreliable on touch
 *  devices and absent under happy-dom, so hide the button rather than ship a
 *  dead control. */
const fullscreenVisible = computed<boolean>(() => {
  if (!themeStore.fullscreenVisible) return false
  if (bp.isTouch.value) return false
  const supported = (fullscreenAdapter as { isSupported?: { value?: boolean } }).isSupported
  return !(supported && supported.value === false)
})

// ── Language ────────────────────────────────────────────────────────────────
// Same registry the header switcher reads. Two hand-written copies of this
// list is exactly how the two drift apart.
const langOptions = computed<DropdownOption[]>(() =>
  locales.options.value.map((l) => ({
    key: l.code,
    label: l.label,
    icon: () => (l.active ? h(Icon, { icon: 'mdi:check', width: 14, height: 14 }) : null),
  })),
)

function onLangSelect(key: string | number): void {
  locales.setLocale(String(key))
}

/** "Show desktop": the strip at the very end of the taskbar. Minimises
 *  everything; clicking again is NOT a restore - the desktop store has no
 *  "was minimised by me" bookkeeping, and inventing one would fight the user's
 *  own minimises. */
function showDesktop(): void {
  for (const win of desktop.windows) desktop.minimize(win.id)
}
</script>

<template>
  <div class="t-desktop-tray">
    <div class="t-desktop-tray__actions">
      <NTooltip v-if="themeStore.reloadVisible" placement="top" trigger="hover">
        <template #trigger>
          <button
            type="button"
            class="t-desktop-tray__btn"
            :aria-label="t('admin.desktop.tray.reload', 'Reload')"
            @click="appStore.reloadPage()"
          >
            <Icon icon="mdi:refresh" width="16" height="16" />
          </button>
        </template>
        {{ t('admin.desktop.tray.reload', 'Reload') }}
      </NTooltip>

      <NTooltip v-if="fullscreenVisible" placement="top" trigger="hover">
        <template #trigger>
          <button
            type="button"
            class="t-desktop-tray__btn"
            :aria-label="
              isFullscreen
                ? t('admin.desktop.tray.exitFullscreen', 'Exit fullscreen')
                : t('admin.desktop.tray.fullscreen', 'Fullscreen')
            "
            @click="toggleFullscreen()"
          >
            <Icon
              :icon="isFullscreen ? 'mdi:fullscreen-exit' : 'mdi:fullscreen'"
              width="16"
              height="16"
            />
          </button>
        </template>
        {{
          isFullscreen
            ? t('admin.desktop.tray.exitFullscreen', 'Exit fullscreen')
            : t('admin.desktop.tray.fullscreen', 'Fullscreen')
        }}
      </NTooltip>

      <NTooltip v-if="themeStore.themeSchemaVisible" placement="top" trigger="hover">
        <template #trigger>
          <button
            type="button"
            class="t-desktop-tray__btn"
            :aria-label="themeSchemaLabel"
            @click="cycleThemeSchema"
          >
            <Icon :icon="themeSchemaIcon" width="16" height="16" />
          </button>
        </template>
        {{ themeSchemaLabel }}
      </NTooltip>

      <NTooltip v-if="showThemeBtn" placement="top" trigger="hover">
        <template #trigger>
          <button
            type="button"
            class="t-desktop-tray__btn"
            :aria-label="t('admin.desktop.tray.themeSettings', 'Theme settings')"
            @click="shell.openThemeDrawer()"
          >
            <Icon icon="mdi:palette-outline" width="16" height="16" />
          </button>
        </template>
        {{ t('admin.desktop.tray.themeSettings', 'Theme settings') }}
      </NTooltip>

      <NDropdown
        v-if="themeStore.multilingualVisible && locales.hasChoice.value"
        :options="langOptions"
        trigger="click"
        placement="top-end"
        @select="onLangSelect"
      >
        <button
          type="button"
          class="t-desktop-tray__btn"
          :aria-label="t('admin.desktop.tray.language', 'Language')"
        >
          <Icon icon="mdi:web" width="16" height="16" />
        </button>
      </NDropdown>
    </div>

    <!-- Chat / notification bell / user avatar, supplied by whoever mounts the
         shell. Empty for a consumer that wires none of them. -->
    <div class="t-desktop-tray__slot">
      <slot />
    </div>

    <NPopover
      :show="calendarOpen"
      trigger="click"
      placement="top-end"
      :show-arrow="false"
      raw
      @update:show="onCalendarOpen"
    >
      <template #trigger>
        <button
          type="button"
          class="t-desktop-tray__clock"
          :title="clockFullDate"
          :aria-label="clockFullDate"
          :aria-expanded="calendarOpen"
        >
          <span class="t-desktop-tray__time">{{ clock }}</span>
        </button>
      </template>
      <div class="t-desktop-tray__calendar">
        <div class="t-desktop-tray__calendar-today">{{ clockFullDate }}</div>
        <NDatePicker v-model:value="calendarValue" panel type="date" />
      </div>
    </NPopover>

    <!-- The sliver at the very edge. Same command as the clock block, kept as
         its own target because that is where the muscle memory goes. -->
    <button
      type="button"
      class="t-desktop-tray__peek"
      :aria-label="t('admin.desktop.tray.showDesktop', 'Show desktop')"
      :title="t('admin.desktop.tray.showDesktop', 'Show desktop')"
      @click="showDesktop"
    />
  </div>
</template>

<style scoped>
.t-desktop-tray {
  display: flex;
  flex: 0 0 auto;
  gap: 2px;
  align-items: center;
  justify-content: flex-end;
  height: 100%;
  color: var(--tnzi-desktop-taskbar-fg);
}

.t-desktop-tray__actions,
.t-desktop-tray__slot {
  display: flex;
  gap: 2px;
  align-items: center;
}

.t-desktop-tray__slot {
  gap: 4px;
  padding: 0 2px;
}

.t-desktop-tray__btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  color: inherit;
  cursor: default;
  background: transparent;
  border: none;
  border-radius: 5px;
  opacity: 0.82;
  transition: background-color 0.12s ease, opacity 0.12s ease;
}

.t-desktop-tray__btn:hover,
.t-desktop-tray__btn:focus-visible {
  background: var(--tnzi-desktop-taskbar-hover);
  opacity: 1;
  outline: none;
}

.t-desktop-tray__clock {
  display: flex;
  align-items: center;
  height: 30px;
  padding: 0 9px;
  font-size: 12px;
  color: inherit;
  cursor: default;
  background: transparent;
  border: none;
  border-radius: 5px;
  transition: background-color 0.12s ease;
}

.t-desktop-tray__clock:hover,
.t-desktop-tray__clock:focus-visible {
  background: var(--tnzi-desktop-taskbar-hover);
  outline: none;
}

.t-desktop-tray__time {
  font-variant-numeric: tabular-nums;
}

.t-desktop-tray__calendar {
  padding: 10px 10px 4px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 18px 46px rgb(0 0 0 / 32%);
}

.t-desktop-tray__calendar-today {
  padding: 0 4px 8px;
  font-size: 12.5px;
  font-weight: 600;
}

/* The panel ships as a dropdown body: it brings its own shadow and rounding,
   which would sit inside ours as a second card. */
.t-desktop-tray__calendar :deep(.n-date-panel) {
  background: transparent;
  border: none;
  box-shadow: none;
}

.t-desktop-tray__peek {
  width: 4px;
  height: 22px;
  margin-left: 4px;
  cursor: default;
  background: var(--tnzi-desktop-taskbar-hover);
  border: none;
  border-radius: 2px;
  transition: background-color 0.12s ease;
}

.t-desktop-tray__peek:hover,
.t-desktop-tray__peek:focus-visible {
  background: var(--tnzi-primary);
  outline: none;
}
</style>
