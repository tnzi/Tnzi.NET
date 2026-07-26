<template>
  <!--
    One activity/comment timeline item: author avatar + name + timestamp, a
    body (default slot or `content`), and an optional `#actions` strip. Pairs
    with `TActivityFeed`. Pure props-driven; the caller resolves the avatar URL.
  -->
  <div class="t-note-card">
    <TAvatar :src="avatarSrc" :name="author" :seed="authorSeed ?? author" :size="size" class="t-note-card__avatar" />
    <div class="t-note-card__body">
      <div class="t-note-card__head">
        <span class="t-note-card__author">{{ author }}</span>
        <span v-if="time" class="t-note-card__time">{{ time }}</span>
        <div v-if="$slots.actions" class="t-note-card__actions"><slot name="actions" /></div>
      </div>
      <div class="t-note-card__content"><slot>{{ content }}</slot></div>
    </div>
  </div>
</template>

<script setup lang="ts">
import TAvatar from './TAvatar.vue'

withDefaults(
  defineProps<{
    author: string
    /** Avatar image URL; falls back to the author initial. */
    avatarSrc?: string | null
    /** Colour seed (e.g. a stable user id). Defaults to `author`. */
    authorSeed?: string | null
    /** Timestamp string (already formatted). */
    time?: string | null
    /** Body text - used when the default slot is empty. */
    content?: string | null
    /** Avatar size. Default 32. */
    size?: number
  }>(),
  { size: 32 },
)

defineSlots<{
  default?: () => unknown
  actions?: () => unknown
}>()
</script>

<style scoped>
.t-note-card {
  display: flex;
  gap: 10px;
  align-items: flex-start;
}
.t-note-card__avatar {
  flex-shrink: 0;
}
.t-note-card__body {
  flex: 1;
  min-width: 0;
}
.t-note-card__head {
  display: flex;
  align-items: baseline;
  gap: 8px;
  margin-bottom: 2px;
}
.t-note-card__author {
  font-size: 13px;
  font-weight: 600;
  color: var(--tnzi-base-text, currentColor);
}
.t-note-card__time {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.45));
}
.t-note-card__actions {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 4px;
}
.t-note-card__content {
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.7));
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
