<template>
  <header class="t-record-header" :class="{ 't-record-header--compact': compact }">
    <div class="t-record-header__identity">
      <slot name="avatar">
        <TAvatar
          v-if="avatar || name"
          :src="avatar"
          :name="avatarName ?? name"
          :icon="icon"
          :size="compact ? 40 : 52"
          shape="rounded"
          :prefer-icon="!!icon && !avatar"
        />
      </slot>

      <div class="t-record-header__body">
        <div class="t-record-header__title-row">
          <slot name="title">
            <h2 class="t-record-header__title" :title="name">{{ name }}</h2>
          </slot>
          <slot name="badges">
            <NTag
              v-for="(badge, i) in badges"
              :key="i"
              size="small"
              round
              :bordered="false"
              :type="badge.type ?? 'default'"
            >
              {{ badge.label }}
            </NTag>
          </slot>
        </div>

        <p v-if="subtitle" class="t-record-header__subtitle">{{ subtitle }}</p>

        <slot name="facts">
          <div v-if="facts.length" class="t-record-header__facts">
            <span v-for="(fact, i) in facts" :key="i" class="t-record-header__fact">
              <TSvgIcon v-if="fact.icon" :icon="fact.icon" :size="14" />
              <span v-if="fact.label" class="t-record-header__fact-label">{{ fact.label }}</span>
              <span class="t-record-header__fact-value">{{ fact.value || EMPTY_DASH }}</span>
            </span>
          </div>
        </slot>
      </div>
    </div>

    <div v-if="$slots.actions" class="t-record-header__actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<script setup lang="ts">
/**
 * TRecordHeader - the identity band at the top of a record surface.
 *
 * Answers "what am I looking at?" before the reader touches any field: the
 * record's face (avatar or tinted glyph), its name, its status, and the three
 * or four facts that identify it (code, owner, created, balance). Everything
 * below it is then detail rather than discovery.
 *
 * Use it at the top of a `TDetailLayout` panel, inside a view drawer, or above
 * a section stack. It is presentation-only - no data loading, no routing.
 *
 * (Doc comment in the script, not above the root element: a leading comment
 * node in `<template>` makes the component multi-root and breaks fallthrough.)
 */
import { NTag } from 'naive-ui'
import { TAvatar, TSvgIcon } from '@tnzi/ui'
import { EMPTY_DASH } from '../../utils/placeholders'

export interface RecordBadge {
  label: string
  type?: 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error'
}

export interface RecordFact {
  /** Iconify glyph before the fact. */
  icon?: string
  /** Short caption ("Owner", "Created"). Omit for a self-evident value. */
  label?: string
  /** Pre-formatted value. Blank renders the shared dash. */
  value?: string
}

interface Props {
  /** The record's name - the biggest text on the surface. */
  name: string
  /** One muted line under the name (type, category, tagline). */
  subtitle?: string
  /** Image URL for the face tile. */
  avatar?: string
  /** Iconify glyph used when there is no image. */
  icon?: string
  /** Name used to derive the initial when there is no image (defaults to `name`). */
  avatarName?: string
  /** Status chips beside the name. */
  badges?: RecordBadge[]
  /** The handful of identifying facts under the name. */
  facts?: RecordFact[]
  /** Tighter band for drawers and nested panels. */
  compact?: boolean
}

withDefaults(defineProps<Props>(), {
  subtitle: undefined,
  avatar: undefined,
  icon: undefined,
  avatarName: undefined,
  badges: () => [],
  facts: () => [],
  compact: false,
})

defineSlots<{
  /** Replaces the avatar tile. */
  avatar?: () => unknown
  /** Replaces the name (keeps the badge row). */
  title?: () => unknown
  /** Replaces the badge row. */
  badges?: () => unknown
  /** Replaces the fact row. */
  facts?: () => unknown
  /** Right-aligned record operations. */
  actions?: () => unknown
}>()
</script>

<style scoped>
.t-record-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--tnzi-border);
}
.t-record-header--compact {
  padding-bottom: 12px;
}
.t-record-header__identity {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  min-width: 0;
  flex: 1 1 auto;
}
.t-record-header__body {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 5px;
}
.t-record-header__title-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  min-width: 0;
}
.t-record-header__title {
  margin: 0;
  font-size: 19px;
  font-weight: 700;
  line-height: 1.25;
  color: var(--tnzi-base-text);
  min-width: 0;
  max-width: 100%;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-record-header--compact .t-record-header__title {
  font-size: 16px;
}
.t-record-header__subtitle {
  margin: 0;
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted);
}
.t-record-header__facts {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 20px;
  margin-top: 2px;
}
.t-record-header__fact {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted);
  min-width: 0;
}
.t-record-header__fact-label::after {
  content: ':';
}
.t-record-header__fact-value {
  color: var(--tnzi-base-text);
  font-weight: 500;
  overflow-wrap: anywhere;
}
.t-record-header__actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
  flex-wrap: wrap;
  justify-content: flex-end;
}

/* Phones: actions drop to their own row under the identity block so a long
   name is never squeezed by a button group. */
@media (max-width: 640px) {
  .t-record-header {
    flex-direction: column;
    align-items: stretch;
  }
  .t-record-header__actions {
    justify-content: flex-start;
  }
  .t-record-header__title {
    white-space: normal;
  }
}
</style>
