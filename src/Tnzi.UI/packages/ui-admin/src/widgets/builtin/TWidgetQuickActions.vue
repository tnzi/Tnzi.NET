<script setup lang="ts">
/**
 * `TWidgetQuickActions` — grid of action tiles with icon + label.
 *
 * Each action can either navigate (`to`) or fire an arbitrary callback
 * (`onClick`). Use this to surface 4-8 frequent admin actions ("Add
 * user", "Open audit log", "Reindex KB", …) on the dashboard.
 */
import { computed } from 'vue'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import { TSvgIcon } from '@tnzi/ui'
import { maybeTranslate } from '../../pages/_shared/translate'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'

export interface QuickAction {
  /** Stable key for the v-for list. */
  key: string
  /** Iconify name (e.g. `mdi:account-plus`). */
  icon: string
  /** Display label — i18n key or raw text. */
  label: string
  /** Vue-router target. Mutually exclusive with `onClick`. */
  to?: RouteLocationRaw
  /** Custom click handler. Mutually exclusive with `to`. */
  onClick?: () => void | Promise<void>
  /**
   * Color tone for the icon chip. Defaults to `'primary'`. Matches the
   * KPI card tone palette so quick actions feel visually consistent with
   * the rest of the dashboard.
   */
  tone?: 'primary' | 'info' | 'success' | 'warning' | 'error'
  /**
   * Permission the action's destination requires. When set, the tile is hidden
   * from users who lack it (super-user bypass + fail-open) so a business admin
   * doesn't get a shortcut that only lands on a 403 (e.g. Settings →
   * system.parameter.view). Omit for always-shown actions.
   */
  permission?: string
}

interface Props {
  actions: QuickAction[]
  /**
   * Cards per row at md+ breakpoint. Default 4 (matches a 24-col split of
   * 6). Smaller screens always render 2 per row, then 1 on phones.
   */
  cols?: number
}

const props = withDefaults(defineProps<Props>(), {
  cols: 4,
})

const router = useRouter()
const authStore = useAdminAuthStore()

// Hide actions whose destination the user can't reach (super-user bypass +
// fail-open before permissions load), so a business admin never sees a tile
// that only bounces to /403.
const visibleActions = computed<QuickAction[]>(() => {
  const bypass = authStore.isSuperUser || authStore.userInfo === null
  if (bypass) return props.actions
  return props.actions.filter((a) => !a.permission || authStore.hasPermission(a.permission))
})

const gridStyle = computed(() => ({
  '--t-widget-actions-cols': String(props.cols),
}))

function resolveLabel(value: string): string {
  return maybeTranslate(value)
}

async function handleClick(action: QuickAction): Promise<void> {
  if (action.onClick) {
    await action.onClick()
    return
  }
  if (action.to) {
    await router.push(action.to)
  }
}
</script>

<template>
  <div class="t-widget-quick-actions" :style="gridStyle">
    <button
      v-for="action in visibleActions"
      :key="action.key"
      type="button"
      class="t-widget-quick-actions__tile"
      @click="handleClick(action)"
    >
      <span class="t-widget-quick-actions__icon" :data-tone="action.tone ?? 'primary'">
        <TSvgIcon :icon="action.icon" :size="22" />
      </span>
      <span class="t-widget-quick-actions__label">{{ resolveLabel(action.label) }}</span>
    </button>
  </div>
</template>

<style scoped>
.t-widget-quick-actions {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}
@media (min-width: 640px) {
  .t-widget-quick-actions {
    grid-template-columns: repeat(var(--t-widget-actions-cols, 4), minmax(0, 1fr));
  }
}
.t-widget-quick-actions__tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 14px 10px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  cursor: pointer;
  text-align: center;
  transition: transform 0.15s ease, box-shadow 0.15s ease, border-color 0.15s ease;
}
.t-widget-quick-actions__tile:hover {
  transform: translateY(-2px);
  border-color: var(--tnzi-primary);
  box-shadow: 0 4px 12px rgb(0 0 0 / 0.08);
}
.t-widget-quick-actions__icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.12);
  color: var(--tnzi-primary);
}
.t-widget-quick-actions__icon[data-tone='info'] {
  background: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.12);
  color: var(--tnzi-info);
}
.t-widget-quick-actions__icon[data-tone='success'] {
  background: rgb(var(--tnzi-success-rgb, 24 160 88) / 0.12);
  color: var(--tnzi-success);
}
.t-widget-quick-actions__icon[data-tone='warning'] {
  background: rgb(var(--tnzi-warning-rgb, 240 160 32) / 0.12);
  color: var(--tnzi-warning);
}
.t-widget-quick-actions__icon[data-tone='error'] {
  background: rgb(var(--tnzi-error-rgb, 208 48 80) / 0.12);
  color: var(--tnzi-error);
}
.t-widget-quick-actions__label {
  font-size: 12px;
  color: var(--tnzi-base-text);
  line-height: 1.3;
}
</style>
