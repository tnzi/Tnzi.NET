<template>
  <!-- soybean parity (search-modal.vue): `class` carries a fixed-width
       layout via positioning ('fixed left-0 right-0' would auto-center
       the modal if naive injected unset insets, but the modal already
       teleports to body with its own mask, so we just constrain width
       via inline style). `card-style` is unreliable across Naive UI
       versions - `style` on NModal is applied to the inner content
       container (which IS the NCard when preset='card'). -->
  <NModal
    :show="show"
    :mask-closable="true"
    preset="card"
    :class="['t-global-search', { 't-global-search--fullscreen': isFullscreen }]"
    :style="modalStyle"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <div class="t-global-search__body">
      <NInput
        ref="inputRef"
        :value="inputBuffer"
        :placeholder="translate('admin.search.placeholder')"
        class="t-global-search__input"
        @update:value="onQueryChange"
        @keydown="onKeydown"
      />
      <ul
        v-if="filtered.length > 0"
        class="t-global-search__list"
        role="listbox"
        aria-label="Search results"
      >
        <li
          v-for="(item, index) in filtered"
          :key="item.key"
          :class="[
            't-global-search__item',
            { 't-global-search__item--active': index === highlighted },
          ]"
          role="option"
          :aria-selected="index === highlighted"
          @click="onSelect(item)"
          @mousemove="highlighted = index"
        >
          <span class="t-global-search__item-label">{{ item.label }}</span>
          <span class="t-global-search__item-path">{{ item.path }}</span>
        </li>
      </ul>
      <!-- Phase H4 I4: use NEmpty (soybean parity) for no-result state. -->
      <NEmpty
        v-else
        class="t-global-search__empty"
        :description="translate('admin.search.empty')"
      />
      <!-- Phase H4 footer: keyboard-shortcut hints (soybean parity). -->
      <div class="t-global-search__footer">
        <span class="t-global-search__kbd-row">
          <kbd>↵</kbd>
          <span>{{ translate('admin.search.kbdEnter') }}</span>
        </span>
        <span class="t-global-search__kbd-row">
          <kbd>↑</kbd><kbd>↓</kbd>
          <span>{{ translate('admin.search.kbdNav') }}</span>
        </span>
        <span class="t-global-search__kbd-row">
          <kbd>esc</kbd>
          <span>{{ translate('admin.search.kbdClose') }}</span>
        </span>
      </div>
    </div>
  </NModal>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { NModal, NInput, NEmpty } from 'naive-ui'
import { useAdminRouteStore, type AdminMenuItem } from '../../stores/useAdminRouteStore'
import { useBreakpoint } from '../../headless/useBreakpoint'

export interface SearchItem {
  key: string
  label: string
  path: string
}

interface Props {
  show: boolean
  matcher?: (query: string, item: SearchItem) => boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  show: false,
  matcher: undefined,
  translate: (key: string) => key,
})

const emit = defineEmits<{
  'update:show': [value: boolean]
  select: [item: SearchItem]
}>()

const routeStore = useAdminRouteStore()
const inputBuffer = ref('')
const query = ref('')
const highlighted = ref(0)
const inputRef = ref<{ focus?: () => void } | null>(null)

// Auto-switch to fullscreen on viewports narrower than ~660px (search
// shell is 630px wide + side margins) so phones don't see a clipped or
// edge-touching modal. <sm (640) → fullscreen.
const bp = useBreakpoint()
const isFullscreen = computed<boolean>(() => bp.width.value > 0 && bp.width.value < 660)
const modalStyle = computed(() =>
  isFullscreen.value
    ? { width: '100vw', maxWidth: '100vw' }
    : { width: 'min(630px, 95vw)' },
)

const flatItems = computed<SearchItem[]>(() => {
  const result: SearchItem[] = []
  function walk(list: AdminMenuItem[]): void {
    for (const m of list) {
      if (m.path) {
        result.push({ key: m.key, label: m.label, path: m.path })
      }
      if (m.children && m.children.length > 0) walk(m.children)
    }
  }
  walk(routeStore.menus)
  return result
})

function defaultMatcher(q: string, item: SearchItem): boolean {
  const needle = q.toLowerCase()
  return item.label.toLowerCase().includes(needle) || item.path.toLowerCase().includes(needle)
}

