<script setup lang="ts">
/**
 * `TLangSwitch` — minimal language switcher backed by an NPopselect dropdown.
 *
 * Stays presentational: emits the new locale, lets the consumer wire it to
 * `vue-i18n` / their own store. Default options cover `en` and `zh-CN`,
 * matching `@tnzi/ui-admin/locales`. Pass `options` to add more.
 */
import { computed } from 'vue'
import { NPopselect } from 'naive-ui'
import TButtonIcon from '../display/TButtonIcon.vue'

export interface LangOption {
  label: string
  value: string
}

interface Props {
  value?: string
  defaultValue?: string
  options?: LangOption[]
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  value: undefined,
  defaultValue: 'en',
  options: () => [
    { label: 'English', value: 'en' },
    { label: '简体中文', value: 'zh-CN' },
  ],
  translate: undefined,
})

const emit = defineEmits<{
  change: [value: string]
  'update:value': [value: string]
}>()

const current = computed(() => props.value ?? props.defaultValue)

function onSelect(v: string): void {
  emit('change', v)
  emit('update:value', v)
}

const tooltip = computed(() =>
  props.translate ? props.translate('admin.lang.switch') : 'Language',
)

// Normalize options for NPopselect's NSelect-shaped contract (value + label).
const popselectOptions = computed(() =>
  props.options.map((o) => ({ ...o })),
)
</script>

<template>
  <NPopselect
    :value="current"
    :options="popselectOptions"
    trigger="click"
    @update:value="onSelect"
  >
    <TButtonIcon icon="mdi:translate" :tooltip="tooltip" class="t-lang-switch" />
  </NPopselect>
</template>
