<template>
  <header class="t-admin-header" :class="{ 't-admin-header--fixed': fixed }">
    <!-- Left region: logo + toggler + breadcrumb -->
    <div class="t-admin-header__left">
      <div class="t-admin-header__logo">
        <slot name="logo">{{ title }}</slot>
      </div>
      <button
        v-if="showToggler"
        class="t-admin-header__toggler"
        :aria-label="appStore.siderCollapse ? 'Expand sider' : 'Collapse sider'"
        @click="appStore.toggleSiderCollapse()"
      >
        ☰
      </button>
      <div class="t-admin-header__breadcrumb">
        <slot name="breadcrumb" />
      </div>
    </div>

    <!-- Right region: action buttons + user/notif slots -->
    <div class="t-admin-header__right">
      <button
        v-if="showSearch"
        class="t-admin-header__search"
        aria-label="Search"
        @click="emit('openSearch')"
      >
        🔍
      </button>
      <button
        v-if="showReload"
        class="t-admin-header__reload"
        aria-label="Reload"
        @click="appStore.reloadPage()"
      >
        ↻
      </button>
      <button
        v-if="showFullscreen"
        class="t-admin-header__fullscreen"
        :aria-label="isFullscreen ? 'Exit fullscreen' : 'Enter fullscreen'"
        @click="toggleFullscreen()"
      >
        {{ isFullscreen ? '⤡' : '⤢' }}
      </button>
      <div v-if="showLangSwitch" class="t-admin-header__lang">
        <button
          aria-label="Language"
          @click="setLocale(appStore.locale === 'en' ? 'zh-cn' : 'en')"
        >
          {{ appStore.locale === 'en' ? 'EN' : '中' }}
        </button>
      </div>
      <button
        v-if="showThemeBtn"
        class="t-admin-header__theme"
        aria-label="Theme"
        @click="emit('openThemeDrawer')"
      >
        🎨
      </button>
      <div v-if="$slots.notification" class="t-admin-header__notification">
        <slot name="notification" />
      </div>
      <div v-if="$slots.user" class="t-admin-header__user">
        <slot name="user" />
      </div>
    </div>
  </header>
</template>

<script setup lang="ts">
import { useFullscreen } from '@vueuse/core'
import { useAdminAppStore } from '../../stores/useAdminAppStore'

interface Props {
  title?: string
  fixed?: boolean
  showToggler?: boolean
  showSearch?: boolean
  showFullscreen?: boolean
  showThemeBtn?: boolean
  showLangSwitch?: boolean
  showReload?: boolean
}

withDefaults(defineProps<Props>(), {
  title: 'Tnzi Admin',
  fixed: true,
  showToggler: true,
  showSearch: true,
  showFullscreen: true,
  showThemeBtn: true,
  showLangSwitch: true,
  showReload: true,
})

const emit = defineEmits<{
  openSearch: []
  openThemeDrawer: []
  localeChange: [locale: 'en' | 'zh-cn']
}>()

const appStore = useAdminAppStore()
const { isFullscreen, toggle: toggleFullscreen } = useFullscreen()

function setLocale(locale: 'en' | 'zh-cn') {
  appStore.setLocale(locale)
  emit('localeChange', locale)
}

defineExpose({ setLocale })
</script>

<style scoped>
.t-admin-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: var(--tnzi-admin-header-height, 56px);
  padding: 0 16px;
  background-color: var(--tnzi-header-bg, var(--tnzi-container-bg));
  border-bottom: 1px solid var(--tnzi-border-color);
  z-index: 10;
}
.t-admin-header--fixed {
  position: sticky;
  top: 0;
}
.t-admin-header__left {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}
.t-admin-header__logo {
  font-weight: 600;
  font-size: 16px;
  color: var(--tnzi-text-1);
  white-space: nowrap;
}
.t-admin-header__toggler,
.t-admin-header__search,
.t-admin-header__reload,
.t-admin-header__fullscreen,
.t-admin-header__theme {
  border: none;
  background: transparent;
  cursor: pointer;
  padding: 6px 8px;
  border-radius: 4px;
  font-size: 16px;
  color: var(--tnzi-text-2);
  transition: background-color 0.15s, color 0.15s;
}
.t-admin-header__toggler:hover,
.t-admin-header__search:hover,
.t-admin-header__reload:hover,
.t-admin-header__fullscreen:hover,
.t-admin-header__theme:hover {
  background-color: var(--tnzi-hover-bg);
  color: var(--tnzi-primary);
}
.t-admin-header__breadcrumb {
  margin-left: 12px;
  min-width: 0;
  overflow: hidden;
}
.t-admin-header__right {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}
.t-admin-header__lang button {
  border: none;
  background: transparent;
  cursor: pointer;
  padding: 6px 8px;
  border-radius: 4px;
  font-size: 13px;
  color: var(--tnzi-text-2);
}
.t-admin-header__lang button:hover {
  background-color: var(--tnzi-hover-bg);
  color: var(--tnzi-primary);
}
@media (max-width: 640px) {
  .t-admin-header__breadcrumb {
    display: none;
  }
}
</style>
