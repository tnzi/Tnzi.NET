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
 * A row may also carry one trailing action (`NavItem.action`), a quick action
 * such as "create" on a "Projects" row: it emits `item-action`, never `select`.
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
import { vTitleReveal } from '../../headless/title-reveal'
import { vIconMotion, type IconMotion } from '../../headless/icon-motion'
import { vTooltip } from '../../headless/tooltip'

export interface NavItem {
  readonly id: string
  readonly label: string
  readonly icon?: string
  /**
   * A one-shot motion of the icon when the pointer enters the row (and on
   * keyboard focus): `ring` / `hop` / `tilt` / `spin` / `pop`. Pick the one
   * that matches the icon's meaning; unset means a still icon.
   */
  readonly iconMotion?: IconMotion
  readonly active?: boolean
  /** Trailing count or tag. */
  readonly badge?: string | number
  /**
   * How much the badge asks for: unset is the quiet grey count (informational);
   * `'warning'` an amber pill (something waits, not urgent); `'danger'` a red one
   * (something needs the person now). The collapsed rail shows a toned badge on
   * the item's icon; an untoned one stays out of it, as before.
   */
  readonly badgeTone?: NavBadgeTone
  /**
   * A quick action at the row's right end (e.g. a "+" that creates a project
   * without opening the Projects page first). It shows while the row is
   * hovered or holds keyboard focus, and always on a touch screen; the badge
   * gives way while it shows. Clicking it emits `item-action`, never `select`.
   * The collapsed rail does not show it.
   */
  readonly action?: NavItemAction
}

/** How loudly a nav item's badge asks for attention (see `NavItem.badgeTone`). */
export type NavBadgeTone = 'danger' | 'warning'

/** The icon button at the end of a nav row. `label` is its accessible name and tooltip. */
export interface NavItemAction {
  readonly id: string
  readonly icon: string
  readonly label: string
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
  'item-action': [itemId: string, actionId: string]
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
      <!-- The sidebar's shared heading style (styles/index.css): the same
           heading as the conversation list's, told from the rows by colour. -->
      <div v-if="group.label" class="t-sidebar-nav__head t-sidebar-heading">
        <component
          :is="group.collapsible ? 'button' : 'span'"
          :type="group.collapsible ? 'button' : undefined"
          class="t-sidebar-nav__head-label t-sidebar-heading__label"
          :class="{ 'is-collapsible': group.collapsible }"
          :aria-expanded="group.collapsible ? !isCollapsed(group) : undefined"
          :aria-label="group.collapsible ? toggleLabel(group) : undefined"
          @click="toggleGroup(group)"
        >
          <span>{{ group.label }}</span>
          <Icon
            v-if="group.collapsible"
            class="t-sidebar-nav__chevron t-sidebar-heading__chevron"
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
          <template v-for="item in group.items" :key="item.id">
            <!-- A row with an action is a box holding two sibling buttons
                 (a button cannot contain another). The box is the hover host
                 for the icon motion and the title reveal, so moving onto the
                 action neither replays the one nor ends the other; the row
                 button marks itself for the keyboard-focus motion. -->
            <div
              v-if="item.action"
              v-icon-motion
              class="t-sidebar-nav__row"
              data-title-reveal-host
            >
              <button
                type="button"
                class="t-sidebar-nav__item"
                data-icon-motion-focus
                :class="{ 'is-active': item.active }"
                :aria-current="item.active ? 'page' : undefined"
                @click="emit('select', item, group)"
              >
                <Icon
                  v-if="item.icon"
                  :icon="item.icon"
                  class="t-sidebar-nav__item-icon"
                  :data-icon-motion="item.iconMotion"
                />
                <span v-title-reveal class="t-sidebar-nav__item-label">{{ item.label }}</span>
                <!-- Room for the action, which is laid over it, so the label
                     never runs under the button and does not reflow when it
                     shows. The badge sits here and gives way to it. -->
                <span class="t-sidebar-nav__item-end">
                  <span
                    v-if="item.badge != null"
                    class="t-sidebar-nav__item-badge"
                    :class="item.badgeTone ? `t-sidebar-nav__item-badge--${item.badgeTone}` : undefined"
                    :data-badge-tone="item.badgeTone"
                  >
                    {{ item.badge }}
                  </span>
                </span>
              </button>
              <button
                v-tooltip="{ text: item.action.label, side: 'right' }"
                type="button"
                class="t-sidebar-nav__item-action"
                :aria-label="item.action.label"
                @click="emit('item-action', item.id, item.action.id)"
              >
                <Icon :icon="item.action.icon" aria-hidden="true" />
              </button>
            </div>
            <button
              v-else
              v-icon-motion
              type="button"
              class="t-sidebar-nav__item"
              data-title-reveal-host
              :class="{ 'is-active': item.active }"
              :aria-current="item.active ? 'page' : undefined"
              @click="emit('select', item, group)"
            >
              <Icon
                v-if="item.icon"
                :icon="item.icon"
                class="t-sidebar-nav__item-icon"
                :data-icon-motion="item.iconMotion"
              />
              <span v-title-reveal class="t-sidebar-nav__item-label">{{ item.label }}</span>
              <span
                v-if="item.badge != null"
                class="t-sidebar-nav__item-badge"
                :class="item.badgeTone ? `t-sidebar-nav__item-badge--${item.badgeTone}` : undefined"
                :data-badge-tone="item.badgeTone"
              >
                {{ item.badge }}
              </span>
            </button>
          </template>
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
  /* The rows' own 1px gap, so a heading sits one row pitch above its first
     row (see `.t-sidebar-heading`). */
  gap: 1px;
  padding: 0 8px;
}
/* The sidebar's section gap above every group after the first, the same as
   above the conversation list's heading. */
