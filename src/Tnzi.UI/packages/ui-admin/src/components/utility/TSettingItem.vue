<script setup lang="ts">
/**
 * `TSettingItem` - single drawer/list row with a left label, optional
 * help-tooltip icon, and a right slot for the control. Direct port of
 * soybean's `setting-item.vue`.
 *
 * Reference: `D:\Github\soybean-admin-example\src\components\common\
 *   setting-item.vue` (`flex-y-center justify-between` row with
 *   `<slot name="suffix">` for label-decoration like an `IconTooltip`).
 *
 * Usage:
 *
 * ```vue
 * <TSettingItem label="Tab cache" tooltip="Enables KeepAlive for tab pages">
 *   <NSwitch :value="..." />
 * </TSettingItem>
 * ```
 */
import { NTooltip } from 'naive-ui'
import { Icon } from '@iconify/vue'

interface Props {
  /** Left-side row label. */
  label: string
  /** Optional help-tooltip text. When set, renders a `?` icon next to
   *  the label that pops the tooltip on hover. */
  tooltip?: string
  /** Iconify icon for the help tooltip. Defaults to mdi help-circle. */
  tooltipIcon?: string
}

withDefaults(defineProps<Props>(), {
  tooltip: undefined,
  tooltipIcon: 'mdi:help-circle-outline',
})
</script>

<template>
  <section class="t-setting-item">
    <div class="t-setting-item__label-wrap">
      <span class="t-setting-item__label">{{ label }}</span>
      <NTooltip v-if="tooltip" placement="top" trigger="hover">
        <template #trigger>
          <Icon :icon="tooltipIcon" class="t-setting-item__tooltip-icon" />
        </template>
        {{ tooltip }}
      </NTooltip>
    </div>
    <div class="t-setting-item__control">
      <slot />
    </div>
  </section>
</template>

<style scoped>
.t-setting-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 8px 0;
}
.t-setting-item__label-wrap {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: var(--tnzi-base-text, #1f1f1f);
  font-size: 14px;
  /* Truncate when the label runs past the control. */
  min-width: 0;
}
.t-setting-item__label {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-setting-item__tooltip-icon {
  flex-shrink: 0;
  width: 14px;
  height: 14px;
  color: var(--tnzi-base-text-muted, #6b7280);
  cursor: help;
}
.t-setting-item__control {
  flex-shrink: 0;
}
</style>
