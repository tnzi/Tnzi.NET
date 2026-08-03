<template>
  <Teleport to="body">
    <Transition name="t-confirm">
      <div
        v-if="show"
        ref="rootRef"
        class="t-confirm-wrapper"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="titleId"
      >
        <div class="t-confirm__backdrop" @click="handleCancel" />
        <div class="t-confirm">
          <div :id="titleId" class="t-confirm__title">{{ title }}</div>
          <div v-if="content || $slots.default" class="t-confirm__content">
            <slot>{{ content }}</slot>
          </div>
          <div class="t-confirm__actions">
            <button type="button" class="t-confirm__cancel" @click="handleCancel">
              {{ cancelText }}
            </button>
            <button
              ref="primaryBtnRef"
              type="button"
              class="t-confirm__ok"
              :class="{ 't-confirm__ok--danger': danger }"
              @click="handleConfirm"
            >
              {{ confirmText }}
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useFocusTrap } from '../../headless/feedback/useFocusTrap'

interface Props {
  show: boolean
  title: string
  content?: string
  confirmText?: string
  cancelText?: string
  danger?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  content: '',
  confirmText: 'Confirm',
  cancelText: 'Cancel',
  danger: false,
})

const emit = defineEmits<{
  'update:show': [value: boolean]
  confirm: []
  cancel: []
}>()

const rootRef = ref<HTMLElement | null>(null)
const primaryBtnRef = ref<HTMLButtonElement | null>(null)

const titleId = `t-confirm-title-${Math.random().toString(36).slice(2, 10)}`

function handleCancel() {
  emit('update:show', false)
  emit('cancel')
}

function handleConfirm() {
  emit('confirm')
  emit('update:show', false)
}

useFocusTrap(rootRef, () => props.show, {
  onEscape: handleCancel,
  initialFocus: () => primaryBtnRef.value,
})
</script>

<style scoped>
.t-confirm-wrapper {
  position: fixed;
  inset: 0;
  z-index: 2000;
  display: flex;
  align-items: center;
  justify-content: center;
}
.t-confirm__backdrop {
  position: absolute;
  inset: 0;
  background-color: rgba(0, 0, 0, 0.5);
}
.t-confirm {
  position: relative;
  background-color: var(--tnzi-base-bg, #fff);
  border-radius: 8px;
  padding: 24px;
  min-width: 320px;
  max-width: 480px;
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.2);
}
.t-confirm__title {
  font-size: 18px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  margin-bottom: 12px;
}
.t-confirm__content {
  font-size: 14px;
  color: var(--tnzi-base-text-muted);
  line-height: 1.5;
  margin-bottom: 20px;
}
.t-confirm__actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}
.t-confirm__cancel,
.t-confirm__ok {
  padding: 8px 16px;
  border-radius: 6px;
  border: 1px solid var(--tnzi-border);
  background-color: transparent;
  cursor: pointer;
  font-size: 14px;
}
.t-confirm__ok {
  background-color: var(--tnzi-primary-500);
  color: #fff;
  border-color: var(--tnzi-primary-500);
}
.t-confirm__ok--danger {
  background-color: var(--tnzi-error-500);
  border-color: var(--tnzi-error-500);
}
.t-confirm-enter-active,
.t-confirm-leave-active {
  transition: opacity 0.2s ease;
}
.t-confirm-enter-from,
.t-confirm-leave-to {
  opacity: 0;
}
</style>
