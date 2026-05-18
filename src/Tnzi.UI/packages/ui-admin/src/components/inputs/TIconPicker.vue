<script setup lang="ts">
/**
 * `TIconPicker` — searchable Iconify icon picker.
 *
 * Renders an `NInput` with a popover panel showing a grid of icons. By
 * default ships with a curated MDI subset (~80 commonly-used admin icons).
 * Consumers can override via `icons` prop or extend via `extraIcons`.
 *
 * v-model:value is the iconify identifier string (e.g. `"mdi:account"`).
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { NInput, NPopover, NEmpty } from 'naive-ui'

interface Props {
  value: string
  /** Override the default icon list. */
  icons?: string[]
  /** Append to the default icon list. */
  extraIcons?: string[]
  placeholder?: string
}

const props = withDefaults(defineProps<Props>(), {
  value: '',
  icons: undefined,
  extraIcons: () => [],
  placeholder: 'Pick an icon…',
})

const emit = defineEmits<{
  'update:value': [value: string]
}>()

const DEFAULT_ICONS: string[] = [
  // Navigation / layout
  'mdi:home',
  'mdi:menu',
  'mdi:dashboard',
  'mdi:view-list',
  'mdi:view-grid',
  'mdi:apps',
  'mdi:format-list-bulleted',
  'mdi:tune',
  'mdi:cog',
  'mdi:cog-outline',
  // People
  'mdi:account',
  'mdi:account-outline',
  'mdi:account-multiple',
  'mdi:account-group',
  'mdi:account-cog',
  'mdi:shield-account',
  // Data
  'mdi:database',
  'mdi:table',
  'mdi:file',
  'mdi:file-multiple',
  'mdi:folder',
  'mdi:folder-open',
  'mdi:cloud',
  'mdi:cloud-upload',
  'mdi:cloud-download',
  // Communication
  'mdi:email',
  'mdi:email-outline',
  'mdi:bell',
  'mdi:bell-outline',
  'mdi:message',
  'mdi:chat',
  'mdi:forum',
  // Actions
  'mdi:plus',
  'mdi:minus',
  'mdi:pencil',
  'mdi:delete',
  'mdi:content-save',
  'mdi:refresh',
  'mdi:download',
  'mdi:upload',
  'mdi:export',
  'mdi:import',
  'mdi:magnify',
  'mdi:filter',
  'mdi:sort',
  // Status
  'mdi:check',
  'mdi:check-circle',
  'mdi:close',
  'mdi:close-circle',
  'mdi:alert',
  'mdi:alert-circle',
  'mdi:information',
  'mdi:help-circle',
  // Time / calendar
  'mdi:calendar',
  'mdi:clock',
  'mdi:clock-outline',
  'mdi:history',
  // Commerce
  'mdi:cart',
  'mdi:credit-card',
  'mdi:currency-usd',
  'mdi:tag',
  'mdi:gift',
  // Tools
  'mdi:tools',
  'mdi:wrench',
  'mdi:chart-bar',
  'mdi:chart-line',
  'mdi:chart-pie',
  // Misc
  'mdi:star',
  'mdi:heart',
  'mdi:bookmark',
  'mdi:lock',
  'mdi:lock-open',
  'mdi:key',
  'mdi:eye',
  'mdi:eye-off',
  'mdi:flag',
  'mdi:link',
]

const showPopover = ref(false)
const search = ref('')

const allIcons = computed(() => [
  ...(props.icons ?? DEFAULT_ICONS),
  ...props.extraIcons,
])

const filteredIcons = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return allIcons.value
  return allIcons.value.filter((icon) => icon.toLowerCase().includes(q))
})

function pick(icon: string): void {
  emit('update:value', icon)
  showPopover.value = false
  search.value = ''
}

function clear(): void {
  emit('update:value', '')
}
</script>

<template>
  <NPopover
    :show="showPopover"
    trigger="manual"
    placement="bottom-start"
    @clickoutside="showPopover = false"
  >
    <template #trigger>
      <NInput
        :value="value"
        :placeholder="placeholder"
        readonly
        clearable
        @click="showPopover = !showPopover"
        @clear="clear"
      >
        <template #prefix>
          <Icon v-if="value" :icon="value" width="18" height="18" />
          <Icon v-else icon="mdi:emoticon-outline" width="18" height="18" />
        </template>
      </NInput>
    </template>
    <div class="t-icon-picker__panel">
      <NInput
        v-model:value="search"
        size="small"
        placeholder="Search icons…"
        class="t-icon-picker__search"
      >
        <template #prefix>
          <Icon icon="mdi:magnify" width="16" height="16" />
        </template>
      </NInput>
      <div class="t-icon-picker__grid">
        <button
          v-for="icon in filteredIcons"
          :key="icon"
          type="button"
          class="t-icon-picker__cell"
          :class="{ 't-icon-picker__cell--active': value === icon }"
          :title="icon"
          @click="pick(icon)"
        >
          <Icon :icon="icon" width="20" height="20" />
        </button>
        <NEmpty
          v-if="filteredIcons.length === 0"
          description="No matching icons"
        />
      </div>
    </div>
  </NPopover>
</template>

<style scoped>
.t-icon-picker__panel {
  width: 320px;
}
.t-icon-picker__search {
  margin-bottom: 8px;
}
.t-icon-picker__grid {
  display: grid;
  grid-template-columns: repeat(8, 1fr);
  gap: 4px;
  max-height: 280px;
  overflow-y: auto;
}
.t-icon-picker__cell {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border: 1px solid transparent;
  background: transparent;
  border-radius: 4px;
  cursor: pointer;
  color: var(--tnzi-base-text-muted);
  transition: background 0.1s, color 0.1s, border-color 0.1s;
}
.t-icon-picker__cell:hover {
  background: var(--tnzi-primary-100);
  color: var(--tnzi-primary);
}
.t-icon-picker__cell--active {
  border-color: var(--tnzi-primary);
  color: var(--tnzi-primary);
}
</style>
