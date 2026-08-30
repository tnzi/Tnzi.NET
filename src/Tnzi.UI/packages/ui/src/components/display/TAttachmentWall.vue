<template>
  <!--
    Inline attachment grid: image thumbnails open in a shared lightbox
    (NImageGroup), non-image files render as a type-glyph chip linking to the
    file, with optional per-tile remove and a trailing add tile. For embedding
    receipts / attachments in a form or card - lighter than the Storage explorer.
  -->
  <div ref="rootEl" class="t-attachment-wall">
    <n-image-group>
      <component :is="reorderEnabled ? dragContainer : TileFragment" v-bind="dragBindings">
        <div
          v-for="(att, i) in renderAttachments"
          :key="keyOf(att, i)"
          class="t-attachment-wall__tile t-media-frame"
          :class="{ 't-attachment-wall__tile--reorderable': reorderEnabled }"
          :style="tileStyle"
          :tabindex="reorderEnabled ? 0 : undefined"
          :aria-label="reorderEnabled ? tileLabel(att, i) : undefined"
          @keydown.self="onTileKeydown($event, i)"
        >
          <slot name="tile" :attachment="att" :index="i" :size="size" :is-image="isImage(att)" :glyph="glyph(att)">
            <n-image
              v-if="isImage(att)"
              :src="att.url"
              :width="size"
              :height="size"
              object-fit="cover"
              class="t-attachment-wall__img"
            />
            <a
              v-else
              :href="safeHref(att.url)"
              target="_blank"
              rel="noopener"
              class="t-attachment-wall__file"
              :title="att.name ?? ''"
            >
              <Icon :icon="glyph(att)" class="t-attachment-wall__glyph" />
              <span class="t-attachment-wall__name">{{ att.name ?? 'File' }}</span>
            </a>
          </slot>
          <!--
            The only surface a pointer gesture starts on. Not focusable and
            aria-hidden: it duplicates no capability, because the keyboard
            reorders through the tile itself, which is what announces the
            shortcut. See `DRAG_HANDLE` for why a handle and not a filter.
          -->
          <span v-if="reorderEnabled" class="t-attachment-wall__grip t-media-chip t-media-grip" aria-hidden="true">
            <Icon icon="mdi:drag" />
          </span>
          <button
            v-if="removable"
            type="button"
            class="t-attachment-wall__remove t-media-chip t-media-remove"
            aria-label="Remove"
            @click.stop="emit('remove', att)"
          >
            <Icon icon="mdi:close" />
          </button>
        </div>
      </component>
    </n-image-group>
    <button v-if="addable" type="button" class="t-attachment-wall__add" :style="tileStyle" @click="emit('add')">
      <Icon icon="mdi:plus" />
    </button>
    <div v-if="!attachments.length && !addable" class="t-attachment-wall__empty">{{ emptyText }}</div>
    <!--
      A move made with the keyboard makes no sound and moves no focus (the tile
      keeps it, it just sits somewhere else), so without this the operator gets
      no confirmation that anything happened. Rendered only while reordering is
      on, and it has to be in the DOM BEFORE its text changes or the change is
      not announced at all.
    -->
    <span v-if="reorderEnabled" class="t-attachment-wall__sr" role="status" aria-live="polite">{{ announcement }}</span>
  </div>
</template>

<script setup lang="ts">
import { computed, defineComponent, nextTick, ref } from 'vue'
import { NImage, NImageGroup } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useReorderable } from '../../headless/data/useReorderable'

export interface Attachment {
  /**
   * Directly-renderable URL. Optional, because a private file has no such URL:
   * it is reachable only through a short-lived signed URL fetched at render
   * time. Those tiles supply `id` and render through the `#tile` slot.
   */
  url?: string
  /** Stable identity - and what a `#tile` slot needs to resolve the file. */
  id?: string | number
  name?: string
  /** Force image treatment; otherwise inferred from `contentType` / extension. */
  isImage?: boolean
  contentType?: string
}

