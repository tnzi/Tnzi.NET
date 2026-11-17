<script setup lang="ts">
/**
 * TDropdownMenu - a trigger and the small menu it opens, on `TPopoverMenu`.
 *
 * The entries are `TMenuItem`s, with `TMenuSeparator`s between groups:
 *
 * ```vue
 * <TDropdownMenu>
 *   <template #trigger="{ toggle, attrs }">
 *     <TIconButton icon="lucide:ellipsis" label="More" v-bind="attrs" @click="toggle" />
 *   </template>
 *   <template #default="{ close }">
 *     <TMenuItem icon="lucide:pencil" @click="rename(); close()">Rename</TMenuItem>
 *     <TMenuSeparator />
 *     <TMenuItem icon="lucide:trash-2" danger @click="remove(); close()">Delete</TMenuItem>
 *   </template>
 * </TDropdownMenu>
 * ```
 *
 * Keyboard: opening it moves focus to the first entry (not for a finger: a tap
 * has nothing to mark as "you are here"); Up and Down move between the enabled
 * entries and wrap, Home and End go to the ends, Escape closes it and gives
 * focus back to the trigger, Tab closes it and moves on. Escape is taken here
 * and goes no further, so a dialog around the menu stays open.
 *
 * `trigger` slot props: `open`, `toggle()` and `attrs` (`aria-haspopup`,
 * `aria-expanded`), to bind on the trigger button. Default slot: `close()`.
 */
import { computed, nextTick, ref, watch } from 'vue'
import TPopoverMenu from './TPopoverMenu.vue'
import { afterMenuPlaced, createMenuKeydown, isTouchScreen, menuItems } from './menu-keys'

const props = withDefaults(
  defineProps<{
    align?: 'left' | 'right'
    placement?: 'top' | 'bottom'
    minWidth?: number
    maxWidth?: number
    /** Header line at the top of the menu. */
    label?: string
  }>(),
  { align: 'right', placement: 'bottom', minWidth: 200, maxWidth: 320, label: '' },
)

const emit = defineEmits<{ 'update:open': [open: boolean] }>()

const open = ref(false)
const root = ref<HTMLElement | null>(null)

function focusTrigger(): void {
  void nextTick(() => {
    const trigger = root.value?.querySelector<HTMLElement>('[data-menu-trigger] button, [data-menu-trigger] [tabindex]')
    trigger?.focus()
  })
}

function close(returnFocus = true): void {
  if (!open.value) return
  open.value = false
  if (returnFocus) focusTrigger()
}

function toggle(): void {
  open.value = !open.value
}

const onKeydown = createMenuKeydown({ root: () => root.value, isOpen: () => open.value, close })

watch(open, async (isOpen) => {
  emit('update:open', isOpen)
  if (!isOpen) return
  await nextTick()
  if (isTouchScreen()) return
  afterMenuPlaced(() => {
    if (open.value) menuItems(root.value)[0]?.focus()
  })
})

const triggerAttrs = computed(() => ({ 'aria-haspopup': 'menu' as const, 'aria-expanded': open.value }))

defineExpose({ close: () => close(false), open: () => (open.value = true) })
</script>

<template>
  <div ref="root" class="t-dropdown-menu" @keydown="onKeydown">
    <div data-menu-trigger class="t-dropdown-menu__trigger">
      <slot name="trigger" :open="open" :toggle="toggle" :attrs="triggerAttrs" />
    </div>
    <TPopoverMenu v-model="open" :align="props.align" :placement="props.placement" :min-width="props.minWidth" :max-width="props.maxWidth" :label="props.label">
      <slot :close="() => close()" />
    </TPopoverMenu>
  </div>
</template>

<style scoped>
/* Both boxes may shrink below their content and never grow past their
   container: a dropdown placed as a shrinking flex item (a branch picker in a
   toolbar) lets its trigger ellipsise instead of running over the controls
   beside it. Unconstrained, both still size to the trigger as before. */
.t-dropdown-menu {
  position: relative;
  display: inline-flex;
  min-width: 0;
  max-width: 100%;
}
.t-dropdown-menu__trigger {
  display: inline-flex;
  min-width: 0;
  max-width: 100%;
}
</style>
