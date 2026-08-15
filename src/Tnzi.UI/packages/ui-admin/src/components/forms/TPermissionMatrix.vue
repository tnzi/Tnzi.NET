<template>
  <!-- ── Mobile (<md): module cards with action-chip buttons ─────────────── -->
  <div v-if="isSm" class="t-perm-matrix t-perm-matrix--mobile">
    <template v-for="group in visibleGroups" :key="group.module.id">
    <div
      v-if="group.sectionStart"
      class="t-perm-matrix__msection"
      :class="`is-${group.sectionStart.origin}`"
    >
      <span class="t-perm-matrix__section-label">
        {{ translate(`matrix.section.${group.sectionStart.origin}`) }}
      </span>
      <span class="t-perm-matrix__section-count">{{ group.sectionStart.count }}</span>
    </div>
    <div class="t-perm-matrix__mcard">
      <!-- role/tabindex/keydown, not a bare click handler: this is the only way
           to open a module's permissions on this breakpoint, so without them a
           keyboard user cannot reach any of them. -->
      <div
        class="t-perm-matrix__mcard-head"
        role="button"
        tabindex="0"
        :aria-expanded="isExpanded(group.module.id)"
        @click="toggleExpand(group.module.id)"
        @keydown.enter.prevent="toggleExpand(group.module.id)"
        @keydown.space.prevent="toggleExpand(group.module.id)"
      >
        <TSvgIcon
          icon="mdi:menu-right"
          :size="18"
          class="t-perm-matrix__chevron"
          :class="{ 'is-open': isExpanded(group.module.id) }"
        />
        <div class="t-perm-matrix__mcard-body">
        <div class="t-perm-matrix__mcard-title">
          <span class="t-perm-matrix__module-name">{{ moduleLabel(group.module) }}</span>
          <code
            v-if="!isCodeRedundant(moduleLabel(group.module), group.module.code)"
            class="t-perm-matrix__code"
          >{{ group.module.code }}</code>
          <NTag
            v-if="group.technical"
            size="tiny"
            :bordered="false"
            type="warning"
            :title="translate('matrix.technicalTip')"
          >
            {{ translate('technicalBadge') }}
          </NTag>
          <span class="t-perm-matrix__mcard-ops" @click.stop>
            <NButton
              size="tiny"
              secondary
              type="primary"
              :disabled="readonly || group.toggleable.length === 0"
              @click="toggleFunctions(group.toggleable, true)"
            >
              {{ translate('matrix.moduleAll') }}
            </NButton>
            <NButton
              size="tiny"
              secondary
              :disabled="readonly || group.toggleable.length === 0"
              @click="toggleFunctions(group.toggleable, false)"
            >
              {{ translate('matrix.moduleClear') }}
            </NButton>
          </span>
        </div>
        <div class="t-perm-matrix__module-meta">
          <span class="t-perm-matrix__bar">
            <span
              class="t-perm-matrix__bar-fill"
              :style="{ width: `${group.totalCount ? Math.round((group.checkedCount / group.totalCount) * 100) : 0}%` }"
            />
          </span>
          <span class="t-perm-matrix__module-count">{{ group.checkedCount }} / {{ group.totalCount }}</span>
        </div>
        </div>
      </div>
      <template v-if="isExpanded(group.module.id)">
        <div
          v-for="surface in group.surfaces"
          :key="surface.prefix"
          class="t-perm-matrix__mpanel"
          :class="{ 'is-access': surface.isAccess }"
        >
          <div class="t-perm-matrix__surface-title">
            <NCheckbox
              :checked="surface.state === 'all'"
              :indeterminate="surface.state === 'some'"
              :disabled="readonly || surface.toggleable.length === 0"
              @update:checked="(v: boolean) => toggleFunctions(surface.toggleable, v)"
            />
            <span class="t-perm-matrix__surface-name">{{ surfaceLabel(surface) }}</span>
            <NTag
              v-if="surface.isAccess"
              size="tiny"
              :bordered="false"
              type="info"
              :title="translate('matrix.menuEntryTip')"
            >
              {{ translate('matrix.menuEntry') }}
            </NTag>
            <NTag
              v-if="surface.technical && !group.technical"
              size="tiny"
              :bordered="false"
              type="warning"
              :title="translate('matrix.technicalTip')"
            >
              {{ translate('technicalBadge') }}
            </NTag>
          </div>
          <code
            v-if="!isCodeRedundant(surfaceLabel(surface), surface.prefix)"
            class="t-perm-matrix__surface-code t-perm-matrix__mpanel-code"
          >{{ surface.prefix }}</code>
          <div class="t-perm-matrix__chips">
            <template v-for="col in ACTION_COLUMNS" :key="col">
              <span
                v-if="surface.actions[col] && !readonly && isCellDisabled(surface.actions[col]!)"
                class="t-perm-matrix__chip-blocked"
                :title="cellTitle(surface.actions[col]!)"
              >
                {{ translate(`matrix.${col}`) }}
              </span>
              <NButton
                v-else-if="surface.actions[col]"
                size="small"
                :type="checkedSet.has(surface.actions[col]!.id) ? 'primary' : 'default'"
                :disabled="readonly"
                :title="cellTitle(surface.actions[col]!)"
                @click="onCellClick(surface.actions[col])"
              >
                <template v-if="checkedSet.has(surface.actions[col]!.id)" #icon>
                  <TSvgIcon icon="mdi:check" :size="14" />
                </template>
                {{ translate(`matrix.${col}`) }}
              </NButton>
            </template>
            <template v-for="item in surface.special" :key="item.fn.id">
              <span
                v-if="!readonly && isCellDisabled(item.fn)"
                class="t-perm-matrix__chip-blocked"
                :title="cellTitle(item.fn)"
              >
                {{ translate(`matrix.${item.kind}`) }}
              </span>
              <NButton
                v-else
                size="small"
                :type="checkedSet.has(item.fn.id) ? 'warning' : 'default'"
                :disabled="readonly"
                :title="cellTitle(item.fn)"
                @click="toggleFunctions([item.fn], !checkedSet.has(item.fn.id))"
              >
                <template v-if="checkedSet.has(item.fn.id)" #icon>
                  <TSvgIcon icon="mdi:check" :size="14" />
                </template>
                {{ translate(`matrix.${item.kind}`) }}
              </NButton>
            </template>
          </div>
        </div>
      </template>
    </div>
    </template>
    <div v-if="visibleGroups.length === 0" class="t-perm-matrix__empty">
      {{ translate('matrix.empty') }}
    </div>
  </div>

  <!-- ── Desktop (md+): panel × action grid ──────────────────────────────── -->
  <div v-else class="t-perm-matrix">
    <table class="t-perm-matrix__table">
      <thead>
        <tr>
          <th class="t-perm-matrix__surface-col">
            {{ translate('matrix.surface') }}
            <span class="t-perm-matrix__col-sub">/ {{ translate('matrix.code') }}</span>
          </th>
          <th
            v-for="col in ACTION_COLUMNS"
            :key="col"
            class="t-perm-matrix__action-col"
            :title="translate(`matrix.colTip.${col}`)"
          >
            <span class="t-perm-matrix__col-head">{{ translate(`matrix.${col}`) }}</span>
            <span class="t-perm-matrix__col-sub">
              <template v-if="translate(`matrix.${col}`).toLowerCase() !== col">{{ col }} </template>
              {{ columnCounts(col).checked }}/{{ columnCounts(col).total }}
            </span>
          </th>
          <th class="t-perm-matrix__special-col" :title="translate('matrix.colTip.special')">
            {{ translate('matrix.special') }}
          </th>
        </tr>
      </thead>
      <tbody v-for="group in visibleGroups" :key="group.module.id">
        <tr
          v-if="group.sectionStart"
          class="t-perm-matrix__section-row"
          :class="`is-${group.sectionStart.origin}`"
        >
          <td :colspan="totalCols">
            <span class="t-perm-matrix__section-label">
              {{ translate(`matrix.section.${group.sectionStart.origin}`) }}
            </span>
            <span class="t-perm-matrix__section-count">{{ group.sectionStart.count }}</span>
          </td>
        </tr>
        <tr
          class="t-perm-matrix__module-row"
          :class="{ 'is-expanded': isExpanded(group.module.id) }"
          @click="toggleExpand(group.module.id)"
        >
          <td class="t-perm-matrix__module-cell">
            <div class="t-perm-matrix__module-inner">
            <TSvgIcon
              icon="mdi:menu-right"
              :size="18"
              class="t-perm-matrix__chevron"
              :class="{ 'is-open': isExpanded(group.module.id) }"
            />
            <div class="t-perm-matrix__module-body">
            <div class="t-perm-matrix__module-title">
              <span class="t-perm-matrix__module-name">{{ moduleLabel(group.module) }}</span>
              <code
                v-if="!isCodeRedundant(moduleLabel(group.module), group.module.code)"
                class="t-perm-matrix__code"
              >{{ group.module.code }}</code>
              <NTag
                v-if="group.technical"
                size="tiny"
                :bordered="false"
                type="warning"
                :title="translate('matrix.technicalTip')"
              >
                {{ translate('technicalBadge') }}
              </NTag>
            </div>
            <div class="t-perm-matrix__module-meta">
              <span class="t-perm-matrix__bar">
                <span
                  class="t-perm-matrix__bar-fill"
                  :style="{ width: `${group.totalCount ? Math.round((group.checkedCount / group.totalCount) * 100) : 0}%` }"
                />
              </span>
              <span class="t-perm-matrix__module-count">{{ group.checkedCount }} / {{ group.totalCount }}</span>
            </div>
            </div>
            </div>
          </td>
          <td v-for="col in ACTION_COLUMNS" :key="col" class="t-perm-matrix__module-num-cell">
            <span
              class="t-perm-matrix__module-num"
              :class="{ 'is-on': moduleColumnChecked(group, col) > 0 }"
            >
              {{ moduleColumnChecked(group, col) }}
            </span>
          </td>
          <td class="t-perm-matrix__module-ops" @click.stop>
            <NButton
              size="tiny"
              secondary
              type="primary"
              :disabled="readonly || group.toggleable.length === 0"
              @click="toggleFunctions(group.toggleable, true)"
            >
              {{ translate('matrix.moduleAll') }}
            </NButton>
            <NButton
              size="tiny"
              secondary
              :disabled="readonly || group.toggleable.length === 0"
              @click="toggleFunctions(group.toggleable, false)"
            >
              {{ translate('matrix.moduleClear') }}
            </NButton>
          </td>
        </tr>
        <template v-if="isExpanded(group.module.id)">
          <tr
            v-for="surface in group.surfaces"
            :key="surface.prefix"
            class="t-perm-matrix__surface-row"
            :class="{ 'is-access': surface.isAccess }"
          >
            <td class="t-perm-matrix__surface-cell">
              <div class="t-perm-matrix__surface-inner">
              <NCheckbox
                :checked="surface.state === 'all'"
                :indeterminate="surface.state === 'some'"
                :disabled="readonly || surface.toggleable.length === 0"
                @update:checked="(v: boolean) => toggleFunctions(surface.toggleable, v)"
              />
              <span class="t-perm-matrix__surface-text">
                <span class="t-perm-matrix__surface-title">
                  <span class="t-perm-matrix__surface-name">{{ surfaceLabel(surface) }}</span>
                  <NTag
                    v-if="surface.isAccess"
                    size="tiny"
                    :bordered="false"
                    type="info"
                    class="t-perm-matrix__badge"
                    :title="translate('matrix.menuEntryTip')"
                  >
                    {{ translate('matrix.menuEntry') }}
                  </NTag>
                  <NTag
                    v-if="surface.technical && !group.technical"
                    size="tiny"
                    :bordered="false"
                    type="warning"
                    class="t-perm-matrix__badge"
                    :title="translate('matrix.technicalTip')"
                  >
                    {{ translate('technicalBadge') }}
                  </NTag>
                </span>
                <code
                  v-if="!isCodeRedundant(surfaceLabel(surface), surface.prefix)"
                  class="t-perm-matrix__surface-code"
                >{{ surface.prefix }}</code>
              </span>
              </div>
            </td>
            <td
              v-for="col in ACTION_COLUMNS"
              :key="col"
              class="t-perm-matrix__cell"
              :class="{
                'is-clickable': surface.actions[col] && !readonly && !isCellDisabled(surface.actions[col]!),
              }"
              :title="surface.actions[col] ? cellTitle(surface.actions[col]!) : undefined"
              @click="onCellClick(surface.actions[col])"
            >
              <span
                v-if="surface.actions[col] && !readonly && isCellDisabled(surface.actions[col]!)"
                class="t-perm-matrix__hatch-box"
              />
              <NCheckbox
                v-else-if="surface.actions[col]"
                size="large"
                :checked="checkedSet.has(surface.actions[col]!.id)"
                :disabled="readonly"
                @click.stop
                @update:checked="(v: boolean) => toggleFunctions([surface.actions[col]!], v)"
              />
              <span v-else class="t-perm-matrix__na">·</span>
            </td>
            <td class="t-perm-matrix__special-cell">
              <template v-if="surface.special.length > 0">
                <template v-for="item in surface.special" :key="item.fn.id">
                  <span
                    v-if="!readonly && isCellDisabled(item.fn)"
                    class="t-perm-matrix__chip-blocked t-perm-matrix__special-pill"
                    :title="cellTitle(item.fn)"
                  >
                    {{ translate(`matrix.${item.kind}`) }}
                  </span>
                  <!-- A checkbox (not a bare colour-only pill) so granted vs not is
                       obvious at a glance; the label keeps the danger tint for the
                       powerful execute/assign grants. -->
                  <NCheckbox
                    v-else
                    :checked="checkedSet.has(item.fn.id)"
                    :disabled="readonly"
                    class="t-perm-matrix__special-check"
                    :class="`is-${item.kind}`"
                    :title="cellTitle(item.fn)"
                    @update:checked="(v: boolean) => toggleFunctions([item.fn], v)"
                  >
                    {{ translate(`matrix.${item.kind}`) }}
                  </NCheckbox>
                </template>
              </template>
              <span v-else class="t-perm-matrix__na">·</span>
            </td>
          </tr>
        </template>
      </tbody>
    </table>
    <div v-if="visibleGroups.length === 0" class="t-perm-matrix__empty">
      {{ translate('matrix.empty') }}
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * Permission assignment MATRIX - rows are permission panels (an entity's
 * code prefix, e.g. `user` / `finance.account`), columns are the crud actions
 * (view / create / update / delete) plus a trailing "special" column for
 * trigger-style actions (`.execute`, rendered as warning-toned checkable
 * pills) and the role-grant action (`.assign`).
 *
 * Readability layers on top of the raw grid:
 *  - Modules are COLLAPSIBLE sections: each module row carries a granted
 *    progress bar, per-action granted counts aligned under the action
 *    columns, and "All / Clear" shortcut buttons. `defaultExpanded` opens
 *    everything; `expandFirst` opens just the first module (one-shot, so
 *    "collapse all" still wins afterwards). A keyword filter force-expands
 *    the matching modules.
 *  - Column headers show the action label plus a catalogue-wide granted
 *    count (and the raw action code when the label is localized).
 *  - Module ACCESS codes (`{group}.view` such as `ai.view`, which gate the
 *    sidebar group itself) are detected, pinned first and tagged "menu
 *    entry"; modules whose every panel is Technical carry the badge on the
 *    module row instead of repeating it per panel.
 *  - Panel names render on two lines (localized name + monospaced code),
 *    whole cells toggle on click, checked cells get a tinted background.
 *    Delegation-blocked cells render as hatched boxes - visually distinct
 *    from "not granted"; missing actions are a soft dot.
 *  - **Mobile (<md)**: the table becomes module CARDS - every panel lists
 *    its available actions as tappable chip buttons (granted = filled +
 *    check, special = warning tone, blocked = hatched, missing actions are
 *    simply omitted). Same state model, same emits.
 *  - `labelOverrides` maps panel prefixes (and `module:{code}` keys) to
 *    localized display names; unmapped codes fall back to the backend
 *    display name.
 *
 * Delegation-aware: `grantableCodes` (the grantor's own permission set)
 * blocks cells the current user cannot hand out - mirroring the backend
 * guard (`GetRoleGrantViolationAsync`), which remains the real enforcement.
 * `null`/`undefined` = everything grantable (super admin / permissions not
 * loaded).
 */
import { computed, ref, watch } from 'vue'
import { NCheckbox, NTag, NButton } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { isCodeRedundant } from '../../headless/code-label'
import type { FunctionModuleDto, ModuleFunctionDto } from '@tnzi/core/services/authorization'
import { PermissionCategory } from '@tnzi/core/services/authorization'

const ACTION_COLUMNS = ['view', 'create', 'update', 'delete'] as const
type ActionColumn = (typeof ACTION_COLUMNS)[number]
// Non-CRUD trailing segments that render as pills in the trailing "special"
// column rather than as their own surface row: `.execute` (trigger actions),
// `.assign` (role-grant), and `.use` (grant-to-use white-list, e.g. chat.use).
const SPECIAL_ACTIONS = ['execute', 'assign', 'use'] as const

interface SpecialItem {
  /** i18n suffix: 'execute' | 'assign' | 'access' (unrecognized trailing segment). */
  kind: string
  fn: ModuleFunctionDto
}

interface SurfaceRow {
  prefix: string
  label: string
  technical: boolean
  /** Module access code (`{group}.view`) gating the sidebar group itself. */
  isAccess: boolean
  order: number
  actions: Partial<Record<ActionColumn, ModuleFunctionDto>>
  special: SpecialItem[]
  /** Cells the current grantor may toggle (enabled + grantable). */
  toggleable: ModuleFunctionDto[]
  state: 'all' | 'some' | 'none'
}

/** First-group-of-its-origin marker driving the section sub-headers. */
interface SectionStart {
  origin: 'app' | 'builtin'
  count: number
}

interface ModuleGroup {
  module: FunctionModuleDto
  surfaces: SurfaceRow[]
  toggleable: ModuleFunctionDto[]
  state: 'all' | 'some' | 'none'
  /** Every panel of the module is Technical → badge the module row once. */
  technical: boolean
  /** Granted / total across every function of the module (incl. disabled). */
  checkedCount: number
  totalCount: number
  /**
   * `app` = a consumer application's own permission module; `builtin` = a
   * FRAMEWORK catalogue module (backend `FunctionModuleDto.isBuiltIn`). Consumer
   * modules sort first.
   */
  origin: 'app' | 'builtin'
  /** Set on the first group of each origin when both origins are present. */
  sectionStart?: SectionStart | null
}

const props = withDefaults(
  defineProps<{
    modules: FunctionModuleDto[]
    functionsByModule: Map<string, ModuleFunctionDto[]>
    checkedIds: string[]
    /** Grantor's own permission codes; null/undefined = everything grantable. */
    grantableCodes?: string[] | null
    /** Keyword filter over panel label / prefix (case-insensitive). */
    keyword?: string
    /**
     * Localized display names: panel prefix → label, plus `module:{code}`
     * keys for module headers. Missing keys fall back to backend names.
     */
    labelOverrides?: Record<string, string> | null
    /** Start with every module section expanded. */
    defaultExpanded?: boolean
    /** Auto-expand the FIRST module once data arrives (one-shot). */
    expandFirst?: boolean
    /**
     * Read-only display: every permission renders as granted (checked) and
     * locked (no toggle). Used for a super-admin role, which bypasses every
     * check and effectively holds the whole catalogue - so an explicit,
     * editable row set is meaningless. Browsing (expand/collapse, search) stays.
     */
    readonly?: boolean
    translate: (key: string, params?: Record<string, unknown>) => string
  }>(),
  {
    grantableCodes: null,
    keyword: '',
    labelOverrides: null,
    defaultExpanded: false,
    expandFirst: false,
    readonly: false,
  },
)

const emit = defineEmits<{
  (e: 'update:checkedIds', ids: string[]): void
}>()

const { isSm } = useBreakpoint()

const checkedSet = computed(() => {
  // Read-only (super-admin): treat the entire catalogue as granted so every
  // count, progress bar, and cell renders fully-checked. Interactions are
  // separately locked in the toggle/click handlers and cell `:disabled`.
  if (props.readonly) {
    const all = new Set<string>()
    for (const fns of props.functionsByModule.values()) {
      for (const fn of fns) all.add(fn.id)
    }
    return all
  }
  return new Set(props.checkedIds)
})

const grantableSet = computed<Set<string> | null>(() => {
  if (props.grantableCodes == null) return null
  return new Set(props.grantableCodes.map((c) => c.toLowerCase()))
})

function isGrantable(fn: ModuleFunctionDto): boolean {
  const set = grantableSet.value
  return set === null || set.has(fn.code.toLowerCase())
}

function isCellDisabled(fn: ModuleFunctionDto): boolean {
  return !fn.isEnabled || !isGrantable(fn)
}

function cellTitle(fn: ModuleFunctionDto): string {
  if (!fn.isEnabled) return `${fn.code} - ${props.translate('matrix.disabled')}`
  if (!isGrantable(fn)) return `${fn.code} - ${props.translate('matrix.notGrantable')}`
  return fn.code
}

function splitCode(code: string): { prefix: string; action: string | null } {
  const idx = code.lastIndexOf('.')
  if (idx < 0) return { prefix: code, action: null }
  const seg = code.slice(idx + 1).toLowerCase()
  if ((ACTION_COLUMNS as readonly string[]).includes(seg) || (SPECIAL_ACTIONS as readonly string[]).includes(seg)) {
    return { prefix: code.slice(0, idx), action: seg }
  }
  return { prefix: code, action: null }
}

function stateOf(toggleable: ModuleFunctionDto[]): 'all' | 'some' | 'none' {
  if (toggleable.length === 0) return 'none'
  let checked = 0
  for (const fn of toggleable) {
    if (checkedSet.value.has(fn.id)) checked += 1
  }
  if (checked === 0) return 'none'
  return checked === toggleable.length ? 'all' : 'some'
}

function surfaceLabel(surface: SurfaceRow): string {
  return props.labelOverrides?.[surface.prefix] ?? surface.label
}

function moduleLabel(module: FunctionModuleDto): string {
  return props.labelOverrides?.[`module:${module.code}`] ?? module.name
}

const groups = computed<ModuleGroup[]>(() => {
  const result: ModuleGroup[] = []
  const sortedModules = [...props.modules].sort(
    (a, b) => (a.order ?? 0) - (b.order ?? 0) || a.name.localeCompare(b.name),
  )
  for (const module of sortedModules) {
    const fns = props.functionsByModule.get(module.id) ?? []
    if (fns.length === 0) continue

    const byPrefix = new Map<string, SurfaceRow>()
    for (const fn of fns) {
      const { prefix, action } = splitCode(fn.code)
      let surface = byPrefix.get(prefix)
      if (!surface) {
        surface = {
          prefix,
          label: prefix,
          technical: false,
          isAccess: false,
          order: fn.order ?? 0,
          actions: {},
          special: [],
          toggleable: [],
          state: 'none',
        }
        byPrefix.set(prefix, surface)
      }
      surface.order = Math.min(surface.order, fn.order ?? 0)
      if (action && (ACTION_COLUMNS as readonly string[]).includes(action)) {
        surface.actions[action as ActionColumn] = fn
        if (action === 'view') {
          // Row label from the view code's display name: "View Users" → "Users".
          surface.label = fn.name.replace(/^View\s+/i, '') || prefix
          surface.technical = fn.category === PermissionCategory.Technical
        }
      } else {
        surface.special.push({
          kind: action && (SPECIAL_ACTIONS as readonly string[]).includes(action) ? action : 'access',
          fn,
        })
        // Surfaces with no view code (e.g. a consumer's custom access code) label from the fn name.
        if (!surface.actions.view && surface.label === prefix) {
          surface.label = fn.name
          surface.technical = fn.category === PermissionCategory.Technical
        }
      }
      if (!isCellDisabled(fn)) surface.toggleable.push(fn)
    }

    // Module ACCESS codes: a view-only surface whose prefix is the parent of
    // other surfaces in the module (`ai` for `ai.agent`, `blog` for
    // `blog.post`) gates the sidebar group, not an entity - tag it and
    // pin it above the entity rows so it stops reading as a duplicate of the
    // module header.
    const allPrefixes = [...byPrefix.keys()]
    for (const s of byPrefix.values()) {
      const viewOnly =
        !!s.actions.view && !s.actions.create && !s.actions.update && !s.actions.delete && s.special.length === 0
      s.isAccess = viewOnly && allPrefixes.some((p) => p !== s.prefix && p.startsWith(`${s.prefix}.`))
    }

    const surfaces = [...byPrefix.values()].sort(
      (a, b) =>
        Number(b.isAccess) - Number(a.isAccess) || a.order - b.order || a.prefix.localeCompare(b.prefix),
    )
    for (const s of surfaces) s.state = stateOf(s.toggleable)

    const toggleable = surfaces.flatMap((s) => s.toggleable)
    let checkedCount = 0
    for (const fn of fns) {
      if (checkedSet.value.has(fn.id)) checkedCount += 1
    }
    result.push({
      module,
      surfaces,
      toggleable,
      state: stateOf(toggleable),
      technical: surfaces.length > 0 && surfaces.every((s) => s.technical),
      checkedCount,
      totalCount: fns.length,
      origin: module.isBuiltIn === true ? 'builtin' : 'app',
    })
  }
  // A consumer application's own modules first, framework built-ins after -
  // stable within each origin so the order/name ordering above is kept. When
  // the backend does not flag origins (older backend → every module is `app`)
  // this is a no-op reorder.
  result.sort((a, b) => originRank(a.origin) - originRank(b.origin))
  return result
})

function originRank(origin: 'app' | 'builtin'): number {
  return origin === 'app' ? 0 : 1
}

/**
 * Mark the first group of each origin so the render can emit a section
 * sub-header ("Application" / "Built-in"). Only sections when BOTH origins are
 * present - a base framework with no consumer permissions (or a keyword filter
 * that leaves one origin) behaves exactly as before, with no lone header.
 */
function decorateSections(list: ModuleGroup[]): ModuleGroup[] {
  const hasApp = list.some((g) => g.origin === 'app')
  const hasBuiltin = list.some((g) => g.origin === 'builtin')
  if (!hasApp || !hasBuiltin) {
    return list.map((g) => ({ ...g, sectionStart: null }))
  }
  const appCount = list.filter((g) => g.origin === 'app').length
  const builtinCount = list.length - appCount
  let markedApp = false
  let markedBuiltin = false
  return list.map((g) => {
    let sectionStart: SectionStart | null = null
    if (g.origin === 'app' && !markedApp) {
      markedApp = true
      sectionStart = { origin: 'app', count: appCount }
    } else if (g.origin === 'builtin' && !markedBuiltin) {
      markedBuiltin = true
      sectionStart = { origin: 'builtin', count: builtinCount }
    }
    return { ...g, sectionStart }
  })
}

/** Table column count (panel + action columns + special) for section header colspan. */
const totalCols = computed(() => ACTION_COLUMNS.length + 2)

const visibleGroups = computed<ModuleGroup[]>(() => {
  const kw = (props.keyword ?? '').trim().toLowerCase()
  if (!kw) return decorateSections(groups.value)
  const filtered: ModuleGroup[] = []
  for (const g of groups.value) {
    const surfaces = g.surfaces.filter(
      (s) =>
        s.label.toLowerCase().includes(kw) ||
        s.prefix.toLowerCase().includes(kw) ||
        surfaceLabel(s).toLowerCase().includes(kw),
    )
    if (surfaces.length > 0) {
      const toggleable = surfaces.flatMap((s) => s.toggleable)
      filtered.push({
        module: g.module,
        surfaces,
        toggleable,
        state: stateOf(toggleable),
        technical: g.technical,
        checkedCount: g.checkedCount,
        totalCount: g.totalCount,
        origin: g.origin,
      })
    }
  }
  return decorateSections(filtered)
})

// ── Collapse / expand ──────────────────────────────────────────────────────
// The first screen is a per-module overview; `expandFirst` (page default)
// opens the first module once data arrives, mirroring the reference design.
// An active keyword force-expands the matching sections. Modules load async,
// so the state is "base default + per-module override" rather than a
// snapshot of ids.
const baseExpanded = ref(props.defaultExpanded)
const expandOverrides = ref(new Map<string, boolean>())
const expandFirstDone = ref(false)

watch(
  groups,
  (gs) => {
    if (!props.expandFirst || expandFirstDone.value || gs.length === 0) return
    expandFirstDone.value = true
    const first = gs[0]!.module.id
    if (!expandOverrides.value.has(first)) {
      const next = new Map(expandOverrides.value)
      next.set(first, true)
      expandOverrides.value = next
    }
  },
  { immediate: true },
)

const keywordActive = computed(() => (props.keyword ?? '').trim().length > 0)

function isExpandedRaw(moduleId: string): boolean {
  return expandOverrides.value.get(moduleId) ?? baseExpanded.value
}

function isExpanded(moduleId: string): boolean {
  return keywordActive.value || isExpandedRaw(moduleId)
}

function toggleExpand(moduleId: string): void {
  const next = new Map(expandOverrides.value)
  next.set(moduleId, !isExpandedRaw(moduleId))
  expandOverrides.value = next
}

function expandAll(): void {
  baseExpanded.value = true
  expandOverrides.value = new Map()
}

function collapseAll(): void {
  baseExpanded.value = false
  expandOverrides.value = new Map()
}

defineExpose({ expandAll, collapseAll })

// ── Counts ─────────────────────────────────────────────────────────────────
/** Catalogue-wide granted/total for one action column (collapse-independent). */
function columnCounts(col: ActionColumn): { checked: number; total: number } {
  let checked = 0
  let total = 0
  for (const g of groups.value) {
    for (const s of g.surfaces) {
      const fn = s.actions[col]
      if (!fn) continue
      total += 1
      if (checkedSet.value.has(fn.id)) checked += 1
    }
  }
  return { checked, total }
}

/** Granted count for one action column within a module. */
function moduleColumnChecked(group: ModuleGroup, col: ActionColumn): number {
  let n = 0
  for (const s of group.surfaces) {
    const fn = s.actions[col]
    if (fn && checkedSet.value.has(fn.id)) n += 1
  }
  return n
}

function onCellClick(fn: ModuleFunctionDto | undefined): void {
  if (props.readonly || !fn || isCellDisabled(fn)) return
  toggleFunctions([fn], !checkedSet.value.has(fn.id))
}

function toggleFunctions(fns: ModuleFunctionDto[], checked: boolean): void {
  if (props.readonly || fns.length === 0) return
  const next = new Set(checkedSet.value)
  for (const fn of fns) {
    if (isCellDisabled(fn)) continue
    if (checked) next.add(fn.id)
    else next.delete(fn.id)
  }
  emit('update:checkedIds', [...next])
}
</script>

<style scoped>
.t-perm-matrix {
  overflow: auto;
}
.t-perm-matrix__table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}
.t-perm-matrix__table th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--tnzi-layout-bg);
  text-align: left;
  font-weight: 600;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  padding: 6px 10px;
  border-bottom: 1px solid var(--tnzi-border);
  white-space: nowrap;
  vertical-align: middle;
}
.t-perm-matrix__col-head {
  color: var(--tnzi-base-text);
  font-size: 12.5px;
}
.t-perm-matrix__col-sub {
  display: block;
  font-weight: 400;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  margin-top: 2px;
}
.t-perm-matrix__surface-col .t-perm-matrix__col-sub {
  display: inline;
  margin-top: 0;
}
.t-perm-matrix__action-col {
  width: 72px;
  text-align: center !important;
}
.t-perm-matrix__action-col .t-perm-matrix__col-sub {
  text-align: center;
}
.t-perm-matrix__special-col {
  width: 118px;
  text-align: center !important;
}

