<template>
  <NModal
    :show="show"
    :z-index="zIndex"
    :mask-closable="maskClosable"
    :auto-focus="false"
    @update:show="emit('update:show', $event)"
  >
    <div class="t-chat-dialog" :style="{ width }">
      <div class="t-chat-dialog__head">
        <span class="t-chat-dialog__title">{{ title }}</span>
        <button class="t-chat-dialog__close" :title="closeLabel" @click="close">
          <Icon icon="mdi:close" :width="18" />
        </button>
      </div>

      <div class="t-chat-dialog__body">
        <slot />
      </div>

      <div v-if="$slots.footer" class="t-chat-dialog__footer">
        <slot name="footer" />
      </div>
    </div>
  </NModal>
</template>

<script setup lang="ts">
import { NModal } from 'naive-ui'
import { Icon } from '@iconify/vue'

// Shared chrome for the chat pop-up dialogs (New Chat / Member Picker / Search
// History). Gives every dialog the same compact header + close button, padding,
// rounded surface and z-index so they read as one consistent family - instead of
// each rolling its own card. The body is a flex column so the consumer can give a
// FIXED-height scroll area inside (the dialog footprint then never jumps with
// content). z-index defaults above the chat NModal (~2000) so it always clears it.
withDefaults(
  defineProps<{
    show: boolean
    title: string
    /** Fixed dialog width (capped at 92vw). */
    width?: string
    zIndex?: number
    maskClosable?: boolean
    closeLabel?: string
  }>(),
  { width: '360px', zIndex: 3200, maskClosable: true, closeLabel: 'Close' },
)

const emit = defineEmits<{ 'update:show': [v: boolean] }>()

function close() { emit('update:show', false) }
</script>

<style scoped>
.t-chat-dialog {
  display: flex;
  flex-direction: column;
  max-width: 92vw;
  /* Cap the height so a short phone (or the keyboard eating half the screen)
     never pushes the header/footer off-view; the body scrolls instead. */
  max-height: 90dvh;
  background: var(--chat-surface, #fff);
  border-radius: var(--tnzi-admin-radius-lg, 12px);
  box-shadow: var(--tnzi-shadow-drawer, 0 12px 48px rgba(0, 0, 0, 0.22));
  overflow: hidden;
}

.t-chat-dialog__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 44px;
  padding: 0 8px 0 16px;
  flex-shrink: 0;
  border-bottom: 1px solid var(--chat-border, #eaeaea);
}

.t-chat-dialog__title {
  font-size: 14px;
  font-weight: 600;
  color: var(--chat-text, #1f1f1f);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-chat-dialog__close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  flex-shrink: 0;
  border: none;
  border-radius: 6px;
  background: transparent;
  cursor: pointer;
  color: var(--chat-text-2, #6f6f6f);
  transition: background 0.12s, color 0.12s;
}

.t-chat-dialog__close:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.06));
  color: var(--chat-text, #1f1f1f);
}

.t-chat-dialog__body {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-height: 0;
  /* Scroll long content within the height-capped dialog. */
  overflow-y: auto;
  padding: 12px 16px;
}

.t-chat-dialog__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  flex-shrink: 0;
  padding: 10px 16px;
  border-top: 1px solid var(--chat-border, #eaeaea);
}
</style>
