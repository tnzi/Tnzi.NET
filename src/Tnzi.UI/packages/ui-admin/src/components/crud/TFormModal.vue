<template>
  <NModal
    :show="state.visible.value"
    :mask-closable="false"
    preset="card"
    :title="title"
    :style="{ width: width + 'px' }"
    @update:show="onUpdateShow"
  >
    <slot :formData="state.formData.value" :mode="state.mode.value" />
    <template #footer>
      <slot name="footer">
        <div class="t-form-modal__footer">
          <NButton class="t-form-modal__cancel" @click="onCancel">
            {{ t('admin.common.cancel') }}
          </NButton>
          <NButton
            v-if="state.mode.value !== 'view'"
            type="primary"
            class="t-form-modal__confirm"
            @click="onConfirm"
          >
            {{ t('admin.common.confirm') }}
          </NButton>
        </div>
      </slot>
    </template>
  </NModal>
</template>

<script setup lang="ts">
import { NModal, NButton } from 'naive-ui'
import { useFormModal } from '../../headless/useFormModal'

type FormState = ReturnType<typeof useFormModal<unknown>>

interface Props {
  state: FormState
  title: string
  width?: number
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  width: 560,
  translate: undefined,
})

const emit = defineEmits<{
  submit: []
}>()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

function onUpdateShow(value: boolean): void {
  if (!value) props.state.close()
}

function onCancel(): void {
  props.state.close()
}

function onConfirm(): void {
  emit('submit')
}
</script>

<style scoped>
.t-form-modal__footer {
  display: flex;
  justify-content: flex-end;
  gap: var(--tnzi-spacing-sm, 8px);
}
</style>
