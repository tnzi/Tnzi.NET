<script setup lang="ts">
/**
 * TInfoTip - a small "i" button that opens a line of supplementary
 * information beside it, on a click, a tap or Enter / Space: the same on a
 * touch screen as with a mouse (a toggletip).
 *
 * A tooltip (`vTooltip`) is for the label of a control, which the control's
 * accessible name already carries; on a touch screen it shows only on a long
 * press, which nobody finds unless they know. Information a person on a phone
 * must be able to reach ("Sorting orders only the rows shown on this page")
 * goes behind this button instead.
 *
 * The text is `text` or the default slot. The button is named by `label`
 * ("More information" by default, from the app's catalogue); the panel is a
 * `TPopover` (a non-modal dialog named by the same words), so Escape closes it
 * and focus goes back to the button, and a press outside or Tab out closes it.
 */
import TIconButton from './TIconButton.vue'
import TPopover from '../overlay/TPopover.vue'
import { useControlsText } from './controls-text'
import type { FloatingAlign, FloatingSide } from '../../headless/place-beside'

withDefaults(
  defineProps<{
    text?: string
    /** Accessible name of the button and the panel; "More information" by default. */
    label?: string
    icon?: string
    side?: FloatingSide
    align?: FloatingAlign
    /** Width of the panel (number = px); by default it fits the text, up to 280px. */
    width?: number | string
    size?: 'xs' | 'sm'
  }>(),
  { text: '', label: '', icon: 'lucide:info', side: 'top', align: 'center', width: undefined, size: 'xs' },
)

defineSlots<{ default?: () => unknown }>()

const words = useControlsText()
</script>

<template>
  <TPopover
    class="t-info-tip"
    :label="label || words.moreInfo"
    :side="side"
    :align="align"
    :width="width"
    data-info-tip
  >
    <template #trigger="{ toggle, attrs }">
      <TIconButton
        v-bind="attrs"
        class="t-info-tip__button"
        :icon="icon"
        :label="label || words.moreInfo"
        :size="size"
        data-info-tip-button
        @click="toggle"
      />
    </template>
    <slot>{{ text }}</slot>
  </TPopover>
</template>

<!-- The panel is TPopover's, not this component's root: its rules are global, under the component's own class, specific enough to win over the popover's own padding. -->
<style>
.t-popover-root > .t-popover.t-info-tip {
  max-width: min(280px, calc(100vw - 16px));
  padding: 8px 10px;
  font-size: 12px;
  line-height: 1.45;
  color: var(--tnzi-ai-text-secondary);
}
</style>

<style scoped>
.t-info-tip__button {
  color: var(--tnzi-ai-text-tertiary);
}
</style>
