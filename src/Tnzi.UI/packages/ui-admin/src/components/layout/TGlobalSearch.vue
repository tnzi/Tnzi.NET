<template>
  <NModal
    :show="show"
    :mask-closable="true"
    preset="card"
    class="t-global-search"
    :style="{ width: '600px' }"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <div class="t-global-search__body">
      <NInput
        ref="inputRef"
        :value="query"
        :placeholder="translate('admin.search.placeholder')"
        class="t-global-search__input"
        @update:value="onQueryChange"
        @keydown="onKeydown"
      />
      <ul class="t-global-search__list" role="listbox" aria-label="Search results">
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
        <li v-if="filtered.length === 0" class="t-global-search__empty">
          {{ translate('admin.search.empty') }}
        </li>
      </ul>
    </div>
  </NModal>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { NModal, NInput } from 'naive-ui'
import { useAdminRouteStore, type AdminMenuItem } from '../../stores/useAdminRouteStore'

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
const query = ref('')
const highlighted = ref(0)
const inputRef = ref<{ focus?: () => void } | null>(null)

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
      inputRef.value?.focus?.()
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
  border-top: 1px solid var(--tnzi-border-color, #e5e7eb);
}

.t-global-search__item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 12px;
  cursor: pointer;
  border-radius: 6px;
  color: var(--tnzi-text-color, #1f2937);
  transition: background-color 0.12s ease;
}

.t-global-search__item--active {
  background-color: var(--tnzi-primary-color-hover, rgba(59, 130, 246, 0.12));
}

.t-global-search__item-label {
  font-weight: 500;
}

.t-global-search__item-path {
  font-size: 12px;
  color: var(--tnzi-text-color-3, #9ca3af);
}

.t-global-search__empty {
  padding: 24px 12px;
  text-align: center;
  color: var(--tnzi-text-color-3, #9ca3af);
}
</style>