/* ── Origin sections (Application vs Built-in) ─────────────────────────── */
/* A slim sub-header splitting the consumer application's own permission
   modules (shown first) from the framework built-in catalogue. Only rendered
   when both origins are present. */
.t-perm-matrix__section-row td {
  padding: 7px 10px 5px;
  background: var(--tnzi-layout-bg);
  border-top: 1px solid var(--tnzi-border);
  border-bottom: 1px solid var(--tnzi-border);
}
.t-perm-matrix__section-label {
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--tnzi-base-text-muted);
}
.t-perm-matrix__section-row.is-app .t-perm-matrix__section-label,
.t-perm-matrix__msection.is-app .t-perm-matrix__section-label {
  color: var(--tnzi-primary);
}
.t-perm-matrix__section-count {
  margin-left: 8px;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
}
.t-perm-matrix__msection {
  display: flex;
  align-items: baseline;
  gap: 4px;
  padding: 6px 2px 2px;
  margin-top: 2px;
  flex-shrink: 0;
}

/* ── Module section header ─────────────────────────────────────────────── */
.t-perm-matrix__module-row td {
  padding: 8px 10px;
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg));
  border-top: 1px solid var(--tnzi-border);
  border-bottom: 1px solid var(--tnzi-border);
  cursor: pointer;
  user-select: none;
}
.t-perm-matrix__module-row:hover td {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.04);
}
.t-perm-matrix__module-cell {
  min-width: 200px;
}
.t-perm-matrix__module-inner {
  display: flex;
  align-items: center;
  gap: 4px;
}
/* Desktop module row: the name/code/badge and the progress bar + count share
   ONE row - the bar is shoved to the right with margin-left:auto. (Mobile cards
   use `mcard-body` instead and keep the stacked layout.) */
