<script setup lang="ts">
/**
 * `TDesktopStartMenu` - the start button and the panel it opens.
 *
 * The panel carries the FULL menu tree (grouped), where the icon grid carries
 * only leaves. That split is the point: the desktop is for reaching things
 * quickly, the start menu is the complete, structured index.
 *
 * It ships a filter because the preset menu runs to three digits of pages -
 * a start menu you have to scroll through to find anything is a worse sidebar.
 *
 * Rendered as a bottom-left overlay rather than a naive popover: it rises from
 * the same corner as the start button that opened it, and the backdrop gives it
 * a click-anywhere dismiss without a focus trap around a panel that is really
 * just a list of links.
 */
import { computed, nextTick, ref, watch } from 'vue'
import { NInput, NScrollbar } from 'naive-ui'
import { TAvatar, TSvgIcon } from '@tnzi/ui'
import { useAdminRouteStore, type AdminMenuItem } from '../../stores/useAdminRouteStore'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { useSettingsEntry } from '../../headless/useSettingsEntry'
import { useChromeActions } from '../../headless/useChromeActions'
import { resolveWindowIcon } from './desktop-labels'
import { desktopTileStyle, useDesktopTint } from './desktop-tints'

const props = defineProps<{
  show: boolean
  /** Product name, shown next to the user in the panel footer. */
  brand?: string
  brandIcon?: string
  translate?: (key: string, fallback?: string) => string
}>()

const emit = defineEmits<{
  'update:show': [value: boolean]
  open: [item: AdminMenuItem]
}>()

const routeStore = useAdminRouteStore()
const authStore = useAdminAuthStore()
const tintFor = useDesktopTint()
/**
 * Settings and the built-in-menus toggle live in the sidebar footer in every
 * other layout - and the desktop has no sidebar, so both were unreachable here.
 * Same gating, same navigation, opened as a window.
 */
const settings = useSettingsEntry()
/**
 * The host's own chrome actions live in the sidebar footer in every other
 * layout - and this one has no sidebar, so they would silently vanish for any
 * app that turns the desktop on. Same list, same gating, same strip as the two
 * built-ins beside them.
 */
const hostActions = useChromeActions()

const t = (key: string, fallback: string): string => props.translate?.(key, fallback) ?? fallback

const keyword = ref('')
const searchRef = ref<InstanceType<typeof NInput> | null>(null)

/** Already resolved by the route store - see the note in TDesktopIcons. */
function labelOf(item: AdminMenuItem): string {
  return item.label
}

function iconOf(item: AdminMenuItem): string {
  return resolveWindowIcon(item.icon, item.key)
}

function tileStyleOf(item: AdminMenuItem): Record<string, string> {
  const tint = tintFor(item.key, (item.meta as { color?: string } | undefined)?.color)
  return desktopTileStyle(tint, 34, 8)
}

interface StartGroup {
  key: string
  label: string
  items: AdminMenuItem[]
}

/** Group by top-level menu entry; a top-level leaf becomes its own group of one
 *  so nothing silently disappears from the index. */
const groups = computed<StartGroup[]>(() =>
  routeStore.menus.map((top) => {
    const items: AdminMenuItem[] = []
    const walk = (nodes: AdminMenuItem[]): void => {
      for (const node of nodes) {
        if (node.children?.length) walk(node.children)
        else if (node.path) items.push(node)
      }
    }
    if (top.children?.length) walk(top.children)
    else if (top.path) items.push(top)
    return { key: top.key, label: labelOf(top), items }
  }),
)

const filtered = computed<StartGroup[]>(() => {
  const q = keyword.value.trim().toLowerCase()
  if (!q) return groups.value.filter((g) => g.items.length > 0)
  return groups.value
    .map((g) => ({ ...g, items: g.items.filter((i) => labelOf(i).toLowerCase().includes(q)) }))
    .filter((g) => g.items.length > 0)
})

const totalPages = computed(() =>
  groups.value.reduce((sum, g) => sum + g.items.length, 0),
)

const userName = computed(
  () =>
    authStore.userInfo?.displayName ||
    authStore.userInfo?.shortName ||
    authStore.userInfo?.username ||
    '',
)

function setShow(value: boolean): void {
  emit('update:show', value)
}

// Reset and focus the filter each time the panel opens - a start menu that
// reopens holding the last query is a small trap.
watch(
  () => props.show,
  (open) => {
    if (!open) return
    keyword.value = ''
    void nextTick(() => searchRef.value?.focus())
  },
)

function onPick(item: AdminMenuItem): void {
  emit('open', item)
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key === 'Escape') setShow(false)
}

function onHostAction(run: () => void): void {
  // Same reason as onOpenSettings: the panel is a launcher, and whatever the
  // action opens would come up underneath it.
  setShow(false)
  run()
}

function onOpenSettings(): void {
  // Close first: the panel is a launcher, and leaving it over the window it
  // just opened means the user has to dismiss it before using the thing.
  setShow(false)
  settings.open()
}
</script>

