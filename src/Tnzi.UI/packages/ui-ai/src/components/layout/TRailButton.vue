<script setup lang="ts">
/**
 * @experimental
 * TRailButton - one icon button of a collapsed icon rail.
 *
 * At 56px the rail has no room for labels, so every button carries its label
 * twice: as its accessible name (`aria-label`) and as a tooltip beside it
 * (`vTooltip`: 350 ms after the pointer rests, at once on keyboard focus, never
 * for touch). This is the button `TChatRail` draws for its nav items and the
 * one a product puts in the rail's slots for its own entries, so they look and
 * behave like the built-in ones.
 *
 * A `badge` with a `badgeTone` (a nav item's count that asks for attention, see
 * `NavItem.badgeTone`) sits on the icon's top right corner as a small filled
 * pill and is read after the label; without a tone no badge is drawn, as the
 * rail has no room for the quiet grey counts.
 *
 * Attributes fall through to the `<button>` (`aria-current`, `aria-expanded`,
 * `data-*`, a class of the caller's own).
 */
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import type { FloatingSide } from '../../headless/place-beside'
import type { IconMotion } from '../../headless/icon-motion'
import type { NavBadgeTone } from './TSidebarNav.vue'
import { vIconMotion } from '../../headless/icon-motion'
import { vTooltip } from '../../headless/tooltip'

const props = withDefaults(
  defineProps<{
    /** The button's accessible name and its tooltip. */
    label: string
    /** Iconify name. Replace the icon through the default slot. */
    icon?: string
    /** Drawn as the current destination (the neutral selected fill). */
    active?: boolean
    /** One-shot icon motion on hover (`vIconMotion`). */
    iconMotion?: IconMotion
    /** `false` drops the tooltip (a button whose own popover names it); a string shows that instead of `label`. */
    tooltip?: boolean | string
    /** Side the tooltip opens on. Default `right`, beside a left-hand rail. */
    tooltipSide?: FloatingSide
    /** A count on the icon's corner; drawn only with `badgeTone`. */
    badge?: string | number
    /** The badge's tone (see `NavItem.badgeTone`). */
    badgeTone?: NavBadgeTone
  }>(),
  {
    icon: undefined,
    active: false,
    iconMotion: undefined,
    tooltip: true,
    tooltipSide: 'right',
    badge: undefined,
    badgeTone: undefined,
  },
)

defineEmits<{ click: [event: MouseEvent] }>()

const showsBadge = computed(() => props.badge != null && props.badge !== '' && !!props.badgeTone)
const name = computed(() => (showsBadge.value ? `${props.label}, ${props.badge}` : props.label))

const tooltipValue = computed(() =>
  props.tooltip === false
    ? null
    : { text: typeof props.tooltip === 'string' ? props.tooltip : props.label, side: props.tooltipSide },
)
</script>

<template>
  <button
    v-icon-motion
    v-tooltip="tooltipValue"
    type="button"
    class="t-rail-button"
    :class="{ 'is-active': active }"
    :aria-label="name"
    @click="$emit('click', $event)"
  >
    <slot>
      <Icon v-if="icon" :icon="icon" :data-icon-motion="iconMotion" />
    </slot>
    <span
      v-if="showsBadge"
      class="t-rail-button__badge"
      :class="`t-rail-button__badge--${badgeTone}`"
      :data-badge-tone="badgeTone"
      aria-hidden="true"
    >{{ badge }}</span>
  </button>
</template>

<style scoped>
.t-rail-button {
  position: relative;
  width: 40px;
  height: 40px;
  border: none;
  background: transparent;
  border-radius: 10px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 19px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  flex-shrink: 0;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-rail-button:hover {
  background: var(--tnzi-ai-sidebar-hover);
  color: var(--tnzi-ai-text);
}
/* The same neutral selected wash as the expanded nav; the icon takes the
   row's text colour, never the brand's. */
.t-rail-button.is-active {
  background: var(--tnzi-ai-sidebar-selected);
  color: var(--tnzi-ai-text);
}
/* On the icon's top right corner, ringed in the rail's colour so it stands
   off the glyph. */
.t-rail-button__badge {
  position: absolute;
  top: 3px;
  right: 2px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  box-sizing: border-box;
  border-radius: 8px;
  box-shadow: 0 0 0 2px var(--tnzi-ai-sidebar-bg);
  font-size: 10px;
  font-weight: 600;
  line-height: 1;
  font-variant-numeric: tabular-nums;
  pointer-events: none;
}
.t-rail-button__badge--danger {
  background: var(--tnzi-ai-badge-danger);
  color: var(--tnzi-ai-on-badge-danger);
}
.t-rail-button__badge--warning {
  background: var(--tnzi-ai-badge-warning);
  color: var(--tnzi-ai-on-badge-warning);
}
.t-rail-button:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 1px;
}
</style>
