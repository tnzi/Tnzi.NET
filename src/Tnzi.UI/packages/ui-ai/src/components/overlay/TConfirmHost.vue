<script setup lang="ts">
/**
 * TConfirmHost - renders the confirmation `useConfirm()` asks for (see
 * `confirm.ts`) as a small `alertdialog` `TDialog`. Mounted once per app:
 * `TChatApp` mounts it; an app with its own shell puts it beside its root. A
 * second host for the same confirmer draws nothing.
 *
 * Keyboard and focus are `TDialog`'s: focus moves into the dialog on open and
 * back to what had it on close, Tab stays inside, Escape dismisses (the answer
 * is false). While `action` runs the dialog is busy: Escape, the backdrop and
 * both buttons do nothing, and a failure is shown inside it (`TNotice`), with
 * the dialog still open.
 */
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import TDialog from './TDialog.vue'
import TButton from '../controls/TButton.vue'
import TNotice from '../controls/TNotice.vue'
import { confirmer as defaultConfirmer, type Confirmer } from './confirm'
import { useControlsText } from '../controls/controls-text'

const props = defineProps<{
  /** The confirmer to show; the package-wide one (`useConfirm()`'s) by default. */
  confirmer?: Confirmer
}>()

const source = computed(() => props.confirmer ?? defaultConfirmer)
const self = Symbol('confirm-host')
const text = useControlsText()

const request = computed(() => (source.value.owner.value === self ? source.value.current.value : null))
const checked = ref(false)
const busy = ref(false)
const failure = ref<string | null>(null)

watch(request, () => {
  checked.value = false
  busy.value = false
  failure.value = null
})

function setOpen(open: boolean): void {
  if (!open && !busy.value) request.value?.resolve(false)
}

function describe(error: unknown): string {
  const describeFn = request.value?.describe
  if (describeFn) return describeFn(error)
  return error instanceof Error ? error.message : String(error)
}

async function confirm(): Promise<void> {
  const current = request.value
  if (!current || busy.value) return
  if (!current.action) {
    current.resolve(true)
    return
  }
  busy.value = true
  failure.value = null
  try {
    await current.action(checked.value)
    busy.value = false
    current.resolve(true)
  } catch (error) {
    busy.value = false
    failure.value = describe(error)
  }
}

onMounted(() => source.value.claim(self))
onBeforeUnmount(() => source.value.release(self))
</script>

<template>
  <TDialog
    role="alertdialog"
    :model-value="request !== null"
    :title="request?.title ?? ''"
    size="sm"
    :busy="busy"
    data-confirm-dialog
    @update:model-value="setOpen"
  >
    <p class="t-confirm__message">{{ request?.message }}</p>
    <label v-if="request?.checkbox" class="t-check t-confirm__check">
      <input v-model="checked" type="checkbox" :disabled="busy" data-confirm-check />
      <span>{{ request.checkbox }}</span>
    </label>
    <TNotice v-if="failure" tone="error" class="t-confirm__failure">{{ failure }}</TNotice>
    <template #footer>
      <TButton :disabled="busy" data-confirm-cancel @click="setOpen(false)">{{ request?.cancelText || text.cancel }}</TButton>
      <TButton :variant="request?.danger ? 'danger' : 'primary'" :loading="busy" data-confirm-ok @click="confirm">
        {{ request?.confirmText || text.confirm }}
      </TButton>
    </template>
  </TDialog>
</template>

<style scoped>
.t-confirm__message {
  margin: 0;
  color: var(--tnzi-ai-text-secondary);
  font-size: 14px;
  line-height: 1.6;
  white-space: pre-line;
  overflow-wrap: anywhere;
}
.t-confirm__check,
.t-confirm__failure {
  margin-top: 16px;
}
</style>