<template>
  <button
    type="button"
    class="t-desktop-start__button"
    :class="{ 't-desktop-start__button--open': show }"
    :aria-label="t('admin.desktop.start.open', 'Start')"
    :title="t('admin.desktop.start.open', 'Start')"
    :aria-expanded="show"
    @click="setShow(!show)"
  >
    <!-- The product's own mark when it has one. The four-square glyph is the
         fallback: an app with no brand icon still needs something that reads as
         "everything lives here", and a wordmark is illegible at this size. -->
    <TSvgIcon v-if="brandIcon" :icon="brandIcon" :size="20" aria-hidden="true" />
    <span v-else class="t-desktop-start__glyph" aria-hidden="true">
      <i /><i /><i /><i />
    </span>
  </button>

  <Teleport to="body">
    <div
      v-if="show"
      class="t-desktop-start__overlay"
      @click.self="setShow(false)"
      @keydown="onKeydown"
    >
      <div class="t-desktop-start__panel" role="dialog" :aria-label="t('admin.desktop.start.open', 'Start')">
        <!-- The product's name belongs here, on the system surface, rather than
             in the taskbar corner where it competes with the dock. -->
        <header v-if="brand || brandIcon" class="t-desktop-start__brand">
          <TSvgIcon v-if="brandIcon" :icon="brandIcon" :size="22" aria-hidden="true" />
          <span v-if="brand" class="t-desktop-start__brand-name">{{ brand }}</span>
        </header>
        <div class="t-desktop-start__search">
          <NInput
            ref="searchRef"
            v-model:value="keyword"
            clearable
            :placeholder="t('admin.desktop.start.searchPlaceholder', 'Search')"
            @keydown="onKeydown"
          >
            <template #prefix>
              <TSvgIcon icon="mdi:magnify" :size="16" aria-hidden="true" />
            </template>
          </NInput>
        </div>

        <NScrollbar class="t-desktop-start__scroll">
          <div v-if="filtered.length === 0" class="t-desktop-start__empty">
            {{ t('admin.desktop.start.empty', 'Nothing matches') }}
          </div>
          <section v-for="group in filtered" :key="group.key" class="t-desktop-start__group">
            <h4 class="t-desktop-start__group-label">{{ group.label }}</h4>
            <div class="t-desktop-start__grid">
              <button
                v-for="item in group.items"
                :key="item.key"
                type="button"
                class="t-desktop-start__item"
                :title="labelOf(item)"
                @click="onPick(item)"
              >
                <span class="t-desktop-start__tile" :style="tileStyleOf(item)">
                  <TSvgIcon :icon="iconOf(item)" :size="18" aria-hidden="true" />
                </span>
                <span class="t-desktop-start__item-label">{{ labelOf(item) }}</span>
              </button>
            </div>
          </section>
        </NScrollbar>

        <!-- Identity only. The user MENU (profile, sign out) is one click away
             in the tray; a second copy here would be two places to keep in
             agreement about what signing out does. -->
        <footer class="t-desktop-start__footer">
          <TAvatar :name="userName" :size="28" />
          <span class="t-desktop-start__footer-name">{{ userName }}</span>
          <span class="t-desktop-start__footer-count">
            {{ t('admin.desktop.start.pageCount', '{n} pages').replace('{n}', String(totalPages)) }}
          </span>
          <button
            v-if="settings.canToggleBuiltIn.value"
            type="button"
            class="t-desktop-start__footer-btn"
            :class="{ 'is-active': settings.builtInEnabled.value }"
            :title="settings.builtInTip.value"
            :aria-pressed="settings.builtInEnabled.value"
            @click="settings.toggleBuiltIn()"
          >
            <TSvgIcon icon="mdi:cube-outline" :size="17" aria-hidden="true" />
          </button>
          <button
            v-if="settings.available.value"
            type="button"
            class="t-desktop-start__footer-btn"
            :title="settings.label.value"
            :aria-label="settings.label.value"
            @click="onOpenSettings"
          >
            <TSvgIcon icon="mdi:cog-outline" :size="17" aria-hidden="true" />
          </button>
          <button
            v-for="action in hostActions"
            :key="action.key"
            type="button"
            class="t-desktop-start__footer-btn"
            :class="{ 'is-active': action.active }"
            :title="action.label"
            :aria-label="action.label"
            @click="onHostAction(action.run)"
          >
            <TSvgIcon :icon="action.icon" :size="17" aria-hidden="true" />
          </button>
        </footer>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.t-desktop-start__button {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  color: inherit;
  cursor: default;
  background: transparent;
  border: none;
  border-radius: 5px;
  transition: background-color 0.12s ease;
}

.t-desktop-start__button:hover,
.t-desktop-start__button:focus-visible,
.t-desktop-start__button--open {
  background: var(--tnzi-desktop-taskbar-hover);
  outline: none;
}

