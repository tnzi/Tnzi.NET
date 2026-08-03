<script setup lang="ts">
/**
 * @experimental
 * TSidebarNav - grouped navigation list for a sidebar.
 *
 * Renders N groups, each optionally titled, collapsible, and carrying its own
 * header actions. A group with no `label` renders as a bare run of items,
 * which is how a product's primary nav sits above its titled sections without
 * a heading of its own.
 *
 * Collapse state is owned here and keyed by group id, so a consumer wiring a
 * nav does not have to hold a map of booleans it never reads.
 *
 * @example
 * ```ts
 * const groups = [
 *   { id: 'main', items: [{ id: 'chat', label: 'Chat', icon: 'lucide:message-square', active: true }] },
 *   { id: 'projects', label: 'Projects', collapsible: true,
 *     actions: [{ id: 'new', icon: 'lucide:plus', label: 'New project' }],
 *     items: projects.map(p => ({ id: p.id, label: p.name, icon: 'lucide:folder' })) },
 * ]
 * ```
 */
import { ref } from 'vue'
import { Icon } from '@iconify/vue'
import { useAiI18n, formatAiMessage } from '../../i18n/index'

export interface NavItem {
  readonly id: string
  readonly label: string
  readonly icon?: string
  readonly active?: boolean
  /** Trailing count or tag. */
  readonly badge?: string | number
}

/** Icon button rendered in a group's header row. */
export interface NavGroupAction {
  readonly id: string
  readonly icon: string
  readonly label: string
}

export interface NavGroup {
  readonly id: string
  /** Heading text. Omit for an untitled run of items. */
  readonly label?: string
  readonly items: readonly NavItem[]
  /** Clicking the heading toggles the item list. Requires `label`. */
  readonly collapsible?: boolean
  /** Start collapsed. Only meaningful with `collapsible`. */
  readonly defaultCollapsed?: boolean
  readonly actions?: readonly NavGroupAction[]
}

withDefaults(
  defineProps<{
    groups?: ReadonlyArray<NavGroup>
  }>(),
  {
    groups: () => [],
  },
)

const emit = defineEmits<{
  select: [item: NavItem, group: NavGroup]
  'group-action': [groupId: string, actionId: string]
  'group-toggle': [groupId: string, collapsed: boolean]
}>()

const t = useAiI18n()

/* Seeded lazily rather than from a watcher on `groups`: a group added later
   should start from its own `defaultCollapsed`, and re-seeding the whole map
   on every list change would discard what the user had toggled. */
const collapsedOverrides = ref<Record<string, boolean>>({})

function isCollapsed(group: NavGroup): boolean {
  const override = collapsedOverrides.value[group.id]
  return override ?? group.defaultCollapsed ?? false
}

function toggleGroup(group: NavGroup): void {
  if (!group.collapsible) return
  const next = !isCollapsed(group)
  collapsedOverrides.value = { ...collapsedOverrides.value, [group.id]: next }
  emit('group-toggle', group.id, next)
}

function toggleLabel(group: NavGroup): string {
  const template = isCollapsed(group)
    ? t.value.sidebar.expandGroup
    : t.value.sidebar.collapseGroup
  return formatAiMessage(template, { group: group.label ?? '' })
}

/* Header action buttons live inside the (button) heading when the group is
   collapsible, so their clicks must not also toggle it. */
function onGroupAction(group: NavGroup, actionId: string, event: MouseEvent): void {
  event.stopPropagation()
  emit('group-action', group.id, actionId)
}
</script>

<template>
  <nav class="t-sidebar-nav">
    <div v-for="group in groups" :key="group.id" class="t-sidebar-nav__group">
      <div v-if="group.label" class="t-sidebar-nav__head">
        <component
          :is="group.collapsible ? 'button' : 'span'"
          :type="group.collapsible ? 'button' : undefined"
          class="t-sidebar-nav__head-label"
          :class="{ 'is-collapsible': group.collapsible }"
          :aria-expanded="group.collapsible ? !isCollapsed(group) : undefined"
          :aria-label="group.collapsible ? toggleLabel(group) : undefined"
          @click="toggleGroup(group)"
        >
          <span>{{ group.label }}</span>
          <Icon
            v-if="group.collapsible"
            class="t-sidebar-nav__chevron"
            :class="{ 'is-collapsed': isCollapsed(group) }"
            icon="lucide:chevron-down"
          />
        </component>

        <div v-if="group.actions?.length" class="t-sidebar-nav__head-actions">
          <button
            v-for="action in group.actions"
            :key="action.id"
            type="button"
            class="t-sidebar-nav__head-action"
            :aria-label="action.label"
            @click="onGroupAction(group, action.id, $event)"
          >
            <Icon :icon="action.icon" />
          </button>
        </div>
      </div>

      <div v-show="!isCollapsed(group)" class="t-sidebar-nav__items">
        <slot name="items" :group="group">
          <button
            v-for="item in group.items"
            :key="item.id"
            type="button"
            class="t-sidebar-nav__item"
            :class="{ 'is-active': item.active }"
            @click="emit('select', item, group)"
          >
            <Icon v-if="item.icon" :icon="item.icon" class="t-sidebar-nav__item-icon" />
            <span class="t-sidebar-nav__item-label">{{ item.label }}</span>
            <span v-if="item.badge != null" class="t-sidebar-nav__item-badge">
              {{ item.badge }}
            </span>
          </button>
        </slot>
        <slot name="group-after" :group="group" />
      </div>
    </div>
  </nav>
</template>

<style scoped>
.t-sidebar-nav {
  display: flex;
  flex-direction: column;
}
.t-sidebar-nav__group {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 0 8px;
}
.t-sidebar-nav__group + .t-sidebar-nav__group {
  margin-top: 14px;
}
.t-sidebar-nav__head {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 0 2px 4px;
  min-height: 24px;
}
.t-sidebar-nav__head-label {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 0 8px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-tertiary);
  font-family: inherit;
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.01em;
  text-align: left;
}
.t-sidebar-nav__head-label.is-collapsible {
  cursor: pointer;
  border-radius: 6px;
}
.t-sidebar-nav__head-label.is-collapsible:hover {
  color: var(--tnzi-ai-text-secondary);
}
.t-sidebar-nav__chevron {
  font-size: 13px;
  transition: transform var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-sidebar-nav__chevron.is-collapsed {
  transform: rotate(-90deg);
}
.t-sidebar-nav__head-actions {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
}
.t-sidebar-nav__head-action {
  width: 20px;
  height: 20px;
  border: none;
  background: transparent;
  border-radius: 5px;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.t-sidebar-nav__head-action:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-sidebar-nav__items {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.t-sidebar-nav__item {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  height: 36px;
  padding: 0 9px;
  border: none;
  background: transparent;
  border-radius: 10px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 14px;
  cursor: pointer;
  text-align: left;
  transition: background var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-sidebar-nav__item:hover {
  background: var(--tnzi-ai-hover);
}
.t-sidebar-nav__item.is-active {
  background: var(--tnzi-ai-accent-soft);
  color: var(--tnzi-ai-accent);
  font-weight: 500;
}
.t-sidebar-nav__item-icon {
  width: 18px;
  height: 18px;
  font-size: 18px;
  flex-shrink: 0;
  color: var(--tnzi-ai-text-secondary);
}
.t-sidebar-nav__item.is-active .t-sidebar-nav__item-icon {
  color: var(--tnzi-ai-accent);
}
.t-sidebar-nav__item-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-sidebar-nav__item-badge {
  flex-shrink: 0;
  font-size: 11px;
  color: var(--tnzi-ai-text-tertiary);
}
</style>
