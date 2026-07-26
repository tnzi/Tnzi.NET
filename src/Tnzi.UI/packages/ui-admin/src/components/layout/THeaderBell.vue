<template>
  <!--
    Header notification bell - an unread `NBadge` over a bell trigger opening an
    `NPopover` dropdown: a titled panel, one `#item` scoped-slot row per entry, a
    load-more button, and an empty state. The reusable primitive apps otherwise
    reverse-engineer (the old pattern Teleported into the header's internal
    class because no bell existed). Mount it in the header via
    `defineAdminApp({ headerNotification })` (the `#header-notification` slot).
  -->
  <n-popover trigger="click" placement="bottom-end" :show-arrow="false" style="padding: 0" class="t-header-bell__pop">
    <template #trigger>
      <button type="button" class="t-header-bell__trigger" :aria-label="title">
        <n-badge :value="unreadCount" :max="99" :show="unreadCount > 0" processing>
          <TSvgIcon :icon="icon" class="t-header-bell__icon" />
        </n-badge>
      </button>
    </template>
    <div class="t-header-bell__panel">
      <div class="t-header-bell__head">
        <span class="t-header-bell__title">{{ title }}</span>
        <slot name="head-actions" />
      </div>
      <div class="t-header-bell__list">
        <template v-if="items.length">
          <div v-for="(item, i) in items" :key="keyOf(item, i)" class="t-header-bell__item">
            <slot name="item" :item="item" :index="i" />
          </div>
          <button
            v-if="hasMore"
            type="button"
            class="t-header-bell__more"
            :disabled="loading"
            @click="emit('load-more')"
          >
            {{ loading ? loadingText : moreText }}
          </button>
        </template>
        <div v-else class="t-header-bell__empty">
          <slot name="empty">{{ emptyText }}</slot>
        </div>
      </div>
    </div>
  </n-popover>
</template>

<script setup lang="ts" generic="T">
import { NBadge, NPopover } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

const props = withDefaults(
  defineProps<{
    /** Unread count - drives the badge (hidden at 0, capped at 99). */
    unreadCount?: number
    items: T[]
    itemKey?: (item: T, index: number) => string | number
    hasMore?: boolean
    loading?: boolean
    title?: string
    emptyText?: string
    moreText?: string
    loadingText?: string
    /** Bell icon. */
    icon?: string
  }>(),
  {
    unreadCount: 0,
    title: 'Notifications',
    emptyText: 'No notifications',
    moreText: 'Load more',
    loadingText: 'Loading…',
    icon: 'mdi:bell-outline',
  },
)

const emit = defineEmits<{ 'load-more': [] }>()

defineSlots<{
  item?: (props: { item: T; index: number }) => unknown
  'head-actions'?: () => unknown
  empty?: () => unknown
}>()

const keyOf = (item: T, index: number): string | number => props.itemKey?.(item, index) ?? index
</script>

<style scoped>
.t-header-bell__trigger {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border: none;
  background: transparent;
  border-radius: 8px;
  color: var(--tnzi-base-text-muted, currentColor);
  cursor: pointer;
}
.t-header-bell__trigger:hover {
  background: var(--tnzi-admin-menu-item-hover-bg, rgba(0, 0, 0, 0.05));
}
.t-header-bell__icon {
  font-size: 19px;
}
.t-header-bell__panel {
  width: 320px;
  max-width: 90vw;
}
.t-header-bell__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--tnzi-border, rgba(0, 0, 0, 0.08));
}
.t-header-bell__title {
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text, currentColor);
}
.t-header-bell__list {
  max-height: 60vh;
  overflow-y: auto;
}
.t-header-bell__item {
  padding: 0 4px;
}
.t-header-bell__more {
  display: block;
  width: 100%;
  padding: 10px;
  border: none;
  border-top: 1px solid var(--tnzi-border, rgba(0, 0, 0, 0.08));
  background: transparent;
  color: var(--tnzi-primary, #2080f0);
  font-size: 13px;
  cursor: pointer;
}
.t-header-bell__more:disabled {
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.4));
  cursor: default;
}
.t-header-bell__empty {
  padding: 28px 16px;
  text-align: center;
  font-size: 13px;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.45));
}
</style>
