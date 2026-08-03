<template>
  <div
    class="t-attach"
    :class="{ 't-attach--dropping': dropping }"
    @dragenter.prevent="onDragEnter"
    @dragover.prevent
    @dragleave="onDragLeave"
    @drop.prevent="onDrop"
  >
    <div class="t-attach__head">
      <span class="t-attach__title">{{ label('title') }}</span>
      <span class="t-attach__count">{{ items.length }}</span>
      <NButton v-if="canAdd" size="tiny" :loading="busy" class="t-attach__add" @click="pick">
        <template #icon><TSvgIcon icon="mdi:paperclip" :size="14" /></template>
        {{ label('add') }}
      </NButton>
    </div>

    <NAlert v-if="error" type="error" :bordered="false" closable class="t-attach__error" @close="error = null">
      {{ error }}
    </NAlert>

    <ul v-if="items.length" class="t-attach__list">
      <li v-for="item in items" :key="item.id" class="t-attach__item">
<TFileLink class="t-attach__link" :file-id="item.fileId" :title="item.fileName">
          <TSvgIcon :icon="iconFor(item.contentType, item.fileName)" :size="18" class="t-attach__icon" />
          <span class="t-attach__name">{{ item.fileName }}</span>
        </TFileLink>
        <span class="t-attach__size">{{ formatFileSize(item.fileSize) }}</span>
        <NPopconfirm v-if="canRemove" @positive-click="removeAttachment(item)">
          <template #trigger>
            <NButton quaternary circle size="tiny" :aria-label="label('remove')">
              <TSvgIcon icon="mdi:close" :size="14" />
            </NButton>
          </template>
          {{ label('removeConfirm') }}
        </NPopconfirm>
      </li>
    </ul>

    <TEmpty v-else-if="!loading" :text="canAdd ? label('emptyDroppable') : label('empty')" />

    <input ref="fileInput" type="file" multiple class="t-attach__input" @change="onFilesPicked" />
  </div>
</template>

<script setup lang="ts">
/**
 * `TAttachmentPanel` - the supporting files behind a record.
 *
 * Deliberately entity-agnostic: it takes a `(docType, docId)` pair and an
 * upload/link contract, so an invoice, a matter, or a consumer app's own
 * document all reuse the same panel instead of each growing their own.
 *
 * Upload is two steps by design - the file goes to Storage, then only its id is
 * linked to the record. Nothing here assumes the record's owner module can see
 * Storage at all (the finance module deliberately cannot).
 */
import { ref, watch } from 'vue'
import { NAlert, NButton, NPopconfirm } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TFileLink from '../display/TFileLink.vue'
import { formatFileSize } from '@tnzi/core'
import { TEmpty } from '@tnzi/ui'

export interface AttachmentItem {
  id: string
  fileId: string
  fileName: string
  contentType?: string | null
  fileSize: number
  caption?: string | null
  creationTime?: string
}

const props = withDefaults(
  defineProps<{
    items: AttachmentItem[]
    loading?: boolean
    canAdd?: boolean
    canRemove?: boolean
    /** Upload a file and return the stored id + metadata to link. */
    upload?: (file: File) => Promise<{ fileId: string; fileName: string; contentType?: string | null; fileSize: number }>
    /** Link an uploaded file to the record. */
    attach?: (linked: { fileId: string; fileName: string; contentType?: string | null; fileSize: number }) => Promise<void>
    remove?: (item: AttachmentItem) => Promise<void>
    /** i18n lookup relative to `attachments.*`. */
    translate?: (key: string) => string
  }>(),
  { loading: false, canAdd: true, canRemove: true },
)

const emit = defineEmits<{ changed: [] }>()

const FALLBACK: Record<string, string> = {
  title: 'Attachments',
  add: 'Attach',
  remove: 'Remove attachment',
  removeConfirm: 'Remove this attachment?',
  empty: 'No attachments.',
  emptyDroppable: 'No attachments yet - drop a file here or use Attach.',
  uploadFailed: 'Upload failed.',
}