/** Payload of the `reorder` event. */
export interface AttachmentReorderPayload {
  /** Every attachment in its new order - this is what you persist. */
  attachments: Attachment[]
  /** The same sequence as keys, resolved through `itemKey` / `id` / `url`. */
  orderedKeys: (string | number)[]
  /** Index the attachment came from. */
  from: number
  /** Index it landed at. */
  to: number
  /** The attachment that moved. */
  moved: Attachment
  /**
   * Put the wall back the way it was before this move. Call it when the persist
   * call fails: the move is applied optimistically, so the tile is already in
   * its new place, and without this the screen keeps showing an order the
   * server rejected while nothing tells the operator their change was lost.
   */
  revert: () => void
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
    /**
     * Let the user change the order of the tiles - by drag and by keyboard -
     * and report the result through `reorder`. Off by default: a wall whose
     * order carries no meaning should not offer to change it, and turning this
     * on adds one tab stop per tile.
     *
     * ★ Give the attachments a stable identity (`id`, `url` or `itemKey`)
     * before turning this on. The index fallback re-keys every tile on every
     * move, so Vue re-renders tile contents in place instead of moving the
     * nodes, and a `#tile` slot that resolves a signed URL per file would
     * re-resolve the whole wall after each move.
     *
     * Dragging is done from a grip that appears on each tile, and needs the
     * optional peer `vue-draggable-plus`, loaded on demand the first time this
     * is on. The keyboard path has no dependency at all, so a wall whose drag
     * module never arrives still reorders (see `useReorderable`).
     */
    reorderable?: boolean
    /**
     * Keyboard instructions appended to each tile's accessible name while
     * `reorderable` is on. Override it to translate it.
     */
    reorderHint?: string
  }>(),
  {
    size: 72,
    emptyText: 'No attachments',
    reorderHint: 'Use Alt with the arrow keys to reorder.',
  },
)

const emit = defineEmits<{
  add: []
  remove: [attachment: Attachment]
  reorder: [payload: AttachmentReorderPayload]
}>()

defineSlots<{
  /**
   * Replaces a tile's contents; the frame, the remove button, the add tile and
   * the shared lightbox group stay ours.
   *
   * ★ This is the seam for files that are NOT publicly readable. Their URL is a
   * short-lived signed one that has to be fetched, and the machinery that does
   * the fetching lives a layer ABOVE this package (it needs the HTTP client and
   * the storage bridge). Rather than invert that dependency - or make the host
   * pre-resolve every URL into a map prop, which the framework already retired
   * once - the host renders its own resolving component per tile right here.
   *
   * ```
   * <TAttachmentWall :attachments="files">
   *   <template #tile="{ attachment, size, isImage }">
   *     <TFileImage v-if="isImage" :file-id="attachment.id" :width="size" :height="size" />
   *     <TFileLink v-else :file-id="attachment.id">{{ attachment.name }}</TFileLink>
   *   </template>
   * </TAttachmentWall>
   * ```
   */
  tile?: (props: {
    attachment: Attachment
    index: number
    size: number
    isImage: boolean
    glyph: string
  }) => unknown
}>()

const tileStyle = computed(() => ({ width: `${props.size}px`, height: `${props.size}px` }))

const keyOf = (att: Attachment, index: number): string | number =>
  props.itemKey?.(att, index) ?? att.id ?? att.url ?? index

/**
 * Guard a caller-supplied file URL used in `<a :href>` - Vue does not sanitize
 * `:href`, so a `javascript:` / `vbscript:` / `data:text/html` scheme would run
 * on click. Everything else (http/https, relative, blob:, data: media) passes.
 */
