<script setup lang="ts">
/**
 * @experimental
 * TComposerModelPicker - a compact model and thinking effort switch in the
 * composer's toolbar.
 *
 * The trigger reads "model · effort" ("Opus 5.5 · High"), or the model alone
 * when it offers no effort choice. It opens upward (the composer sits at the
 * bottom of the screen): the models the user may pick, with a check on the
 * current one, then a "Thinking effort" row, a segmented choice of the efforts
 * the current model offers (its default marked with a small dot) and one line
 * saying what the chosen effort does. Picking a model closes the menu; picking
 * an effort does not, so the two can be set in one go. The effort row fills
 * the menu's width, each option growing from its own width (Kimi's off / on
 * are two halves); on a phone the menu spans the screen and each option is at
 * least 44px tall.
 *
 * Efforts are runtime-native values. Claude Code's low / medium / high / xhigh /
 * max are named from the locale (`composer.effortNames`); any other value is
 * shown as it is. `TChatApp` shows the picker when it is given more than one
 * model, or one model that offers efforts.
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import TPopoverMenu from '../overlay/TPopoverMenu.vue'
import TSegmented from '../layout/TSegmented.vue'
import type { SegmentedOption } from '../layout/page-toolbar'
import { useAiI18n } from '../../i18n/index'
import { useViewportBelow } from '../../headless/useViewportBelow'

export interface ComposerModelOption {
  id: string
  /** Shown name; the id when empty. */
  displayName?: string | null
  /** One line under the name in the list. */
  description?: string | null
  /** The reasoning efforts this model offers (runtime-native values, in display order); none: no effort row. */
  efforts?: ReadonlyArray<string> | null
  /** The model's default effort, marked in the effort row. */
  defaultEffort?: string | null
}

const props = withDefaults(
  defineProps<{
    models: ReadonlyArray<ComposerModelOption>
    /** The selected model id; the first model when null or unknown. */
    modelValue?: string | null
    /** The selected effort (`v-model:effort`); none chosen when null or not one the current model offers. */
    effort?: string | null
    disabled?: boolean
  }>(),
  {
    modelValue: null,
    effort: null,
    disabled: false,
  },
)

const emit = defineEmits<{
  'update:modelValue': [modelId: string]
  'update:effort': [effort: string]
}>()

const t = useAiI18n()
const open = ref(false)
/** The shell's phone layout: the menu spans the screen and the effort row fills it with touch-sized options. */
const phone = useViewportBelow(768)

/** Wide enough for five efforts beside each other; on a phone, the screen less its margins. */
const menuMinWidth = computed(() => {
  if (!efforts.value.length) return 220
  return phone.value && typeof window !== 'undefined' ? Math.max(260, window.innerWidth - 16) : 340
})
const menuMaxWidth = computed(() => (efforts.value.length ? Math.max(menuMinWidth.value, 420) : 300))

const nameOf = (m: ComposerModelOption) => m.displayName || m.id

const current = computed(
  () => props.models.find((m) => m.id === props.modelValue) ?? props.models[0] ?? null,
)

const efforts = computed<string[]>(() => [...(current.value?.efforts ?? [])])

const chosenEffort = computed(() =>
  props.effort && efforts.value.includes(props.effort) ? props.effort : null,
)

const effortName = (value: string) => t.value.composer.effortNames[value] ?? value

const effortOptions = computed<SegmentedOption[]>(() =>
  efforts.value.map((value) => ({
    value,
    label: effortName(value),
    ...(value === current.value?.defaultEffort ? { dot: 'muted' as const, dotLabel: t.value.composer.effortDefault } : {}),
  })),
)

const effortHint = computed(() =>
  chosenEffort.value ? (t.value.composer.effortHints[chosenEffort.value] ?? null) : null,
)

const triggerText = computed(() => {
  if (!current.value) return ''
  const name = nameOf(current.value)
  return chosenEffort.value ? `${name} · ${effortName(chosenEffort.value)}` : name
})

function toggle(): void {
  if (props.disabled) return
  open.value = !open.value
}

function choose(id: string): void {
  open.value = false
  if (id !== current.value?.id) emit('update:modelValue', id)
}

function chooseEffort(value: string): void {
  if (value !== chosenEffort.value) emit('update:effort', value)
}