.t-perm-matrix__module-body {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
  flex: 1;
}
.t-perm-matrix__module-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-weight: 600;
  white-space: nowrap;
}
.t-perm-matrix__module-body .t-perm-matrix__module-title {
  min-width: 0;
}
.t-perm-matrix__module-body .t-perm-matrix__module-name {
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-perm-matrix__module-body .t-perm-matrix__module-meta {
  margin-top: 0;
  margin-left: auto;
}
.t-perm-matrix__chevron {
  transition: transform 0.15s ease;
  color: var(--tnzi-base-text-muted);
  flex-shrink: 0;
}
.t-perm-matrix__chevron.is-open {
  transform: rotate(90deg);
}
.t-perm-matrix__module-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 4px;
  white-space: nowrap;
}
.t-perm-matrix__bar {
  width: 110px;
  height: 6px;
  border-radius: 3px;
  /* Explicit track grey - the old layout-bg token was invisible on the
     white module row. */
  background: var(--tnzi-border);
  overflow: hidden;
  flex-shrink: 0;
}
.t-perm-matrix__bar-fill {
  display: block;
  height: 100%;
  border-radius: 3px;
  background: var(--tnzi-primary);
  transition: width 0.2s ease;
}
.t-perm-matrix__module-count {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
}
.t-perm-matrix__module-num-cell {
  text-align: center;
}
.t-perm-matrix__module-num {
  display: inline-block;
  min-width: 22px;
  padding: 1px 4px;
  border-radius: 4px;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-base-text-muted);
  background: var(--tnzi-layout-bg);
}
.t-perm-matrix__module-num.is-on {
  color: var(--tnzi-primary);
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.1);
  font-weight: 600;
}
.t-perm-matrix__module-ops {
  white-space: nowrap;
  text-align: center;
}
.t-perm-matrix__module-ops .n-button + .n-button {
  margin-left: 6px;
}

