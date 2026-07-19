<template>
  <!--
    Vertical activity / comment feed: an optional composer at the top, then one
    row per item (rendered through the `#item` scoped slot, typically a
    `TNoteCard`) joined by a connector line, with an empty state. Generic — the
    caller owns the item shape and rendering.
  -->
  <div class="t-activity-feed">
    <div v-if="$slots.composer" class="t-activity-feed__composer">
      <slot name="composer" />
    </div>
    <div v-if="items.length" class="t-activity-feed__list" :class="{ 't-activity-feed__list--connected': connector }">
      <div v-for="(item, i) in items" :key="keyOf(item, i)" class="t-activity-feed__item">
        <slot name="item" :item="item" :index="i" />
      </div>
    </div>
    <div v-else class="t-activity-feed__empty">
      <slot name="empty">{{ emptyText }}</slot>
    </div>
  </div>
</template>

<script setup lang="ts" generic="T">
const props = withDefaults(
  defineProps<{
    items: T[]
    /** Stable key per item. Default: the array index. */
    itemKey?: (item: T, index: number) => string | number
    /** Draw the vertical connector line between items. Default true. */
    connector?: boolean
    emptyText?: string
  }>(),
  { connector: true, emptyText: 'No activity yet' },
)

defineSlots<{
  item?: (props: { item: T; index: number }) => unknown
  composer?: () => unknown
  empty?: () => unknown
}>()

const keyOf = (item: T, index: number): string | number => props.itemKey?.(item, index) ?? index
</script>

<style scoped>
.t-activity-feed {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.t-activity-feed__composer {
  padding-bottom: 4px;
}
.t-activity-feed__list {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.t-activity-feed__item {
  position: relative;
}
/* Connector line: a subtle rail down the left, aligned under a ~32px avatar. */
.t-activity-feed__list--connected .t-activity-feed__item:not(:last-child)::before {
  content: '';
  position: absolute;
  left: 15px;
  top: 34px;
  bottom: -16px;
  width: 1px;
  background: var(--tnzi-border, rgba(0, 0, 0, 0.08));
}
.t-activity-feed__empty {
  padding: 24px 0;
  text-align: center;
  font-size: 13px;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.45));
}
</style>