.t-desktop-start__glyph {
  display: grid;
  grid-template-rows: 7px 7px;
  grid-template-columns: 7px 7px;
  gap: 2px;
}

.t-desktop-start__glyph i {
  background: var(--tnzi-primary, #646cff);
  border-radius: 1px;
}

.t-desktop-start__glyph i:nth-child(2),
.t-desktop-start__glyph i:nth-child(3) {
  opacity: 0.8;
}

.t-desktop-start__glyph i:nth-child(4) {
  opacity: 0.6;
}

.t-desktop-start__overlay {
  position: fixed;
  inset: 0;
  z-index: var(--tnzi-desktop-z-start);
  display: flex;
  align-items: flex-end;
  justify-content: flex-start;
  /* Clears the taskbar so the panel reads as rising out of the dock rather
     than sitting on top of it, and lines its left edge up with the brand. */
  padding-bottom: calc(var(--tnzi-desktop-taskbar-height, 48px) + 8px);
  padding-left: 6px;
  animation: t-desktop-fadein 0.12s ease;
}

.t-desktop-start__panel {
  display: flex;
  flex-direction: column;
  width: min(560px, calc(100vw - 24px));
  max-height: min(620px, 76vh);
  overflow: hidden;
  /* Deliberately the CONTAINER colour, not the taskbar's. This panel is full of
     naive-ui controls (the search box, the tiles) that colour themselves from
     the standard text/background tokens; putting them on a tinted system
     surface with its own flipped foreground is how you get dark-on-dark. It
     takes the same glass, just over its own colour. */
  background: color-mix(
    in srgb,
    var(--tnzi-container-bg, #fff) var(--tnzi-desktop-solidity),
    transparent
  );
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 26px 70px rgb(0 0 0 / 36%);
  backdrop-filter: var(--tnzi-desktop-backdrop);
  animation: t-desktop-padpop 0.16s ease;
}

@keyframes t-desktop-fadein {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes t-desktop-padpop {
  from {
    opacity: 0;
    transform: scale(1.04);
  }

  to {
    opacity: 1;
    transform: none;
  }
}

.t-desktop-start__brand {
  display: flex;
  flex: 0 0 auto;
  gap: 10px;
  align-items: center;
  padding: 15px 18px 3px;
  font-size: 15px;
  font-weight: 700;
  letter-spacing: -0.01em;
  color: var(--tnzi-primary);
}

.t-desktop-start__brand-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-desktop-start__search {
  flex: 0 0 auto;
  padding: 12px 18px 10px;
}

.t-desktop-start__scroll {
  flex: 1 1 auto;
  min-height: 0;
}

.t-desktop-start__empty {
  padding: 40px 12px;
  font-size: 13px;
  text-align: center;
  opacity: 0.6;
}

.t-desktop-start__group {
  padding: 4px 12px 10px;
}

.t-desktop-start__group-label {
  padding: 6px 6px 4px;
  margin: 0;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  opacity: 0.5;
}

.t-desktop-start__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(96px, 1fr));
  gap: 2px;
}

.t-desktop-start__item {
  display: flex;
  flex-direction: column;
  gap: 7px;
  align-items: center;
  padding: 11px 4px 9px;
  color: inherit;
  cursor: default;
  background: transparent;
  border: none;
  border-radius: 6px;
}

.t-desktop-start__item:hover,
.t-desktop-start__item:focus-visible {
  background: var(--tnzi-desktop-window-hover);
  outline: none;
}

.t-desktop-start__tile {
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
}

.t-desktop-start__item-label {
  display: -webkit-box;
  overflow: hidden;
  font-size: 11px;
  line-height: 1.3;
  text-align: center;
  overflow-wrap: anywhere;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.t-desktop-start__footer {
  display: flex;
  flex: 0 0 auto;
  gap: 10px;
  align-items: center;
  padding: 11px 18px;
  background: var(--tnzi-bg-deep);
  border-top: 1px solid var(--tnzi-border);
}

.t-desktop-start__footer-name {
  flex: 1 1 auto;
  overflow: hidden;
  font-size: 12.5px;
  font-weight: 500;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-desktop-start__footer-count {
  flex: 0 0 auto;
  font-size: 11px;
  font-variant-numeric: tabular-nums;
  opacity: 0.55;
}

.t-desktop-start__footer-btn {
  display: flex;
  flex: 0 0 auto;
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
  opacity: 0.65;
  transition: background-color 0.12s ease, opacity 0.12s ease, color 0.12s ease;
}

.t-desktop-start__footer-btn:hover,
.t-desktop-start__footer-btn:focus-visible {
  background: var(--tnzi-desktop-window-hover);
  opacity: 1;
  outline: none;
}

/* The cube's tint carries the toggle's on state - the whole button is the
   switch, matching the sidebar footer rather than inventing a second idiom. */
.t-desktop-start__footer-btn.is-active {
  color: var(--tnzi-primary);
  opacity: 1;
}
</style>
