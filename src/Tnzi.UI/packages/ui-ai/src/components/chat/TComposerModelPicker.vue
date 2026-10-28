<script setup lang="ts">
/**
 * @experimental
 * TComposerModelPicker - a compact model switch in the composer's toolbar.
 *
 * Opens upward (the composer sits at the bottom of the screen) and lists the
 * models the user may pick, with a check on the current one. `TChatApp` shows
 * it when it is given more than one model; with a single model there is
 * nothing to choose and the toolbar stays as it was.
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import TPopoverMenu from '../overlay/TPopoverMenu.vue'
import { useAiI18n } from '../../i18n/index'

export interface ComposerModelOption {
  id: string
  /** Shown name; the id when empty. */
  displayName?: string | null
  /** One line under the name in the list. */
  description?: string | null
}

const props = withDefaults(
  defineProps<{
    models: ReadonlyArray<ComposerModelOption>
    /** The selected model id; the first model when null or unknown. */
    modelValue?: string | null
    disabled?: boolean
  }>(),
  {
    modelValue: null,
    disabled: false,
  },
)

const emit = defineEmits<{
  'update:modelValue': [modelId: string]
}>()

const t = useAiI18n()
const open = ref(false)

const nameOf = (m: ComposerModelOption) => m.displayName || m.id

const current = computed(
  () => props.models.find((m) => m.id === props.modelValue) ?? props.models[0] ?? null,
)

function toggle(): void {
  if (props.disabled) return
  open.value = !open.value
}

function choose(id: string): void {
  open.value = false
  if (id !== current.value?.id) emit('update:modelValue', id)
}
</script>

<template>
  <div class="t-composer-model-picker">
    <button
      type="button"
      class="t-composer-model-picker__trigger"
      :disabled="disabled"
      :aria-label="t.composer.model"
      aria-haspopup="menu"
      :aria-expanded="open"
      @click="toggle"
    >
      <span class="t-composer-model-picker__name">{{ current ? nameOf(current) : '' }}</span>
      <Icon icon="lucide:chevron-down" class="t-composer-model-picker__chevron" />
    </button>
    <TPopoverMenu v-model="open" placement="top" align="right" :min-width="220" :max-width="300" :label="t.composer.model">
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
  max-width: 180px;
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
</style>