.t-sidebar-nav__group + .t-sidebar-nav__group {
  margin-top: var(--tnzi-ai-sidebar-section-gap, 12px);
}
/* The heading box, label and chevron are the shared `.t-sidebar-heading`
   style (styles/index.css); only the group actions are this component's. */
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
  background: var(--tnzi-ai-sidebar-hover);
  color: var(--tnzi-ai-text);
}
.t-sidebar-nav__items {
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.t-sidebar-nav__item {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  /* Row tokens: a shell sets touch sizes on a phone (see TChatApp). */
  height: var(--tnzi-ai-row-height, 32px);
  padding: 0 9px;
  border: none;
  background: transparent;
  border-radius: 8px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: var(--tnzi-ai-row-font-size, 14px);
  cursor: pointer;
  text-align: left;
  transition: background var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-sidebar-nav__item:hover {
  background: var(--tnzi-ai-sidebar-hover);
}
/* Selected is a darker neutral wash and nothing else: the label and icon keep
   the row's colours and weight, so the brand colour never tints the sidebar. */
.t-sidebar-nav__item.is-active {
  background: var(--tnzi-ai-sidebar-selected);
}
.t-sidebar-nav__item-icon {
  width: var(--tnzi-ai-row-icon-size, 18px);
  height: var(--tnzi-ai-row-icon-size, 18px);
  font-size: var(--tnzi-ai-row-icon-size, 18px);
  flex-shrink: 0;
  color: var(--tnzi-ai-text-secondary);
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
/* A badge that asks for attention: a filled pill, its ink chosen per fill so
   it reads in both modes (see the badge tokens). */
.t-sidebar-nav__item-badge--danger,
.t-sidebar-nav__item-badge--warning {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 5px;
  box-sizing: border-box;
  border-radius: 9px;
  font-weight: 600;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}
.t-sidebar-nav__item-badge--danger {
  background: var(--tnzi-ai-badge-danger);
  color: var(--tnzi-ai-on-badge-danger);
}
.t-sidebar-nav__item-badge--warning {
  background: var(--tnzi-ai-badge-warning);
  color: var(--tnzi-ai-on-badge-warning);
}

/* ── Rows with a trailing action ─────────────────────────────────────────
   The row button keeps the plain row's look; the box around it owns the
   hover, so the fill stays while the pointer is on the action. */
.t-sidebar-nav__row {
  position: relative;
  display: flex;
}
.t-sidebar-nav__row:hover > .t-sidebar-nav__item:not(.is-active) {
  background: var(--tnzi-ai-sidebar-hover);
}
/* The action overhangs the row's 9px padding by 5px, as the conversation
   rows' "..." does, so the space kept for it is 5px less than its size. */
.t-sidebar-nav__item-end {
  flex-shrink: 0;
  display: flex;
  justify-content: flex-end;
  min-width: calc(var(--tnzi-ai-row-action-size, 24px) - 5px);
}
.t-sidebar-nav__item-action {
  position: absolute;
  top: 50%;
  right: 4px;
  transform: translateY(-50%);
  width: var(--tnzi-ai-row-action-size, 24px);
  height: var(--tnzi-ai-row-action-size, 24px);
  border: none;
  background: none;
  border-radius: 6px;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  /* Hidden until the row is hovered or holds keyboard focus, so the nav is
     not a column of buttons; on a touch screen there is no hover, so it
     always shows. */
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease),
    background var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease),
    color var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-sidebar-nav__row:hover .t-sidebar-nav__item-action,
.t-sidebar-nav__row:focus-within .t-sidebar-nav__item-action {
  opacity: 1;
}
.t-sidebar-nav__row:hover .t-sidebar-nav__item-badge,
.t-sidebar-nav__row:focus-within .t-sidebar-nav__item-badge {
  visibility: hidden;
}
@media (hover: none) {
  .t-sidebar-nav__item-action {
    opacity: 1;
  }
  .t-sidebar-nav__row .t-sidebar-nav__item-badge {
    visibility: hidden;
  }
}
.t-sidebar-nav__item-action:hover {
  /* Over the row's own hover fill, so the two add up to a deeper wash. */
  background: var(--tnzi-ai-sidebar-hover);
  color: var(--tnzi-ai-text);
}
</style>
