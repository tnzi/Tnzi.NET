<script setup lang="ts">
/**
 * @experimental
 * TThreadTitle - the open conversation's title in the top bar, with the
 * actions that belong to that conversation.
 *
 * Two controls, as in claude.ai's top bar:
 *
 * - the title itself: clicking it turns it, in place, into a field holding the
 *   whole title selected. The field reads as a field (border, radius, focus
 *   ring) and is as wide as its text: it opens where the title was, at about
 *   the same size, and grows or shrinks as the user types (a few characters
 *   at the least), up to the room the top bar has; past that the text scrolls
 *   inside it. Enter or leaving the field saves, Escape cancels, an empty or
 *   unchanged title is not sent (`rename`);
 * - a chevron after it: a menu with Rename (the same in-place edit), Archive or
 *   Unarchive, and Delete.
 *
 * Delete is confirmed in the bar itself ("Delete this conversation?" with
 * Delete / Cancel) rather than in a dialog: the list confirms deletes inline,
 * and this is the same action in a different place. Escape or Cancel backs out.
 *
 * The parent decides which actions exist (`enableRename` / `enableArchive` /
 * `enableDelete`) and when to show this at all: `TChatApp` shows it for a saved
 * conversation when it has a `recents` controller.
 */
import { computed, nextTick, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { isImeComposing } from '@tnzi/core/utils'
import { useAiI18n } from '../../i18n/index'
import TActionMenu, { type ActionMenuItem } from '../overlay/TActionMenu.vue'

const props = withDefaults(
  defineProps<{
    title: string
    /** Whether the conversation is archived (the menu offers Unarchive). */
    archived?: boolean
    enableRename?: boolean
    enableArchive?: boolean
    enableDelete?: boolean
  }>(),
  {
    archived: false,
    enableRename: true,
    enableArchive: true,
    enableDelete: true,
  },
)

const emit = defineEmits<{
  rename: [title: string]
  archive: []
  unarchive: []
  delete: []
}>()

const t = useAiI18n()

const editing = ref(false)
const draft = ref('')
const input = ref<HTMLInputElement | null>(null)
const titleButton = ref<HTMLButtonElement | null>(null)
/* The field's width follows its text, measured in the field's own computed
   font on a canvas (nothing is added to the page, so a long title cannot widen
   anything around the top bar). It reads the field itself on every `input`
   event rather than `draft`: v-model holds back while an IME is composing, and
   a CJK title should widen as the characters are being composed, not only
   when they are committed. */
const inputWidth = ref<string | undefined>(undefined)
/** The field's horizontal padding and border (7px + 1px on each side). */
const FIELD_CHROME_PX = 16
/** Room for the caret after the last character. */
const CARET_PX = 2
let measureContext: CanvasRenderingContext2D | null | undefined

function textWidth(el: HTMLInputElement, text: string): number {
  if (measureContext === undefined) {
    measureContext = typeof document === 'undefined' ? null : document.createElement('canvas').getContext('2d')
  }
  if (typeof measureContext?.measureText !== 'function') return 0
  const style = getComputedStyle(el)
  measureContext.font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`
  return measureContext.measureText(text).width
}

function fitInput(el: HTMLInputElement | null, text: string): void {
  if (!el) return
  inputWidth.value = `${Math.ceil(textWidth(el, text)) + FIELD_CHROME_PX + CARET_PX}px`
}

function onEditInput(event: Event): void {
  const el = event.target as HTMLInputElement
  fitInput(el, el.value)
}
const confirming = ref(false)
const cancelButton = ref<HTMLButtonElement | null>(null)

const menuItems = computed<ActionMenuItem[]>(() => {
  const items: ActionMenuItem[] = []
  if (props.enableRename) items.push({ id: 'rename', label: t.value.recents.rename, icon: 'lucide:pencil' })
  if (props.enableArchive) {
    items.push(
      props.archived
        ? { id: 'unarchive', label: t.value.recents.unarchive, icon: 'lucide:archive-restore' }
        : { id: 'archive', label: t.value.recents.archive, icon: 'lucide:archive' },
    )
  }
  if (props.enableDelete) {
    items.push({ id: 'delete', label: t.value.recents.delete, icon: 'lucide:trash-2', danger: true, dividerBefore: items.length > 0 })
  }
  return items
})

async function startEdit(): Promise<void> {
  if (!props.enableRename) return
  confirming.value = false
  draft.value = props.title
  editing.value = true
  inputWidth.value = undefined
  await nextTick()
  fitInput(input.value, props.title)
  input.value?.focus()
  input.value?.select()
}

async function finishEdit(save: boolean): Promise<void> {
  if (!editing.value) return
  editing.value = false
  const next = draft.value.trim()
  if (save && next && next !== props.title) emit('rename', next)
  await nextTick()
  titleButton.value?.focus()
}

function onEditKeydown(event: KeyboardEvent): void {
  if (isImeComposing(event)) return
  if (event.key === 'Enter') {
    event.preventDefault()
    void finishEdit(true)
  } else if (event.key === 'Escape') {
    /* Claimed, so an overlay underneath does not close on the same key. */
    event.preventDefault()
    void finishEdit(false)
  }
}

async function onMenuSelect(id: string): Promise<void> {
  if (id === 'rename') {
    /* The menu hands focus back to its trigger as it closes; start editing after that. */
    await nextTick()
    void startEdit()
  } else if (id === 'archive') emit('archive')
  else if (id === 'unarchive') emit('unarchive')
  else if (id === 'delete') {
    confirming.value = true
    await nextTick()
    await nextTick()
    cancelButton.value?.focus()
  }
}

function confirmDelete(): void {
  confirming.value = false
  emit('delete')
}

async function cancelDelete(): Promise<void> {
  confirming.value = false
  await nextTick()
  titleButton.value?.focus()
}

function onConfirmKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape') return
  event.preventDefault()
  void cancelDelete()
}
</script>

<template>
  <div class="t-thread-title" :class="{ 'is-editing': editing }">
    <div v-if="confirming" class="t-thread-title__confirm" role="group" :aria-label="t.recents.deleteThreadConfirm" @keydown="onConfirmKeydown">
      <span class="t-thread-title__confirm-text">{{ t.recents.deleteThreadConfirm }}</span>
      <button type="button" class="t-thread-title__confirm-btn t-thread-title__confirm-btn--danger" @click="confirmDelete">
        {{ t.recents.delete }}
      </button>
      <button ref="cancelButton" type="button" class="t-thread-title__confirm-btn" @click="cancelDelete">
        {{ t.common.cancel }}
      </button>
    </div>
    <template v-else>
      <input
        v-if="editing"
        ref="input"
        v-model="draft"
        class="t-thread-title__input"
        :style="{ width: inputWidth }"
        :aria-label="t.recents.rename"
        maxlength="200"
        @input="onEditInput"
        @keydown="onEditKeydown"
        @blur="finishEdit(true)"
      />
      <button
        v-else
        ref="titleButton"
        type="button"
        class="t-thread-title__text"
        :title="enableRename ? t.recents.rename : undefined"
        :disabled="!enableRename"
        @click="startEdit"
      >{{ title }}</button>
      <TActionMenu
        v-if="menuItems.length > 0 && !editing"
        :items="menuItems"
        :label="t.recents.moreActions"
        align="start"
        @select="onMenuSelect"
      >
        <template #trigger="{ toggle, attrs }">
          <button
            type="button"
            class="t-thread-title__chevron"
            v-bind="attrs"
            :aria-label="t.recents.moreActions"
            @click="toggle"
          >
            <Icon icon="lucide:chevron-down" />
          </button>
        </template>
      </TActionMenu>
    </template>
  </div>
</template>

<style scoped>
/* Title and field share the font, height and text inset, so the words stay
   where they were when one turns into the other. The field is a real field:
   a title button shrinks to its text, and a borderless field inheriting that
   width was a one-line sliver showing the end of the title. The field is
   sized to its text (measured in script, set inline), so it opens
   at the title's own size; the block may shrink to the top bar's free room
   (`min-width: 0`), and the field with it, where the text then scrolls. */
.t-thread-title {
  display: flex;
  align-items: center;
  gap: 2px;
  min-width: 0;
  max-width: 100%;
}
.t-thread-title__text,
.t-thread-title__input {
  min-width: 0;
  height: 32px;
  box-sizing: border-box;
  padding: 0 8px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: var(--tnzi-ai-field-font-size, 14px);
  font-weight: 500;
  line-height: 30px;
}
.t-thread-title__text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  text-align: left;
  cursor: text;
}
.t-thread-title__text:disabled { cursor: default; }
.t-thread-title__text:hover:not(:disabled),
.t-thread-title__text:focus-visible { background: var(--tnzi-ai-hover); }
.t-thread-title__text:focus-visible { outline: 2px solid var(--tnzi-ai-accent); outline-offset: -2px; }
.t-thread-title__input {
  flex: 0 1 auto;
  /* A few characters at the least, so an empty or one-letter title still
     reads as a field; never wider than the block. (A plain length: a
     percentage here resolves against a block sized by this very field.) */
  min-width: 64px;
  max-width: 100%;
  /* 1px border inside the same 32px box; the inset shrinks by the same 1px. */
  padding: 0 7px;
  border: 1px solid var(--tnzi-ai-border-strong);
  background: var(--tnzi-ai-surface);
  outline: none;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    box-shadow var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-thread-title__input:focus {
  border-color: var(--tnzi-ai-accent);
  box-shadow: 0 0 0 3px var(--tnzi-ai-accent-soft);
}
.t-thread-title__chevron {
  flex-shrink: 0;
  width: 24px;
  height: 24px;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text-tertiary);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  cursor: pointer;
}
.t-thread-title__chevron:hover,
.t-thread-title__chevron[aria-expanded='true'] {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-thread-title__confirm {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  padding: 0 4px 0 8px;
  font-size: 14px;
}
.t-thread-title__confirm-text {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--tnzi-ai-text-secondary);
}
.t-thread-title__confirm-btn {
  flex-shrink: 0;
  height: 28px;
  padding: 0 10px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 8px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: 13px;
  cursor: pointer;
}
.t-thread-title__confirm-btn:hover { background: var(--tnzi-ai-hover); }
.t-thread-title__confirm-btn--danger {
  border-color: transparent;
  background: var(--tnzi-ai-danger);
  color: var(--tnzi-ai-on-status, #ffffff);
}
.t-thread-title__confirm-btn--danger:hover {
  background: color-mix(in srgb, var(--tnzi-ai-danger) 88%, black);
}
</style>
