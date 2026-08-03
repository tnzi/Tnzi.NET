<template>
  <!--
    One activity/comment timeline item: author avatar + name + timestamp, a
    body (default slot or `content`), and an optional `#actions` strip. Pairs
    with `TActivityFeed`. Pure props-driven; the caller resolves the avatar URL.
  -->
  <div class="t-note-card">
    <slot name="avatar">
      <TAvatar :src="avatarSrc" :name="author" :seed="authorSeed ?? author" :size="size" class="t-note-card__avatar" />
    </slot>
    <div class="t-note-card__body" :class="bodyClass">
      <div class="t-note-card__head">
        <slot name="header">
          <span class="t-note-card__author">{{ author }}</span>
          <!-- 作者与时间之间：职位、来源 chip、可见性标记这类随记录走的元信息。 -->
          <slot name="meta" />
          <span v-if="time" class="t-note-card__time">{{ time }}</span>
        </slot>
        <div v-if="$slots.actions" class="t-note-card__actions"><slot name="actions" /></div>
      </div>
      <div class="t-note-card__content"><slot>{{ content }}</slot></div>
      <div v-if="$slots.footer" class="t-note-card__footer"><slot name="footer" /></div>
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
    /**
     * Extra class(es) on the card body - the hook for per-record state
     * (billable, referenced, pinned, muted …).
     *
     * ★ It exists so a consumer never has to reach through `:deep()` into
     * `.t-note-card__body` to style a state. A private-selector coupling like
     * that breaks silently the day this component renames a class, and the
     * breakage shows up as "the highlight quietly stopped appearing".
     */
    bodyClass?: string | string[] | Record<string, boolean>
  }>(),
  { size: 32 },
)

defineSlots<{
  /** Card body. Falls back to `content`. */
  default?: () => unknown
  /** Replaces the avatar entirely (presence dots, group glyphs, badges). */
  avatar?: () => unknown
  /** Replaces the whole header line - the escape hatch when author+time is the wrong shape. */
  header?: () => unknown
  /** Appended inside the header, between author and time: job title, source chip, visibility. */
  meta?: () => unknown
  /** Right-aligned action strip in the header. */
  actions?: () => unknown
  /** Below the body: attachments, reactions, a billing roll-up. */
  footer?: () => unknown
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
.t-note-card__footer {
  margin-top: 6px;
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
</style>
