<template>
  <!--
    Inline attachment grid: image thumbnails open in a shared lightbox
    (NImageGroup), non-image files render as a type-glyph chip linking to the
    file, with optional per-tile remove and a trailing add tile. For embedding
    receipts / attachments in a form or card — lighter than the Storage explorer.
  -->
  <div class="t-attachment-wall">
    <n-image-group>
      <div v-for="(att, i) in attachments" :key="keyOf(att, i)" class="t-attachment-wall__tile" :style="tileStyle">
        <n-image
          v-if="isImage(att)"
          :src="att.url"
          :width="size"
          :height="size"
          object-fit="cover"
          class="t-attachment-wall__img"
        />
        <a v-else :href="safeHref(att.url)" target="_blank" rel="noopener" class="t-attachment-wall__file" :title="att.name ?? ''">
          <Icon :icon="glyph(att)" class="t-attachment-wall__glyph" />
          <span class="t-attachment-wall__name">{{ att.name ?? 'File' }}</span>
        </a>
        <button
          v-if="removable"
          type="button"
          class="t-attachment-wall__remove"
          aria-label="Remove"
          @click.stop="emit('remove', att)"
        >
          <Icon icon="mdi:close" />
        </button>
      </div>
    </n-image-group>
    <button v-if="addable" type="button" class="t-attachment-wall__add" :style="tileStyle" @click="emit('add')">
      <Icon icon="mdi:plus" />
    </button>
    <div v-if="!attachments.length && !addable" class="t-attachment-wall__empty">{{ emptyText }}</div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NImage, NImageGroup } from 'naive-ui'
import { Icon } from '@iconify/vue'

export interface Attachment {
  url: string
  name?: string
  /** Force image treatment; otherwise inferred from `contentType` / extension. */
  isImage?: boolean
  contentType?: string
}

const props = withDefaults(
  defineProps<{
    attachments: Attachment[]
    /** Stable key per attachment (for remove/reorder). Default: `att.url`. */
    itemKey?: (att: Attachment, index: number) => string | number
    /** Show a per-tile remove button. */
    removable?: boolean
    /** Show a trailing add tile (emits `add`). */
    addable?: boolean
    /** Tile size in px. Default 72. */
    size?: number
    emptyText?: string
  }>(),
  { size: 72, emptyText: 'No attachments' },
)

const emit = defineEmits<{ add: []; remove: [attachment: Attachment] }>()

const tileStyle = computed(() => ({ width: `${props.size}px`, height: `${props.size}px` }))

const keyOf = (att: Attachment, index: number): string | number => props.itemKey?.(att, index) ?? att.url

/**
 * Guard a caller-supplied file URL used in `<a :href>` — Vue does not sanitize
 * `:href`, so a `javascript:` / `vbscript:` / `data:text/html` scheme would run
 * on click. Everything else (http/https, relative, blob:, data: media) passes.
 */
function safeHref(url: string): string {
  return /^\s*(javascript:|vbscript:|data:text\/html)/i.test(url) ? '#' : url
}

function isImage(att: Attachment): boolean {
  if (att.isImage !== undefined) return att.isImage
  if (att.contentType) return att.contentType.startsWith('image/')
  return /\.(png|jpe?g|gif|webp|bmp|svg)$/i.test(att.name ?? att.url)
}

function glyph(att: Attachment): string {
  const s = `${att.contentType ?? ''} ${att.name ?? att.url}`.toLowerCase()
  if (s.includes('pdf')) return 'mdi:file-pdf-box'
  if (/\.(docx?|word)/.test(s) || s.includes('word')) return 'mdi:file-word-box'
  if (/\.(xlsx?|csv)/.test(s) || s.includes('spreadsheet') || s.includes('excel')) return 'mdi:file-excel-box'
  if (/\.(zip|rar|7z|tar|gz)/.test(s)) return 'mdi:folder-zip-outline'
  return 'mdi:file-outline'
}
</script>

<style scoped>
.t-attachment-wall {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: flex-start;
}
.t-attachment-wall :deep(.n-image-group) {
  display: contents;
}
.t-attachment-wall__tile {
  position: relative;
  border-radius: 8px;
  overflow: hidden;
  border: 1px solid var(--tnzi-border, rgba(0, 0, 0, 0.08));
  background: var(--tnzi-container-bg, #fff);
}
.t-attachment-wall__img {
  display: block;
  border-radius: 8px;
}
.t-attachment-wall__file {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  width: 100%;
  height: 100%;
  padding: 6px;
  text-decoration: none;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.65));
}
.t-attachment-wall__glyph {
  font-size: 26px;
}
.t-attachment-wall__name {
  max-width: 100%;
  font-size: 11px;
  line-height: 1.2;
  text-align: center;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-attachment-wall__remove {
  position: absolute;
  top: 2px;
  right: 2px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  border: none;
  border-radius: 50%;
  background: rgba(0, 0, 0, 0.5);
  color: #fff;
  cursor: pointer;
  font-size: 12px;
}
.t-attachment-wall__add {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 1px dashed var(--tnzi-border, rgba(0, 0, 0, 0.2));
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.4));
  cursor: pointer;
  font-size: 22px;
}
.t-attachment-wall__empty {
  padding: 12px 0;
  font-size: 13px;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.45));
}
</style>
