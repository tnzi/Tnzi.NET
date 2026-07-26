<script setup lang="ts">
/**
 * `TLangSwitch` - minimal language switcher backed by an NPopselect dropdown.
 *
 * Stays presentational: emits the new locale, lets the consumer wire it to
 * `vue-i18n` / their own store. Default options cover `en` and `zh-CN`,
 * matching `@tnzi/ui-admin/locales`. Pass `options` to add more.
 *
 * The trigger is wrapped in a real `<span>` element: `TButtonIcon` renders
 * `NTooltip > NButton`, and NTooltip (a VBinder popover) has a non-element
 * fragment root. NPopselect attaches its follow-target via a runtime
 * directive on the trigger slot's root - on a fragment root the directive
 * cannot bind ("Runtime directive used on component with non-element root
 * node"), the follower never gets a target rect, and the dropdown rendered
 * at the viewport origin (0,0). The span gives the directive a measurable
 * single-element root. `inheritAttrs: false` + `v-bind="$attrs"` also land
 * consumer classes (e.g. `text-20px`) on the trigger instead of leaking
 * them onto the teleported popover panel.
 */
import { computed } from 'vue'
import { NPopselect } from 'naive-ui'
import TButtonIcon from '../display/TButtonIcon.vue'

defineOptions({ inheritAttrs: false })

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
    <!-- Single-element trigger root - see the component doc comment. -->
    <span class="t-lang-switch" v-bind="$attrs">
      <TButtonIcon icon="mdi:translate" :tooltip="tooltip" />
    </span>
  </NPopselect>
</template>

<style scoped>
.t-lang-switch {
  display: inline-flex;
  align-items: center;
}
</style>
