<script setup lang="ts">
/**
 * TInlineField - a field written on a line, as on letter paper: the "To" and
 * "Subject" rows of a mail being written, a title row over an editor.
 *
 * The label sits at the start of the row in the secondary text colour, the
 * control fills the rest, and a hairline under the row is its only frame;
 * while focus is inside, the hairline turns the focus colour
 * (`--tnzi-ai-focus-border`). Rows stack into a sheet with no gap. The control
 * goes in the default slot and takes `.t-input t-input--bare` (or is a
 * `TMultiSelect`, a chip list, anything); the slot gives `id` for it, which
 * the label's `for` points at, so a click on the label focuses it and the
 * label is its accessible name. `#end` holds what goes at the row's end
 * ("Cc", "Bcc" toggles).
 *
 * ```vue
 * <TInlineField label="To">
 *   <template #default="{ id }"><input :id="id" v-model="to" class="t-input t-input--bare" /></template>
 *   <template #end><TButton variant="link" @click="showCc = true">Cc</TButton></template>
 * </TInlineField>
 * ```
 *
 * `labelWidth` gives every label of a sheet the same width, so the fields line
 * up; unset, the label is as wide as its words.
 */
import { computed, useId } from 'vue'

const props = withDefaults(
  defineProps<{
    label: string
    /** Width of the label (number = px), so the rows of a sheet line up. */
    labelWidth?: number | string
  }>(),
  { labelWidth: undefined },
)

defineSlots<{
  default?: (props: { id: string }) => unknown
  end?: () => unknown
}>()

const id = useId()
const labelStyle = computed(() => {
  const width = props.labelWidth
  if (width === undefined || width === '') return undefined
  return { width: typeof width === 'number' ? `${width}px` : width }
})
</script>

<template>
  <div class="t-inline-field" data-inline-field>
    <label class="t-inline-field__label" :for="id" :style="labelStyle">{{ label }}</label>
    <div class="t-inline-field__control">
      <slot :id="id" />
    </div>
    <div v-if="$slots.end" class="t-inline-field__end">
      <slot name="end" />
    </div>
  </div>
</template>

<style scoped>
.t-inline-field {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 40px;
  box-sizing: border-box;
  border-bottom: 1px solid var(--tnzi-ai-divider);
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
/* The underline is the field's only frame, so it is what shows focus; there is
   no box for a ring to go round. */
.t-inline-field:focus-within {
  border-bottom-color: var(--tnzi-ai-focus-border);
}
.t-inline-field__label {
  flex: none;
  color: var(--tnzi-ai-text-secondary);
  font-size: 13px;
  white-space: nowrap;
  cursor: default;
}
.t-inline-field__control {
  display: flex;
  flex: 1 1 auto;
  align-items: center;
  min-width: 0;
  min-height: 30px;
  font-size: 13px;
}
.t-inline-field__end {
  display: flex;
  flex: none;
  align-items: center;
  gap: 8px;
}
@media (prefers-reduced-motion: reduce) {
  .t-inline-field {
    transition: none;
  }
}
</style>
