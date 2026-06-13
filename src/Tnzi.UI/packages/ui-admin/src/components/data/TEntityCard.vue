<template>
  <!--
    TEntityCard — the standard chrome for an entity card inside a TCardPage grid.

    Encapsulates three concerns that every card page repeated (and that are easy
    to get subtly wrong):
      1. Whole-card click → "open" the entity (route / detail drawer / edit).
         Opt in with `clickable`; emits `click`. Keyboard accessible (Enter /
         Space) with role="button" + tabindex so it's not mouse-only.
      2. A right-aligned actions footer (the `#actions` slot) whose clicks are
         stopped from bubbling to the card — so action buttons / popconfirms keep
         working without triggering the card's own navigation. (The `@click.stop`
         sits on the footer, an ancestor of the buttons, so the buttons still
         receive the event first; only the bubble up to the card is cut.)
      3. Consistent card chrome: rounded border, a static resting shadow, and a
         hover lift *only* when clickable — so a non-clickable card never offers a
         misleading "I'm tappable" affordance.

    The card body is the default slot; the page owns its bespoke content. Drop
    this in place of a raw `<NCard size="small">` and move the action buttons
    into `#actions`.
  -->
  <NCard
    size="small"
    class="t-entity-card"
    :class="{ 't-entity-card--clickable': clickable }"
    :bordered="bordered"
    :role="clickable ? 'button' : undefined"
    :tabindex="clickable ? 0 : undefined"
    @click="onActivate"
    @keydown="onKeydown"
  >
    <slot />
    <div v-if="$slots.actions" class="t-entity-card__actions" @click.stop>
      <slot name="actions" />
    </div>
  </NCard>
</template>

<script setup lang="ts">
import { NCard } from 'naive-ui'

interface Props {
  /** Make the whole card a button: pointer cursor, hover lift, click + keyboard
   *  activation emit `click`. Leave false for cards with no "open" surface. */
  clickable?: boolean
  /** Forwarded to NCard. Cards keep their border by default. */
  bordered?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  clickable: false,
  bordered: true,
})

const emit = defineEmits<{ click: [event: Event] }>()

defineSlots<{
  default?: () => unknown
  actions?: () => unknown
}>()

function onActivate(event: MouseEvent): void {
  if (!props.clickable) return
  emit('click', event)
}

function onKeydown(event: KeyboardEvent): void {
  if (!props.clickable) return
  if (event.key === 'Enter' || event.key === ' ') {
    // Space would otherwise scroll the page; Enter/Space both "activate".
    event.preventDefault()
    emit('click', event)
  }
}
</script>

<style scoped>
.t-entity-card {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  transition: box-shadow 0.15s ease, transform 0.15s ease;
}
.t-entity-card--clickable {
  cursor: pointer;
}
.t-entity-card--clickable:hover {
  box-shadow: 0 4px 12px rgb(0 0 0 / 0.08);
  transform: translateY(-1px);
}
.t-entity-card--clickable:focus-visible {
  outline: 2px solid var(--tnzi-primary, #6d5ce7);
  outline-offset: 2px;
}
/* Right-aligned action footer. The `@click.stop` lives on this element so the
   buttons inside still receive the event (bubble reaches them first) while the
   card's own click handler is shielded. */
.t-entity-card__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
}
</style>
