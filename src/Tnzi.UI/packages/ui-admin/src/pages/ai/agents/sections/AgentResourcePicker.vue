<template>
  <!--
    AgentResourcePicker - the shared "assigned cards + Add browse modal" surface
    for per-agent resource assignment (Skills / Tools / Knowledge). Presentational
    only: the parent owns the data + persistence and reacts to `assign`/`remove`.
    Renders inside the shared TDetailSection chrome so it matches every other
    detail section pixel-for-pixel (the previous hand-rolled header reused
    AgentDetail's scoped classes, which don't cross the component boundary →
    unstyled header). Immediate model: assign adds an active grant, remove
    deletes it, the per-card switch disables / re-enables it in place. Every
    write affordance (Add, Remove, the switch) hides without `canEdit`.
  -->
  <TDetailSection :title="title" :icon="headerIcon" :hint="hint" max-width="none">
    <template v-if="canEdit" #actions>
      <NButton size="small" type="primary" :disabled="available.length === 0" @click="showAdd = true">
        <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
        {{ addLabel }}
      </NButton>
    </template>

    <p v-if="notice" class="t-resource-picker__notice" role="status">{{ notice }}</p>

    <slot name="prepend" />

    <NSpin :show="loading">
      <div v-if="assigned.length === 0" class="t-resource-picker__empty">{{ emptyText }}</div>
      <div v-else class="t-resource-picker__grid">
        <TEntityCard v-for="item in assigned" :key="item.value" :class="{ 't-resource-picker__card--off': item.enabled === false }">
          <div class="flex items-center gap-8px mb-4px">
            <TSvgIcon v-if="icon" :icon="icon" :size="16" color="var(--tnzi-primary, #6d5ce7)" />
            <span class="t-resource-picker__name flex-1">{{ item.title }}</span>
            <NTag v-if="item.enabled === false" size="small" type="warning" :bordered="false">{{ disabledTag }}</NTag>
          </div>
          <div v-if="item.subtitle" class="t-resource-picker__subtitle font-mono">{{ item.subtitle }}</div>
          <div v-if="item.description" class="t-resource-picker__desc">{{ item.description }}</div>
          <div v-if="item.meta" class="t-resource-picker__meta">{{ item.meta }}</div>
          <div v-if="item.tags && item.tags.length" class="flex flex-wrap gap-4px mt-6px">
            <NTag v-for="tag in item.tags" :key="tag" size="tiny" type="info" :bordered="false">{{ tag }}</NTag>
          </div>
          <div v-if="$slots.itemExtra" class="t-resource-picker__extra">
            <slot name="itemExtra" :item="item" />
          </div>
          <template v-if="canEdit" #actions>
            <!-- enabled === undefined: grant state unknown (the grant list did
                 not load), so there is nothing truthful to show on a switch. -->
            <label v-if="item.enabled !== undefined" class="t-resource-picker__toggle">
              <NSwitch
                size="small"
                :value="item.enabled"
                :loading="busyValue === item.value"
                :aria-label="`${enabledLabel}: ${item.title}`"
                @update:value="emit('toggle', item.value, $event)"
              />
              <span>{{ enabledLabel }}</span>
            </label>
            <NPopconfirm @positive-click="emit('remove', item.value)">
              <template #trigger>
                <NButton size="small" type="error" ghost>{{ removeLabel }}</NButton>
              </template>
              {{ removeConfirm }}
            </NPopconfirm>
          </template>
        </TEntityCard>
      </div>
    </NSpin>

    <TOverlayTheme>
    <NModal v-model:show="showAdd" preset="card" size="small" :title="addLabel" class="w-680px max-w-92vw">
      <NInput v-model:value="search" clearable :placeholder="searchPlaceholder" class="mb-12px" />
      <div class="t-resource-picker__available">
        <div v-if="filteredAvailable.length === 0" class="t-resource-picker__empty">{{ allAssignedText }}</div>
        <div v-for="item in filteredAvailable" :key="item.value" class="t-resource-picker__avail-row">
          <div class="min-w-0 flex-1">
            <div class="t-resource-picker__avail-name">{{ item.title }}</div>
            <div v-if="item.subtitle" class="t-resource-picker__avail-sub font-mono">{{ item.subtitle }}</div>
            <div v-if="item.description" class="t-resource-picker__avail-desc">{{ item.description }}</div>
          </div>
          <NButton size="small" @click="emit('assign', item.value)">{{ assignLabel }}</NButton>
        </div>
      </div>
    </NModal>
    </TOverlayTheme>
  </TDetailSection>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { NButton, NInput, NModal, NPopconfirm, NSpin, NSwitch, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TDetailSection from '../../../../components/detail/TDetailSection.vue'
import TEntityCard from '../../../../components/data/TEntityCard.vue'
import { TOverlayTheme } from '../../../../components/overlay'

/** A renderable resource item (skill / tool group / knowledge base). */
export interface ResourcePickerItem {
  value: string
  title: string
  subtitle?: string
  description?: string
  meta?: string
  tags?: string[]
  /**
   * Grant state of an assigned item: `true` active, `false` disabled (kept on
   * the agent, ignored at runtime). Leave undefined when the state is unknown -
   * no switch is rendered then.
   */
  enabled?: boolean
}

interface Props {
  title: string
  hint: string
  addLabel: string
  removeLabel: string
  removeConfirm: string
  assignLabel: string
  emptyText: string
  allAssignedText: string
  searchPlaceholder: string
  /** Per-item icon shown on each resource row. */
  icon?: string
  /** Section-header icon (mirrors the active nav item's icon). */
  headerIcon?: string
  loading?: boolean
  assigned: ResourcePickerItem[]
  available: ResourcePickerItem[]
  /** Show the write affordances (Add, Remove, enable switch). */
  canEdit?: boolean
  /** Label next to each card's enable switch. */
  enabledLabel?: string
  /** Tag on a card whose grant is disabled. */
  disabledTag?: string
  /** Item whose enable toggle is in flight (spinner on its switch). */
  busyValue?: string | null
  /** One-line notice above the cards (e.g. why grant states are unavailable). */
  notice?: string | null
}

const props = withDefaults(defineProps<Props>(), {
  loading: false,
  icon: undefined,
  headerIcon: undefined,
  canEdit: true,
  enabledLabel: 'Active',
  disabledTag: 'Disabled',
  busyValue: null,
  notice: null,
})

const emit = defineEmits<{ assign: [value: string]; remove: [value: string]; toggle: [value: string, enabled: boolean] }>()

defineSlots<{ prepend?: () => unknown; itemExtra?: (props: { item: ResourcePickerItem }) => unknown }>()

const showAdd = ref(false)
const search = ref('')

const filteredAvailable = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return props.available
  return props.available.filter(
    (i) =>
      i.title.toLowerCase().includes(q) ||
      (i.subtitle?.toLowerCase().includes(q) ?? false) ||
      (i.description?.toLowerCase().includes(q) ?? false),
  )
})
</script>

