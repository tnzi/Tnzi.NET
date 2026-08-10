<template>
  <TModalShell
    :show="show"
    :title="title"
    :width="width"
    :content-max-height-vh="contentMaxHeightVh"
    :fullscreen="fullscreen"
    @update:show="onUpdateShow"
  >
    <slot :form-data="state.formData.value" :mode="state.mode.value" />
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
  </TModalShell>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NButton } from 'naive-ui'
import { TModalShell } from '@tnzi/ui'
import { useFormModal } from '../../headless/useFormModal'

type FormState = ReturnType<typeof useFormModal<unknown>>

interface Props {
  state: FormState
  title: string
  width?: number
  /**
   * Max viewport height (vh) the inner scroll area is allowed to occupy.
   * Forwarded to {@link TModalShell}. Default 65.
   */
  contentMaxHeightVh?: number
  /**
   * Force fullscreen layout regardless of viewport size. When unset, the modal
   * auto-switches to fullscreen on viewports narrower than the configured
   * `width` (or `<640px`, whichever is wider). Forwarded to {@link TModalShell}.
   */
  fullscreen?: boolean
  /**
   * Suppress this modal for the `view` action - the host renders the read-only
   * detail in a drawer (its `#detail` slot) instead. Create/edit still open the
   * modal. Off by default, so a page with no `#detail` slot keeps view-in-modal.
   */
  skipViewMode?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  width: 560,
  contentMaxHeightVh: 65,
  fullscreen: undefined,
  skipViewMode: false,
  translate: undefined,
})

// The form modal yields to a sibling view-drawer when the host opted in
// (`skipViewMode`) and the open-state is a read-only `view`. One open-state,
// chrome chosen by action: create/edit → this modal, view → the drawer.
const show = computed(
  () => props.state.visible.value && !(props.skipViewMode && props.state.mode.value === 'view'),
)

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