function safeHref(url: string | undefined): string | undefined {
  // No URL at all (a private file whose host did not fill the `#tile` slot):
  // emit no href. An `<a>` without one is not a link - it neither navigates nor
  // takes focus - whereas `#` would scroll the page to the top on click.
  if (!url) return undefined
  return /^\s*(javascript:|vbscript:|data:text\/html)/i.test(url) ? '#' : url
}

function isImage(att: Attachment): boolean {
  if (att.isImage !== undefined) return att.isImage
  if (att.contentType) return att.contentType.startsWith('image/')
  return /\.(png|jpe?g|gif|webp|bmp|svg)$/i.test(att.name ?? att.url ?? '')
}

function glyph(att: Attachment): string {
  const s = `${att.contentType ?? ''} ${att.name ?? att.url ?? ''}`.toLowerCase()
  if (s.includes('pdf')) return 'mdi:file-pdf-box'
  if (/\.(docx?|word)/.test(s) || s.includes('word')) return 'mdi:file-word-box'
  if (/\.(xlsx?|csv)/.test(s) || s.includes('spreadsheet') || s.includes('excel')) return 'mdi:file-excel-box'
  if (/\.(zip|rar|7z|tar|gz)/.test(s)) return 'mdi:folder-zip-outline'
  return 'mdi:file-outline'
}

/* -------------------------------------------------------------- reordering */

/**
 * Renders its children and nothing else - no element of its own.
 *
 * The tiles are direct flex children of `.t-attachment-wall` (`n-image-group`
 * is `display: contents` for exactly that reason), so an extra box in between
 * would re-flow every wall that exists today. `:is` still needs *something* to
 * resolve to while reordering is off, and a fragment-returning component is the
 * only thing that renders no markup at all.
 */
const TileFragment = defineComponent({
  name: 'TAttachmentWallTiles',
  inheritAttrs: false,
  setup: (_props, { slots }) => () => slots.default?.() ?? null,
})

/**
 * The only element a pointer gesture starts on.
 *
 * ★ The obvious alternative - a `filter` listing the controls that must stay
 * clickable - is what this component shipped with first, and it made file tiles
 * undraggable. A non-image attachment renders as one `<a>` stretched over the
 * whole tile (`.t-attachment-wall__file` is `width: 100%; height: 100%`), and
 * Sortable matches `filter` against the press target AND its ancestors - so
 * every pixel of a PDF tile matched `a`, the gesture was refused, and nothing
 * said so. A wall of documents simply ignored the mouse.
 *
 * A handle also settles a question a filter cannot: an image tile is already a
 * thing you click (it opens the lightbox), so "press and move" and "press and
 * release" were competing for the same pixels. `TWorkbenchLayout` picks its
 * handle the same way, and Sortable returns from a press outside the handle
 * without calling `preventDefault`, so the remove button and the file link keep
 * working with no filter list to maintain.
 */
const DRAG_HANDLE = '.t-attachment-wall__grip'

const reorderEnabled = computed(() => props.reorderable === true)

const rootEl = ref<HTMLElement | null>(null)
const announcement = ref('')

function nameOf(att: Attachment): string {
  return att.name ?? (isImage(att) ? 'Image' : 'File')
}

const {
  dragContainer,
  renderItems: renderAttachments,
  dragBindings,
  moveByKeyboard,
} = useReorderable<Attachment>({
  source: () => props.attachments,
  enabled: () => reorderEnabled.value,
  containerClass: () => 't-attachment-wall__strip',
  handle: () => DRAG_HANDLE,
  ghostClass: 't-attachment-wall__tile--ghost',
  chosenClass: 't-attachment-wall__tile--chosen',
  onCommit: (move) => {
    announcement.value = `${nameOf(move.moved)}, ${move.to + 1} / ${move.items.length}`
    emit('reorder', {
      attachments: move.items,
      orderedKeys: move.items.map((att, i) => keyOf(att, i)),
      from: move.from,
      to: move.to,
      moved: move.moved,
      revert: move.revert,
    })
  },
})