<style scoped>
.t-resource-picker__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 12px;
}
.t-resource-picker__name {
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-base-text);
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-resource-picker__subtitle {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-resource-picker__desc {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.t-resource-picker__meta {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-top: 6px;
}
.t-resource-picker__card--off .t-resource-picker__name,
.t-resource-picker__card--off .t-resource-picker__desc {
  opacity: 0.6;
}
.t-resource-picker__notice {
  margin: 0 0 12px;
  font-size: 12px;
  color: var(--tnzi-warning, #b7791f);
}
.t-resource-picker__extra {
  margin-top: 6px;
}
.t-resource-picker__toggle {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-right: auto;
}
.t-resource-picker__empty {
  padding: 32px 0;
  text-align: center;
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #9ca3af);
}
.t-resource-picker__available {
  max-height: 460px;
  overflow-y: auto;
}
.t-resource-picker__avail-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 4px;
  border-bottom: 1px solid var(--tnzi-border, #eee);
}
.t-resource-picker__avail-row:last-child {
  border-bottom: none;
}
.t-resource-picker__avail-name {
  font-size: 13px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-resource-picker__avail-sub {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-resource-picker__avail-desc {
  font-size: 11.5px;
  color: var(--tnzi-base-text-muted, #888);
  margin-top: 2px;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
