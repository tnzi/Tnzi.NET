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
/* Wraps when the label and the control do not fit side by side (a narrow
   settings sheet): the control moves under the label instead of squeezing the
   label to nothing or pushing past the pane. On a wide pane nothing wraps. */
.t-setting-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px 24px;
  padding: 14px 0;
}
.t-setting-row__text {
  flex: 1 1 12rem;
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
/* The text's `flex: 1 1 12rem` is a WIDTH basis for the side-by-side row. In a
   column the basis is the main axis, so it becomes a 12rem HEIGHT and opens an
   empty band between the description and the control. Size to content here. */
.t-setting-row--stacked .t-setting-row__text {
  flex: none;
}
.t-setting-row--stacked .t-setting-row__control {
  justify-content: flex-start;
}
</style>
