<template>
  <div class="t-admin-tabs">
    <div class="t-admin-tabs__list">
      <VueDraggable
        v-if="draggable"
        v-model="draggableList"
        class="t-admin-tabs__draggable"
        handle=".t-admin-tabs__tab"
        :animation="200"
      >
        <div
          v-for="tab in allTabs"
          :key="tab.id"
          class="t-admin-tabs__tab"
          :class="{
            't-admin-tabs__tab--active': tab.id === tabStore.activeTabId,
            't-admin-tabs__tab--home': tab.id === tabStore.homeTab?.id,
          }"
          @click="onTabClick(tab)"
          @mousedown="onMouseDown(tab, $event)"
          @contextmenu.prevent="onContextMenu(tab, $event)"
        >
          <span class="t-admin-tabs__title">{{
            props.translate ? props.translate(tab.title) : tab.title
          }}</span>
          <button
            v-if="tab.id !== tabStore.homeTab?.id"
            class="t-admin-tabs__close"
            aria-label="Close tab"
            @click.stop="tabStore.removeTab(tab.id)"
          >
            ×
          </button>
        </div>
      </VueDraggable>
      <div v-else class="t-admin-tabs__draggable">
        <div
          v-for="tab in allTabs"
          :key="tab.id"
          class="t-admin-tabs__tab"
          :class="{
            't-admin-tabs__tab--active': tab.id === tabStore.activeTabId,
            't-admin-tabs__tab--home': tab.id === tabStore.homeTab?.id,
          }"
          @click="onTabClick(tab)"
          @mousedown="onMouseDown(tab, $event)"
          @contextmenu.prevent="onContextMenu(tab, $event)"
        >
          <span class="t-admin-tabs__title">{{
            props.translate ? props.translate(tab.title) : tab.title
          }}</span>
          <button
            v-if="tab.id !== tabStore.homeTab?.id"
            class="t-admin-tabs__close"
            aria-label="Close tab"
            @click.stop="tabStore.removeTab(tab.id)"
          >
            ×
          </button>
        </div>
      </div>
    </div>

    <div class="t-admin-tabs__actions">
      <button
        v-if="showReload"
        class="t-admin-tabs__reload"
        aria-label="Reload"
        @click="appStore.reloadPage()"
      >
        ↻
      </button>
    </div>

    <NDropdown
      :options="contextOptions"
      :show="contextVisible"
      :x="contextX"
      :y="contextY"
      trigger="manual"
      placement="bottom-start"
      @select="onContextSelect"
      @clickoutside="contextVisible = false"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { NDropdown } from 'naive-ui'
import { VueDraggable } from 'vue-draggable-plus'
import { useAdminTabStore, type AdminTab } from '../../stores/useAdminTabStore'
import { useAdminAppStore } from '../../stores/useAdminAppStore'

interface Props {
  closeByMiddleClick?: boolean
  draggable?: boolean
  showReload?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  closeByMiddleClick: true,
  draggable: true,
  showReload: true,
  translate: undefined,
})

const emit = defineEmits<{
  tabClick: [tab: AdminTab]
}>()

const tabStore = useAdminTabStore()
const appStore = useAdminAppStore()

const allTabs = computed<AdminTab[]>(() => tabStore.allTabs)

// VueDraggable binds the mutable tab list (excludes homeTab, which is always first and pinned).
const draggableList = computed<AdminTab[]>({
  get: () => tabStore.tabs,
  set: (value) => {
    tabStore.tabs = value
  },
})

function onTabClick(tab: AdminTab): void {
  tabStore.switchRouteByTab(tab)
  emit('tabClick', tab)
}

function onMouseDown(tab: AdminTab, event: MouseEvent): void {
  // Middle mouse button = 1
  if (
    event.button === 1 &&
    props.closeByMiddleClick &&
    tab.id !== tabStore.homeTab?.id
  ) {
    event.preventDefault()
    tabStore.removeTab(tab.id)
  }
}

// Context menu state
const contextVisible = ref(false)
const contextX = ref(0)
const contextY = ref(0)
const contextTarget = ref<AdminTab | null>(null)

const contextOptions = computed(() => {
  const t = (k: string) => (props.translate ? props.translate(k) : k)
  return [
    { label: t('admin.tabs.closeCurrent'), key: 'close-current' },
    { label: t('admin.tabs.closeLeft'), key: 'close-left' },
    { label: t('admin.tabs.closeRight'), key: 'close-right' },
    { label: t('admin.tabs.closeOthers'), key: 'close-others' },
    { label: t('admin.tabs.closeAll'), key: 'close-all' },
  ]
})

function onContextMenu(tab: AdminTab, event: MouseEvent): void {
  contextTarget.value = tab
  contextX.value = event.clientX
  contextY.value = event.clientY
  contextVisible.value = true
}

function onContextSelect(key: string): void {
  const target = contextTarget.value
  if (!target) return
  switch (key) {
    case 'close-current':
      if (target.id !== tabStore.homeTab?.id) tabStore.removeTab(target.id)
      break
    case 'close-left':
      tabStore.removeLeftTabs(target.id)
      break
    case 'close-right':
      tabStore.removeRightTabs(target.id)
      break
    case 'close-others':
      tabStore.removeOtherTabs(target.id)
      break
    case 'close-all':
      tabStore.clearAllTabs()
      break
  }
  contextVisible.value = false
}

defineExpose({ contextTarget, contextVisible, onContextSelect })
</script>

<style scoped>
.t-admin-tabs {
  display: flex;
  align-items: center;
  height: var(--tnzi-admin-tab-height, 40px);
  background-color: var(--tnzi-tab-bg, var(--tnzi-container-bg));
  border-bottom: 1px solid var(--tnzi-border-color);
  padding: 0 8px;
}
.t-admin-tabs__list {
  flex: 1 1 auto;
  overflow-x: auto;
  overflow-y: hidden;
}
.t-admin-tabs__draggable {
  display: flex;
  align-items: center;
  gap: 4px;
  min-width: max-content;
}
.t-admin-tabs__tab {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  border-radius: 4px 4px 0 0;
  background-color: transparent;
  color: var(--tnzi-text-2);
  font-size: 13px;
  cursor: pointer;
  user-select: none;
  transition:
    background-color 0.15s ease,
    color 0.15s ease;
}
.t-admin-tabs__tab:hover {
  background-color: var(--tnzi-hover-bg);
}
.t-admin-tabs__tab--active {
  background-color: var(--tnzi-primary-bg);
  color: var(--tnzi-primary);
}
.t-admin-tabs__close {
  border: none;
  background: transparent;
  font-size: 14px;
  line-height: 1;
  color: inherit;
  cursor: pointer;
  padding: 2px 4px;
  border-radius: 2px;
}
.t-admin-tabs__close:hover {
  background-color: var(--tnzi-danger-bg);
  color: var(--tnzi-danger);
}
.t-admin-tabs__actions {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  padding: 0 4px;
}
.t-admin-tabs__reload {
  border: none;
  background: transparent;
  color: var(--tnzi-text-2);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}
.t-admin-tabs__reload:hover {
  background-color: var(--tnzi-hover-bg);
  color: var(--tnzi-primary);
}
</style>