function label(key: string): string {
  const translated = props.translate?.(`attachments.${key}`)
  if (translated && !translated.includes(`attachments.${key}`)) return translated
  return FALLBACK[key] ?? key
}

const fileInput = ref<HTMLInputElement | null>(null)
const busy = ref(false)
const error = ref<string | null>(null)
const dropping = ref(false)
// Nested dragenter/dragleave fire per child element; count them or the
// highlight flickers as the pointer crosses the rows.
const dragDepth = ref(0)

watch(() => props.items, () => { error.value = null })

/** A recognisable glyph beats a generic page icon when scanning a list. */
function iconFor(contentType?: string | null, name?: string): string {
  const type = (contentType ?? '').toLowerCase()
  const ext = (name ?? '').split('.').pop()?.toLowerCase() ?? ''
  if (type.startsWith('image/')) return 'mdi:file-image-outline'
  if (type.includes('pdf') || ext === 'pdf') return 'mdi:file-pdf-box'
  if (type.includes('sheet') || ['xls', 'xlsx', 'csv'].includes(ext)) return 'mdi:file-table-outline'
  if (type.includes('word') || ['doc', 'docx'].includes(ext)) return 'mdi:file-document-outline'
  if (type.startsWith('text/')) return 'mdi:file-document-outline'
  return 'mdi:file-outline'
}

function pick() {
  fileInput.value?.click()
}

function onDragEnter() {
  if (!props.canAdd) return
  dragDepth.value++
  dropping.value = true
}

function onDragLeave() {
  if (!props.canAdd) return
  dragDepth.value--
  if (dragDepth.value <= 0) { dragDepth.value = 0; dropping.value = false }
}

async function onDrop(event: DragEvent) {
  dragDepth.value = 0
  dropping.value = false
  if (!props.canAdd) return
  const files = [...(event.dataTransfer?.files ?? [])]
  if (files.length) await send(files)
}

async function onFilesPicked(event: Event) {
  const input = event.target as HTMLInputElement
  const files = [...(input.files ?? [])]
  input.value = ''
  if (files.length) await send(files)
}

async function send(files: File[]) {
  if (!props.upload || !props.attach) return
  busy.value = true
  error.value = null
  try {
    // Sequential on purpose: the server caps attachments per record, and a
    // parallel burst would report that cap as an opaque failure on a random file.
    for (const file of files) {
      const stored = await props.upload(file)
      await props.attach(stored)
    }
    emit('changed')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    busy.value = false
  }
}

async function removeAttachment(item: AttachmentItem) {
  if (!props.remove) return
  try {
    await props.remove(item)
    emit('changed')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}
</script>

<style scoped>
.t-attach {
  display: flex;
  flex-direction: column;
  gap: 8px;
  border-radius: var(--tnzi-admin-radius-md, 6px);
  transition: background 0.15s ease, outline-color 0.15s ease;
  outline: 2px dashed transparent;
  outline-offset: 2px;
}

.t-attach--dropping {
  outline-color: var(--tnzi-primary);
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.04);
}

.t-attach__head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.t-attach__title {
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--tnzi-base-text-muted);
}

.t-attach__count {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
}

.t-attach__add {
  margin-left: auto;
}

.t-attach__error {
  font-size: 12px;
}

.t-attach__list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.t-attach__item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 5px 8px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 6px);
  min-width: 0;
}

.t-attach__link {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1 1 auto;
  min-width: 0;
  color: inherit;
  text-decoration: none;
}

.t-attach__link:hover .t-attach__name {
  color: var(--tnzi-primary);
  text-decoration: underline;
}

.t-attach__icon {
  flex-shrink: 0;
  color: var(--tnzi-base-text-muted);
}

.t-attach__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
}

.t-attach__size {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
  flex-shrink: 0;
}

.t-attach__input {
  display: none;
}
</style>