/* ── Surface rows ──────────────────────────────────────────────────────── */
.t-perm-matrix__surface-row td {
  padding: 5px 10px;
  border-bottom: 1px dashed var(--tnzi-border);
}
.t-perm-matrix__surface-row:hover td {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.04);
}
.t-perm-matrix__surface-row.is-access td {
  background: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.05);
}
.t-perm-matrix__surface-cell {
  min-width: 200px;
  /* Indent panels one hierarchy level under their module header (the module
     chevron column stays to the left). */
  padding-left: 32px !important;
}
.t-perm-matrix__surface-inner {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-perm-matrix__surface-text {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}
.t-perm-matrix__surface-title {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.t-perm-matrix__surface-name {
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-perm-matrix__surface-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  white-space: nowrap;
}
.t-perm-matrix__cell {
  text-align: center;
}
.t-perm-matrix__cell.is-clickable {
  cursor: pointer;
}
/* Delegation-blocked / disabled: a hatched cell-sized box, visually distinct
   from an unchecked (grantable) checkbox. */
.t-perm-matrix__hatch-box {
  display: inline-block;
  width: 18px;
  height: 18px;
  border-radius: 4px;
  border: 1px solid var(--tnzi-border);
  background: repeating-linear-gradient(
    45deg,
    transparent,
    transparent 3px,
    var(--tnzi-border) 3px,
    var(--tnzi-border) 4px
  );
  vertical-align: middle;
}
.t-perm-matrix__special-cell {
  text-align: left;
}
/* Blocked (not-grantable) special still shows as an outlined chip. */
.t-perm-matrix__special-pill + .t-perm-matrix__special-pill {
  margin-left: 6px;
}
.t-perm-matrix__special-pill {
  border: 1px solid var(--tnzi-border);
}
/* Grantable special (execute / assign / use) renders as a LABELLED checkbox so
   granted vs not is obvious from the checkmark, not colour alone. execute and
   assign keep a warning-toned label - they are the powerful grants. */
.t-perm-matrix__special-check {
  font-size: 12.5px;
}
.t-perm-matrix__special-check + .t-perm-matrix__special-check {
  margin-left: 14px;
}
.t-perm-matrix__special-check.is-execute :deep(.n-checkbox__label),
.t-perm-matrix__special-check.is-assign :deep(.n-checkbox__label) {
  color: var(--tnzi-warning);
}
.t-perm-matrix__code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  background: var(--tnzi-layout-bg);
  padding: 1px 4px;
  border-radius: 3px;
  white-space: nowrap;
}
.t-perm-matrix__badge {
  white-space: nowrap;
}
.t-perm-matrix__na {
  color: var(--tnzi-base-text-muted);
  opacity: 0.5;
  font-size: 15px;
}
.t-perm-matrix__empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 32px 8px;
}

