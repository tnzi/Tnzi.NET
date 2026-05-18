<script setup lang="ts">
/**
 * `TProjectTimeline` — naive-ui-NTimeline wrapper for activity feeds:
 * project news, recent operations, audit summaries. Each item resolves
 * a colored icon dot, title, optional subtitle/description, and timestamp.
 *
 * Items are presentational only — no fetching, no infinite scroll. Drop
 * into TDashboardPage's default slot or anywhere a feed makes sense.
 */
import { NTimeline, NTimelineItem } from 'naive-ui'
import TSvgIcon from '../display/TSvgIcon.vue'

export type TimelineTone = 'default' | 'info' | 'success' | 'warning' | 'error'

export interface TimelineItem {
  /** Stable key for list rendering. */
  key: string
  /** Primary line (e.g. user name + action verb). */
  title: string
  /** Optional secondary description. */
  description?: string
  /** Optional timestamp (formatted by the consumer). */
  time?: string
  /** Iconify icon name. */
  icon?: string
  /** Coloring tone — drives the timeline dot color. */
  tone?: TimelineTone
}

interface Props {
  items?: TimelineItem[]
  /** Translation function for the empty state label. */
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  items: () => [],
  translate: undefined,
})

function emptyLabel(): string {
  return props.translate
    ? props.translate('admin.timeline.empty')
    : 'No recent activity'
}
</script>

<template>
  <div class="t-project-timeline">
    <NTimeline v-if="items.length">
      <NTimelineItem
        v-for="item in items"
        :key="item.key"
        :type="item.tone ?? 'default'"
        :title="item.title"
        :content="item.description"
        :time="item.time"
      >
        <template v-if="item.icon" #icon>
          <TSvgIcon :icon="item.icon" :size="14" />
        </template>
      </NTimelineItem>
    </NTimeline>
    <div v-else class="t-project-timeline__empty">{{ emptyLabel() }}</div>
  </div>
</template>

<style scoped>
.t-project-timeline {
  width: 100%;
}
.t-project-timeline__empty {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 32px 0;
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
}
</style>
