<script setup lang="ts">
/**
 * @experimental
 * TSettingRow - one label/description/control line inside a settings pane.
 *
 * The single most repeated shape in any settings surface: a name, an optional
 * explanatory line under it, and a control pinned to the right edge. Without
 * it every consumer re-derives the same flex row, and they drift.
 *
 * The control is the default slot, so it can be anything: a switch, a select,
 * a button, a read-only value, a cluster of buttons.
 *
 * @example
 * ```vue
 * <TSettingRow label="Browser notifications" description="Get notified when a task completes.">
 *   <NSwitch v-model:value="notify" />
 * </TSettingRow>
 * ```
 */
withDefaults(
  defineProps<{
    /** Row name. Ignored when the `label` slot is used. */
    label?: string
    /** Secondary line under the label. Ignored when the `description` slot is used. */
    description?: string
    /** Stack the control under the text instead of pinning it right. Use for
     *  wide controls (a full-width input, a row of theme tiles). */
    stacked?: boolean
  }>(),
  {
    label: '',
    description: '',
    stacked: false,
  },
)
</script>

<template>
  <div class="t-setting-row" :class="{ 't-setting-row--stacked': stacked }">
    <div class="t-setting-row__text">
      <div v-if="label || $slots.label" class="t-setting-row__label">
        <slot name="label">{{ label }}</slot>
      </div>
      <div v-if="description || $slots.description" class="t-setting-row__desc">
        <slot name="description">{{ description }}</slot>
      </div>
    </div>
    <div v-if="$slots.default" class="t-setting-row__control">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.t-setting-row {
  display: flex;
  align-items: center;
  gap: 24px;
  padding: 14px 0;
}
.t-setting-row__text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.t-setting-row__label {
  font-size: 14px;
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.t-setting-row__desc {
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-secondary);
}
/* The control must never be the thing that shrinks: a switch squeezed to 20px
   is unusable, while the description beside it reflows harmlessly. */
.t-setting-row__control {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 8px;
}

.t-setting-row--stacked {
  flex-direction: column;
  align-items: stretch;
  gap: 10px;
}
.t-setting-row--stacked .t-setting-row__control {
  justify-content: flex-start;
}
</style>