/* ── Mobile cards ──────────────────────────────────────────────────────── */
.t-perm-matrix--mobile {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.t-perm-matrix__mcard {
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg));
  overflow: hidden;
  /* Never let a height-capped flex-column parent shrink a collapsed card.
     `overflow: hidden` above makes the card's flex `min-height: auto` resolve
     to 0, so without this a `max-height` on the mobile list would crush every
     collapsed card to a sliver ("piled together"); the list scrolls instead. */
  flex-shrink: 0;
}
.t-perm-matrix__mcard-head {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 10px 12px;
  cursor: pointer;
  user-select: none;
}
.t-perm-matrix__mcard-body {
  flex: 1;
  min-width: 0;
}
.t-perm-matrix__mcard-title {
  display: flex;
  align-items: center;
  gap: 6px;
  row-gap: 6px;
  font-weight: 600;
  /* Long module names (Authorization / Notification) plus the code chip, the
     Technical badge and the All/Clear ops overflow a phone card row. Wrap the
     ops onto a second line instead of letting the squeezed name break one
     character per line. */
  flex-wrap: wrap;
}
.t-perm-matrix__mcard-title .t-perm-matrix__module-name {
  white-space: nowrap;
}
.t-perm-matrix__mcard-ops {
  margin-left: auto;
  display: inline-flex;
  gap: 6px;
}
.t-perm-matrix__mpanel {
  padding: 10px 12px;
  border-top: 1px dashed var(--tnzi-border);
}
.t-perm-matrix__mpanel.is-access {
  background: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.05);
}
.t-perm-matrix__mpanel-code {
  display: block;
  margin: 1px 0 8px 26px;
}
.t-perm-matrix__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-left: 26px;
}
.t-perm-matrix__chip-blocked {
  display: inline-flex;
  align-items: center;
  padding: 4px 12px;
  border-radius: var(--tnzi-admin-radius-md, 6px);
  border: 1px solid var(--tnzi-border);
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
  background: repeating-linear-gradient(
    45deg,
    transparent,
    transparent 4px,
    var(--tnzi-border) 4px,
    var(--tnzi-border) 5px
  );
  cursor: not-allowed;
}
</style>
