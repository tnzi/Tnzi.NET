<template>
  <NBadge :value="unreadCount" :show="unreadCount > 0" :max="99" type="error" :offset="[-2, 2]">
    <button class="t-chat-launcher" type="button" :title="t('launcher.title')" @click="emit('open')">
      <Icon icon="nimbus:chat-dots" :width="20" :height="20" />
    </button>
  </NBadge>
</template>

<script setup lang="ts">
import { NBadge } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { translatePageKey } from '../../pages/_shared/translate'

defineProps<{ unreadCount: number }>()
const emit = defineEmits<{ open: [] }>()
const t = (k: string) => translatePageKey('chat', k)
</script>

<style scoped>
.t-chat-launcher {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-base-text-muted, #666);
  cursor: pointer;
  transition: background 0.15s;
}
.t-chat-launcher:hover {
  background: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.06);
}
</style>