function tileLabel(att: Attachment, index: number): string {
  return `${nameOf(att)}, ${index + 1} / ${renderAttachments.value.length}. ${props.reorderHint}`
}

function focusTile(index: number): void {
  const tiles = rootEl.value?.querySelectorAll<HTMLElement>('.t-attachment-wall__tile')
  tiles?.[index]?.focus()
}

/**
 * Keyboard reordering: Alt + arrow keys, Alt + Home / End. The mapping, the
 * `preventDefault`-before-bounds-check and the move itself live in
 * `useReorderable`; what is local is where focus goes afterwards.
 *
 * Bound with `.self`: the tile is the thing whose accessible name announces the
 * shortcut, so it is the thing the shortcut acts on. A `#tile` slot can hold its
 * own focusable content (the documented example is a file link), and swallowing
 * Alt + Left there would take Back away from someone who never saw the offer.
 */
function onTileKeydown(event: KeyboardEvent, index: number): void {
  const to = moveByKeyboard(event, index)
  if (to === null) return

  // Vue keeps focus on a keyed tile because it moves the node, but an
  // index-keyed wall re-renders in place instead - so put focus where the tile
  // now is either way.
  void nextTick(() => focusTile(to))
}

</script>

<style scoped>
.t-attachment-wall {
  /* Positioned so the visually-hidden live region below anchors to the wall
     rather than to whatever positioned ancestor happens to be up the tree. */
  position: relative;
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: flex-start;
}
.t-attachment-wall :deep(.n-image-group) {
  display: contents;
}
/* Only exists while reordering is on. Sortable measures its container, so this
   one cannot be `display: contents` the way the image group is - it re-creates
   the wall's own flex layout instead, and shrinks so the add tile still fits
   beside it. */
.t-attachment-wall__strip {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: flex-start;
  flex: 0 1 auto;
  min-width: 0;
}
.t-attachment-wall__tile {
  position: relative;
  border-radius: 8px;
  overflow: hidden;
  border: var(--tnzi-surface-inset-border);
  background: var(--tnzi-container-bg, #fff);
}
/* Position only - the grip's material and its reveal are shared with the remove
   chip (`styles/media-chip.css`, classes `t-media-chip` + `t-media-grip`). Top
   LEFT so it never overlaps the remove chip on the right; both sit inside the
   tile because the tile clips its overflow for the thumbnail's rounded corners.
   The cursor lives with the grip, not with the tile: the tile is still a thing
   you click (it opens the lightbox), and a `grab` on all of it would claim
   otherwise. */
.t-attachment-wall__grip {
  position: absolute;
  top: 2px;
  left: 2px;
  z-index: 1;
}
.t-attachment-wall__tile--reorderable:focus-visible {
  outline: 2px solid var(--tnzi-primary, #646cff);
  outline-offset: 2px;
}
/* The placeholder left at the drop target. Faded rather than hidden so the wall
   does not reflow under the pointer mid-gesture. */
.t-attachment-wall__tile--ghost {
  opacity: 0.35;
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
/* Position only - the chip's material, its visibility rule and its hover
   feedback are shared with `TImageUpload` and live in `styles/media-chip.css`
   (classes `t-media-chip` + `t-media-remove`, revealed by the `t-media-frame`
   on the tile). Inside the tile rather than on its outer corner because the
   tile clips its overflow for the thumbnail's rounded corners, so a chip hung
   outside would be cut in half. */
.t-attachment-wall__remove {
  position: absolute;
  top: 2px;
  right: 2px;
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
/* Announcement target for reordering. Off-screen but still readable by
   assistive technology - `display: none` and `visibility: hidden` are skipped
   by screen readers, so neither of those can be used here. */
.t-attachment-wall__sr {
  position: absolute;
  width: 1px;
  height: 1px;
  margin: -1px;
  padding: 0;
  overflow: hidden;
  clip-path: inset(50%);
  white-space: nowrap;
  border: 0;
}
</style>