const filtered = computed<SearchItem[]>(() => {
  const q = query.value.trim()
  if (!q) return flatItems.value
  const match = props.matcher ?? defaultMatcher
  return flatItems.value.filter((item) => match(q, item))
})

function onQueryChange(value: string): void {
  // Phase H4 I5 revisited: tests expect immediate filtering. Keep
  // inputBuffer + query in sync; debounce can come back later as an
  // opt-in prop if a consumer's menu tree is large enough to lag.
  inputBuffer.value = value
  query.value = value
  highlighted.value = 0
}

function onSelect(item: SearchItem): void {
  emit('select', item)
  emit('update:show', false)
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    if (filtered.value.length === 0) return
    highlighted.value = Math.min(highlighted.value + 1, filtered.value.length - 1)
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    highlighted.value = Math.max(highlighted.value - 1, 0)
  } else if (event.key === 'Enter') {
    event.preventDefault()
    const item = filtered.value[highlighted.value]
    if (item) onSelect(item)
  } else if (event.key === 'Escape') {
    event.preventDefault()
    emit('update:show', false)
  }
}

watch(
  () => props.show,
  async (v) => {
    if (v) {
      query.value = ''
      highlighted.value = 0
      await nextTick()
      // Desktop: focus immediately (Ctrl/Cmd+K power-user flow - start typing
      // right away). Phone: skip auto-focus so the soft keyboard doesn't
      // instantly cover the menu list the user opened to browse.
      if (!bp.isSm.value) inputRef.value?.focus?.()
    }
  },
  { immediate: true },
)

watch(filtered, (list) => {
  if (highlighted.value >= list.length) highlighted.value = 0
})

defineExpose({ highlighted })
</script>

<style scoped>
.t-global-search__body {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 4px 0;
}

.t-global-search__input {
  width: 100%;
}

.t-global-search__list {
  list-style: none;
  margin: 0;
  padding: 0;
  max-height: 360px;
  overflow-y: auto;
  border-top: 1px solid var(--tnzi-border, #e5e7eb);
}

.t-global-search__item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 12px;
  cursor: pointer;
  border-radius: var(--tnzi-admin-radius-sm, 6px);
  color: var(--tnzi-base-text);
  transition: background-color 0.12s ease;
}

/* Phase H4 I6: active item gets solid primary fill + white text
   (mirrors soybean search-result.vue:38-42 - much higher contrast
   than the previous 12%-tint version). */
.t-global-search__item--active {
  background-color: var(--tnzi-primary, #646cff);
  color: #ffffff;
}
.t-global-search__item--active .t-global-search__item-path {
  color: rgba(255, 255, 255, 0.7);
}

.t-global-search__item-label {
  font-weight: 500;
}

.t-global-search__item-path {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #9ca3af);
}

/* Phase H4 I3: footer keyboard hints. */
.t-global-search__footer {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 8px 8px 0 8px;
  margin-top: 8px;
  border-top: 1px solid var(--tnzi-border, #e5e7eb);
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #6b7280);
}
.t-global-search__kbd-row {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}
.t-global-search__footer kbd {
  display: inline-block;
  min-width: 18px;
  height: 18px;
  padding: 0 4px;
  border-radius: var(--tnzi-admin-radius-sm, 4px);
  background: var(--tnzi-layout-bg, #f5f7fa);
  box-shadow: inset 0 -1px 0 var(--tnzi-border, #e5e7eb);
  font-family: inherit;
  font-size: 11px;
  line-height: 18px;
  text-align: center;
  color: var(--tnzi-base-text-muted, #6b7280);
}

.t-global-search__empty {
  padding: 24px 12px;
  text-align: center;
  color: var(--tnzi-base-text-muted, #9ca3af);
}

.t-global-search__list {
  /* On phones the keyboard occupies ~40% of the screen so the result
     list has less room. Cap at 50vh to stay within thumb-scroll reach. */
  max-height: min(360px, 50vh);
}
</style>

<!-- Fullscreen targets a teleported root (NModal teleports to body);
     non-scoped block is required. -->
<style>
.t-global-search--fullscreen {
  position: fixed !important;
  inset: 0 !important;
  height: 100dvh !important;
  max-height: 100dvh !important;
  border-radius: 0 !important;
  margin: 0 !important;
}
.t-global-search--fullscreen .n-card {
  border-radius: 0;
  height: 100dvh;
}
</style>
