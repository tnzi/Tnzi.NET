<template>
  <Teleport to="body">
    <Transition name="t-drawer">
      <div
        v-if="show"
        ref="rootRef"
        class="t-drawer-wrapper"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="title ? titleId : undefined"
      >
        <div class="t-drawer__backdrop" @click="handleClose" />
        <div class="t-drawer__panel" :class="`t-drawer__panel--${placement}`" :style="panelStyle">
          <div v-if="title || $slots.header" class="t-drawer__header">
            <slot name="header">
              <div :id="titleId" class="t-drawer__title">{{ title }}</div>
            </slot>
            <button
              ref="closeBtnRef"
              type="button"
              class="t-drawer__close"
              aria-label="Close"
              @click="handleClose"
            >
              &times;
            </button>
          </div>
          <div class="t-drawer__body">
            <slot />
          </div>
          <div v-if="$slots.footer" class="t-drawer__footer">
            <slot name="footer" />
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, ref, type CSSProperties } from 'vue'
import { useFocusTrap } from '../../composables/feedback/useFocusTrap'

type Placement = 'left' | 'right' | 'top' | 'bottom'

interface Props {
  show: boolean
  title?: string
  placement?: Placement
  width?: string
  height?: string
}

const props = withDefaults(defineProps<Props>(), {
  title: '',
  placement: 'right',
  width: '420px',
  height: '320px',
})

const emit = defineEmits<{
  'update:show': [value: boolean]
  close: []
}>()

const rootRef = ref<HTMLElement | null>(null)
const closeBtnRef = ref<HTMLButtonElement | null>(null)

const titleId = `t-drawer-title-${Math.random().toString(36).slice(2, 10)}`

function handleClose() {
  emit('update:show', false)
  emit('close')
}

const panelStyle = computed<CSSProperties>(() => {
  if (props.placement === 'left' || props.placement === 'right') {
    return { width: props.width }
  }
  return { height: props.height }
})

useFocusTrap(rootRef, () => props.show, {
  onEscape: handleClose,
  initialFocus: () => closeBtnRef.value,
})
</script>

<style scoped>
.t-drawer-wrapper {
  position: fixed;
  inset: 0;
  z-index: 2000;
}
.t-drawer__backdrop {
  position: absolute;
  inset: 0;
  background-color: rgba(0, 0, 0, 0.5);
}
.t-drawer__panel {
  position: absolute;
  background-color: var(--tnzi-base-bg, #fff);
  display: flex;
  flex-direction: column;
  box-shadow: 0 0 40px rgba(0, 0, 0, 0.2);
}
.t-drawer__panel--right {
  top: 0;
  right: 0;
  bottom: 0;
}
.t-drawer__panel--left {
  top: 0;
  left: 0;
  bottom: 0;
}
.t-drawer__panel--top {
  top: 0;
  left: 0;
  right: 0;
}
.t-drawer__panel--bottom {
  bottom: 0;
  left: 0;
  right: 0;
}
.t-drawer__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--tnzi-border);
}
.t-drawer__title {
  font-size: 16px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-drawer__close {
  background: transparent;
  border: none;
  font-size: 24px;
  line-height: 1;
  cursor: pointer;
  color: var(--tnzi-base-text-muted);
}
.t-drawer__body {
  flex: 1;
  overflow: auto;
  padding: 20px;
}
.t-drawer__footer {
  padding: 16px 20px;
  border-top: 1px solid var(--tnzi-border);
}
.t-drawer-enter-active,
.t-drawer-leave-active {
  transition: opacity 0.25s ease;
}
.t-drawer-enter-from,
.t-drawer-leave-to {
  opacity: 0;
}
</style>
