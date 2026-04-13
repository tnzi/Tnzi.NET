<template>
  <header :style="headerStyle" class="t-site-header">
    <div class="t-site-header__inner">
      <div class="t-site-header__logo">
        <slot name="logo" />
      </div>
      <div v-if="$slots.nav" class="t-site-header__nav">
        <slot name="nav" />
      </div>
      <div class="t-site-header__spacer" />
      <div v-if="$slots.actions" class="t-site-header__actions">
        <slot name="actions" />
      </div>
      <button
        v-if="showHamburger"
        type="button"
        class="t-site-header__hamburger"
        :aria-label="hamburgerLabel"
        @click="emit('hamburger-click')"
      >
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true" focusable="false">
          <line x1="3" y1="6" x2="21" y2="6" />
          <line x1="3" y1="12" x2="21" y2="12" />
          <line x1="3" y1="18" x2="21" y2="18" />
        </svg>
      </button>
    </div>
  </header>
</template>

<script setup lang="ts">
import { computed, type CSSProperties } from 'vue'

type HeaderVariant = 'solid' | 'transparent' | 'blur'

interface Props {
  variant?: HeaderVariant
  sticky?: boolean
  height?: string
  showHamburger?: boolean
  hamburgerLabel?: string
}

const props = withDefaults(defineProps<Props>(), {
  variant: 'solid',
  sticky: false,
  height: '64px',
  showHamburger: false,
  hamburgerLabel: 'Open menu',
})

const emit = defineEmits<{
  'hamburger-click': []
}>()

const headerStyle = computed<CSSProperties>(() => {
  const background =
    props.variant === 'transparent'
      ? 'transparent'
      : props.variant === 'blur'
      ? 'var(--tnzi-header-blur-bg)'
      : 'var(--tnzi-container-bg)'
  return {
    position: props.sticky ? 'sticky' : 'relative',
    top: props.sticky ? '0' : undefined,
    zIndex: 100,
    height: props.height,
    backgroundColor: background,
    backdropFilter: props.variant === 'blur' ? 'blur(12px)' : undefined,
    boxShadow: props.variant === 'solid' ? 'var(--tnzi-shadow-header)' : undefined,
  }
})
</script>

<style scoped>
.t-site-header {
  width: 100%;
}
.t-site-header__inner {
  display: flex;
  align-items: center;
  gap: 24px;
  height: 100%;
  max-width: 1280px;
  margin: 0 auto;
  padding: 0 clamp(16px, 4vw, 32px);
}
.t-site-header__logo {
  flex-shrink: 0;
}
.t-site-header__nav {
  flex: 0 1 auto;
}
.t-site-header__spacer {
  flex: 1 1 auto;
}
.t-site-header__actions {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 12px;
}
.t-site-header__hamburger {
  display: none;
  background: transparent;
  border: none;
  color: var(--tnzi-base-text);
  padding: 8px;
  cursor: pointer;
  border-radius: 6px;
}
.t-site-header__hamburger:hover {
  background: var(--tnzi-primary-50);
}
@media (max-width: 768px) {
  .t-site-header__nav {
    display: none;
  }
  .t-site-header__hamburger {
    display: flex;
  }
}
</style>