/** The effort row's own arrow keys move its choice; the menu around it must not take them for its item list. */
function keepEffortKeys(event: KeyboardEvent): void {
  if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) event.stopPropagation()
}
</script>

<template>
  <div class="t-composer-model-picker">
    <button
      type="button"
      class="t-composer-model-picker__trigger"
      :disabled="disabled"
      :aria-label="`${t.composer.model}: ${triggerText}`"
      aria-haspopup="menu"
      :aria-expanded="open"
      @click="toggle"
    >
      <span class="t-composer-model-picker__name">{{ triggerText }}</span>
      <Icon icon="lucide:chevron-down" class="t-composer-model-picker__chevron" />
    </button>
    <TPopoverMenu
      v-model="open"
      placement="top"
      align="right"
      :min-width="menuMinWidth"
      :max-width="menuMaxWidth"
      :label="models.length > 1 ? t.composer.model : undefined"
    >
      <template v-if="models.length > 1">
        <button
          v-for="m in models"
          :key="m.id"
          type="button"
          role="menuitemradio"
          class="t-popover-menu__item t-composer-model-picker__item"
          :aria-checked="m.id === current?.id"
          @click="choose(m.id)"
        >
          <span class="t-composer-model-picker__item-text">
            <span class="t-composer-model-picker__item-name">{{ nameOf(m) }}</span>
            <span v-if="m.description" class="t-composer-model-picker__item-desc">{{ m.description }}</span>
          </span>
          <Icon v-if="m.id === current?.id" icon="lucide:check" class="t-popover-menu__item-check" />
        </button>
      </template>
      <div
        v-if="efforts.length"
        class="t-composer-model-picker__effort"
        :class="{ 't-composer-model-picker__effort--below-models': models.length > 1 }"
        role="group"
        :aria-label="t.composer.effort"
        data-effort-row
        @keydown="keepEffortKeys"
      >
        <div class="t-composer-model-picker__effort-title">{{ t.composer.effort }}</div>
        <TSegmented
          grow
          :model-value="chosenEffort ?? ''"
          :options="effortOptions"
          :label="t.composer.effort"
          @update:model-value="chooseEffort"
        />
        <p v-if="effortHint" class="t-composer-model-picker__effort-hint" aria-live="polite">{{ effortHint }}</p>
      </div>
    </TPopoverMenu>
  </div>
</template>

<style scoped>
.t-composer-model-picker {
  position: relative;
  display: inline-flex;
}
.t-composer-model-picker__trigger {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  height: 32px;
  max-width: 220px;
  padding: 0 8px 0 10px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font-family: inherit;
  font-size: 13px;
  cursor: pointer;
}
.t-composer-model-picker__trigger:hover:not(:disabled) {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-composer-model-picker__trigger:disabled {
  cursor: default;
  opacity: 0.6;
}
.t-composer-model-picker__name {
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.t-composer-model-picker__chevron {
  flex-shrink: 0;
  font-size: 14px;
}
/* A two-line row (name + description) needs more than the menu's 34px row
   (qualified so it outranks the menu's own item rule). */
.t-composer-model-picker .t-popover-menu__item.t-composer-model-picker__item {
  height: auto;
  min-height: 34px;
  padding-top: 6px;
  padding-bottom: 6px;
}
.t-composer-model-picker__item-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-composer-model-picker__item-name {
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.t-composer-model-picker__item-desc {
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
/* The effort row: a title, the segmented choice across the menu, one line of
   what the chosen effort does. */
.t-composer-model-picker__effort {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 6px 8px 8px;
}
.t-composer-model-picker__effort--below-models {
  margin-top: 4px;
  padding-top: 10px;
  border-top: 1px solid var(--tnzi-ai-border);
}
.t-composer-model-picker__effort-title {
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary);
}
.t-composer-model-picker__effort-hint {
  margin: 0;
  font-size: 12px;
  line-height: 1.4;
  color: var(--tnzi-ai-text-secondary);
}
/* On a phone the options are touch targets: the segmented control takes its
   height from the toolbar control size, so 48px here gives 44px options. */
@media (max-width: 767px) {
  .t-composer-model-picker__effort {
    --tnzi-ai-toolbar-control-size: 48px;
  }
}
</style>
