<template>
  <div class="t-thread">
    <div class="t-thread__head">
      <span class="t-thread__title">{{ label('title') }}</span>
      <span class="t-thread__count">{{ items.length }}</span>
    </div>

    <NAlert v-if="error" type="error" :bordered="false" closable class="t-thread__error" @close="error = null">
      {{ error }}
    </NAlert>

    <ol v-if="items.length" class="t-thread__list">
      <li v-for="item in items" :key="item.id" class="t-thread__item">
        <TAvatar :name="item.creatorName ?? label('someone')" :size="26" class="t-thread__avatar" />
        <div class="t-thread__body">
          <div class="t-thread__meta">
            <span class="t-thread__author">{{ item.creatorName ?? label('someone') }}</span>
            <TRelativeTime :value="item.creationTime" class="t-thread__time" />
            <NPopconfirm v-if="item.canDelete && canDelete" @positive-click="deleteComment(item)">
              <template #trigger>
                <NButton quaternary circle size="tiny" class="t-thread__delete" :aria-label="label('delete')">
                  <TSvgIcon icon="mdi:close" :size="13" />
                </NButton>
              </template>
              {{ label('deleteConfirm') }}
            </NPopconfirm>
          </div>
          <!-- Plain text, rendered as text: a comment box that interprets markup
               is an injection surface nobody asked for. -->
          <p class="t-thread__text">{{ item.body }}</p>
        </div>
      </li>
    </ol>

    <TEmpty v-else-if="!loading" :text="label('empty')" />

    <div v-if="canPost" class="t-thread__composer">
      <NInput
        v-model:value="draft"
        type="textarea"
        size="small"
        :placeholder="label('placeholder')"
        :autosize="{ minRows: 2, maxRows: 6 }"
        :disabled="busy"
        @keydown="onKeydown"
      />
      <div class="t-thread__composer-actions">
        <span class="t-thread__hint">{{ label('submitHint') }}</span>
        <NButton size="small" type="primary" :loading="busy" :disabled="!draft.trim()" @click="submitComment">
          {{ label('post') }}
        </NButton>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * `TCommentThread` - the internal discussion attached to a record.
 *
 * Entity-agnostic like `TAttachmentPanel`: it renders a list and calls back to
 * post/delete, so any module reuses it rather than growing its own thread.
 *
 * `canDelete` is per-item and comes from the server. The client does not
 * re-derive "am I the author" - a locally-computed rule drifts from the
 * server's, and then the button shows but the call is refused.
 */
import { ref, watch } from 'vue'
import { NAlert, NButton, NInput, NPopconfirm } from 'naive-ui'
import { TSvgIcon, TAvatar, TRelativeTime } from '@tnzi/ui'
import { TEmpty } from '@tnzi/ui'

export interface CommentItem {
  id: string
  body: string
  creatorId?: string | null
  creatorName?: string | null
  creationTime: string
  /** Server's verdict on whether this viewer may delete this comment. */
  canDelete: boolean
}

const props = withDefaults(
  defineProps<{
    items: CommentItem[]
    loading?: boolean
    canPost?: boolean
    canDelete?: boolean
    post?: (body: string) => Promise<void>
    remove?: (item: CommentItem) => Promise<void>
    /** i18n lookup relative to `comments.*`. */
    translate?: (key: string) => string
  }>(),
  { loading: false, canPost: true, canDelete: true },
)

const emit = defineEmits<{ changed: [] }>()

const FALLBACK: Record<string, string> = {
  title: 'Discussion',
  empty: 'No comments yet.',
  placeholder: 'Add an internal note…',
  post: 'Post',
  submitHint: 'Ctrl + Enter to post',
  delete: 'Delete comment',
  deleteConfirm: 'Delete this comment?',
  someone: 'Someone',
}

function label(key: string): string {
  const translated = props.translate?.(`comments.${key}`)
  if (translated && !translated.includes(`comments.${key}`)) return translated
  return FALLBACK[key] ?? key
}

const draft = ref('')
const busy = ref(false)
const error = ref<string | null>(null)

watch(() => props.items, () => { error.value = null })

function onKeydown(event: KeyboardEvent) {
  if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
    event.preventDefault()
    void submitComment()
  }
}

async function submitComment() {
  const body = draft.value.trim()
  if (!body || !props.post) return
  busy.value = true
  error.value = null
  try {
    await props.post(body)
    // Only clear on success - losing what someone typed because the request
    // failed is the least forgivable thing a comment box can do.
    draft.value = ''
    emit('changed')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    busy.value = false
  }
}

async function deleteComment(item: CommentItem) {
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
.t-thread {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.t-thread__head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.t-thread__title {
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--tnzi-base-text-muted);
}

.t-thread__count {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
}

.t-thread__error {
  font-size: 12px;
}

.t-thread__list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.t-thread__item {
  display: flex;
  gap: 8px;
  min-width: 0;
}

.t-thread__avatar {
  flex-shrink: 0;
  margin-top: 1px;
}

.t-thread__body {
  flex: 1 1 auto;
  min-width: 0;
}

.t-thread__meta {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.t-thread__author {
  font-size: 13px;
  font-weight: 600;
}

.t-thread__time {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}

/* Revealed on hover so a thread does not read as a row of close buttons. */
.t-thread__delete {
  margin-left: auto;
  opacity: 0;
  transition: opacity 0.15s ease;
}

.t-thread__item:hover .t-thread__delete,
.t-thread__delete:focus-visible {
  opacity: 1;
}

.t-thread__text {
  margin: 2px 0 0;
  font-size: 13px;
  line-height: 1.5;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.t-thread__composer {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.t-thread__composer-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 10px;
}

.t-thread__hint {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
</style>
