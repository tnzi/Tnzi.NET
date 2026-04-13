<script setup lang="ts">
/**
 * DialogEnvironment — per-dialog lifecycle wrapper.
 *
 * Manages show/hide state, delegates to Dialog for rendering.
 * Handles async close guards (onPositiveClick/onNegativeClick/onClose returning false).
 */
import { ref, onMounted } from 'vue';
import type { DialogEntry } from '../../composables/dialog-state';
import Dialog from './Dialog.vue';

const props = defineProps<{
  dialog: DialogEntry;
}>();

const emit = defineEmits<{
  register: [inst: { hide: () => void }];
  afterLeave: [];
}>();

const show = ref(true);

function hide() {
  show.value = false;
}

async function handlePositiveClick(e: MouseEvent) {
  const { onPositiveClick } = props.dialog;
  if (onPositiveClick) {
    const result = await Promise.resolve(onPositiveClick(e));
    if (result === false) return;
  }
  hide();
}

async function handleNegativeClick(e: MouseEvent) {
  const { onNegativeClick } = props.dialog;
  if (onNegativeClick) {
    const result = await Promise.resolve(onNegativeClick(e));
    if (result === false) return;
  }
  hide();
}

async function handleClose() {
  const { onClose } = props.dialog;
  if (onClose) {
    const result = await Promise.resolve(onClose());
    if (result === false) return;
  }
  hide();
}

function handleMaskClick() {
  props.dialog.onMaskClick?.();
  if (props.dialog.maskClosable !== false) {
    handleClose();
  }
}

function handleEsc() {
  props.dialog.onEsc?.();
  if (props.dialog.closeOnEsc !== false) {
    handleClose();
  }
}

function handleAfterLeave() {
  props.dialog.onAfterLeave?.();
  emit('afterLeave');
}

onMounted(() => {
  emit('register', { hide });
});
</script>

<template>
  <Dialog
    :show="show"
    :type="dialog.type ?? 'default'"
    :title="dialog.title"
    :content="dialog.content"
    :positive-text="dialog.positiveText"
    :negative-text="dialog.negativeText"
    :closable="dialog.closable !== false"
    :show-icon="dialog.showIcon !== false"
    :loading="dialog.loading"
    :prompt="dialog.prompt"
    :prompt-placeholder="dialog.promptPlaceholder"
    :input-value="dialog._inputValue"
    @input-change="(v: string) => dialog._onInputChange?.(v)"
    @positive-click="handlePositiveClick"
    @negative-click="handleNegativeClick"
    @close="handleClose"
    @mask-click="handleMaskClick"
    @esc="handleEsc"
    @after-leave="handleAfterLeave"
  />
</template>
