<script setup lang="ts">
/**
 * `TPinToggler` — pin / unpin button (used by mix-layout sub-sider to lock
 * the second-level drawer open). Icon orientation reflects pinned state.
 */
import { computed } from 'vue'
import TButtonIcon from '../display/TButtonIcon.vue'

interface Props {
  pinned?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  pinned: false,
  translate: undefined,
})

const emit = defineEmits<{
  toggle: [pinned: boolean]
  'update:pinned': [pinned: boolean]
}>()

function onClick(): void {
  emit('toggle', !props.pinned)
  emit('update:pinned', !props.pinned)
}

const icon = computed(() =>
  props.pinned ? 'mdi:pin' : 'mdi:pin-outline',
)
const tooltip = computed(() =>
  props.translate
    ? props.translate(props.pinned ? 'admin.pin.unpin' : 'admin.pin.pin')
    : props.pinned
      ? 'Unpin'
      : 'Pin',
)
</script>

<template>
  <TButtonIcon
    :icon="icon"
    :tooltip="tooltip"
    :class="['t-pin-toggler', { 't-pin-toggler--active': pinned }]"
    @click="onClick"
  />
</template>

<style scoped>
.t-pin-toggler--active {
  color: var(--tnzi-primary);
}
</style>
