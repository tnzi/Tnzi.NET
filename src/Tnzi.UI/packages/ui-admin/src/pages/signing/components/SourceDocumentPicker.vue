<template>
  <div class="sdp">
    <div v-if="fileName" class="sdp__current">
      <TSvgIcon icon="mdi:file-document-outline" :size="16" />
      <span class="sdp__name" :title="fileName">{{ fileName }}</span>
      <NButton v-if="!readonly" size="tiny" quaternary @click="clear">
        {{ translate('form.sourceFileClear') }}
      </NButton>
    </div>

    <div v-if="!readonly" class="sdp__actions">
      <NButton size="small" :loading="busy" @click="pick">
        <TSvgIcon icon="mdi:upload-outline" :size="15" />
        {{ fileName ? translate('form.sourceFileReplace') : translate('form.sourceFilePick') }}
      </NButton>
      <input
        ref="input"
        type="file"
        class="sdp__input"
        accept="application/pdf,.pdf"
        @change="onPicked"
      />
    </div>

    <p class="sdp__hint">{{ translate('form.sourceFileHint') }}</p>

    <NAlert v-if="error" type="warning" :bordered="false" class="sdp__error">
      {{ error }}
    </NAlert>
  </div>
</template>

<script setup lang="ts">
/**
 * The document behind an `Uploaded` signing template.
 *
 * Without this field, picking `Uploaded` produced a form the backend always
 * rejected with no field on it to fix - half the template feature was a dead
 * branch in the shipped UI.
 *
 * ★ It resolves to THREE model keys, not one: the original file, the name the
 *   operator recognises it by, and the rendered PDF. A request takes the
 *   template's rendered PDF verbatim - only Composed templates are laid out per
 *   request - so a template without one produces envelopes carrying an empty
 *   document reference all the way to the moment someone tries to sign. The
 *   component emits the pair and the page writes the keys, because
 *   `FieldRenderContext` carries only this field's own value.
 *
 * ★ PDF only, deliberately. When the source is already a PDF the rendered id is
 *   the same file, which is the whole of the conversion step for that case.
 *   Other formats need a real conversion pass the framework does not ship yet,
 *   and accepting them here would hand back a template that looks saved and
 *   fails later - which is the failure this component exists to remove.
 */
import { ref } from 'vue'
import { NAlert, NButton } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

/** What the picker resolved to; `null` means the operator removed it. */
export interface PickedSourceDocument {
  fileId: string
  fileName: string
}

const props = defineProps<{
  /** Current file name, for display. */
  fileName?: string | null
  readonly: boolean
  translate: (key: string) => string
  /** Stores the picked file and returns its record. Injected so this stays bridge-free. */
  upload: (file: File) => Promise<{ id?: string; fileName?: string | null }>
}>()

const emit = defineEmits<{ change: [PickedSourceDocument | null] }>()

const input = ref<HTMLInputElement | null>(null)
const busy = ref(false)
const error = ref('')

function pick() {
  error.value = ''
  input.value?.click()
}

function clear() {
  error.value = ''
  emit('change', null)
}

async function onPicked(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  // Reset immediately so picking the same file twice still fires a change.
  target.value = ''
  if (!file) return

  const isPdf = file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf')
  if (!isPdf) {
    error.value = props.translate('form.sourceFilePdfOnly')
    return
  }

  error.value = ''
  busy.value = true
  try {
    const record = await props.upload(file)
    if (!record?.id) throw new Error(props.translate('form.sourceFileFailed'))
    emit('change', { fileId: record.id, fileName: record.fileName || file.name })
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    busy.value = false
  }
}
</script>

<style scoped>
.sdp {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.sdp__current {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.sdp__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.sdp__input {
  display: none;
}

.sdp__hint {
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #999);
}
</style>
