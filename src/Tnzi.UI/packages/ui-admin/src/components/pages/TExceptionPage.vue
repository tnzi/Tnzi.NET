<script setup lang="ts">
import { computed } from 'vue'
import { NButton } from 'naive-ui'
import TSvgIcon from '../display/TSvgIcon.vue'

/**
 * Reusable exception page — used by 403, 404, 500 routes and any
 * custom error state. Layout is a centered illustration + heading +
 * subtitle + primary CTA, with subtle background blob for visual interest.
 *
 * Inspired by soybean-admin's `ExceptionBase` pattern but with our
 * Iconify-backed illustration system (no SVG file shipping).
 */
type ExceptionCode = '403' | '404' | '500' | 'offline'

interface Props {
  /** Preset error type — drives illustration + default copy. */
  type?: ExceptionCode
  /** Custom title; falls back to type-default. */
  title?: string
  /** Custom subtitle; falls back to type-default. */
  subtitle?: string
  /** Custom Iconify icon name; falls back to type-default. */
  icon?: string
  /** Primary CTA label. */
  primaryLabel?: string
  /** Secondary CTA label (optional). */
  secondaryLabel?: string
}

const props = withDefaults(defineProps<Props>(), {
  type: '404',
  title: '',
  subtitle: '',
  icon: '',
  primaryLabel: 'Back to home',
  secondaryLabel: '',
})

const emit = defineEmits<{
  primary: []
  secondary: []
}>()

const PRESET: Record<
  ExceptionCode,
  { icon: string; title: string; subtitle: string }
> = {
  '403': {
    icon: 'mdi:shield-lock-outline',
    title: '403',
    subtitle: 'You don’t have permission to access this resource.',
  },
  '404': {
    icon: 'mdi:compass-off-outline',
    title: '404',
    subtitle: 'The page you’re looking for doesn’t exist or has moved.',
  },
  '500': {
    icon: 'mdi:alert-circle-outline',
    title: '500',
    subtitle: 'Something went wrong on our end. Please try again later.',
  },
  offline: {
    icon: 'mdi:wifi-off',
    title: 'Offline',
    subtitle: 'Your network looks unavailable. Reconnect and retry.',
  },
}

const resolved = computed(() => {
  const preset = PRESET[props.type]
  return {
    icon: props.icon || preset.icon,
    title: props.title || preset.title,
    subtitle: props.subtitle || preset.subtitle,
  }
})
</script>

<template>
  <div class="t-exception-page">
    <div class="t-exception-page__blob t-exception-page__blob--a" />
    <div class="t-exception-page__blob t-exception-page__blob--b" />

    <div class="t-exception-page__card">
      <TSvgIcon
        class="t-exception-page__icon"
        :icon="resolved.icon"
        :size="120"
      />
      <h1 class="t-exception-page__title">{{ resolved.title }}</h1>
      <p class="t-exception-page__subtitle">{{ resolved.subtitle }}</p>
      <div class="t-exception-page__actions">
        <NButton type="primary" @click="emit('primary')">
          {{ primaryLabel }}
        </NButton>
        <NButton
          v-if="secondaryLabel"
          quaternary
          @click="emit('secondary')"
        >
          {{ secondaryLabel }}
        </NButton>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-exception-page {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  min-height: 100%;
  padding: 32px;
  overflow: hidden;
  background-color: var(--tnzi-layout-bg);
}

.t-exception-page__blob {
  position: absolute;
  border-radius: 50%;
  filter: blur(64px);
  pointer-events: none;
}
.t-exception-page__blob--a {
  width: 480px;
  height: 480px;
  top: -180px;
  left: -160px;
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.18);
}
.t-exception-page__blob--b {
  width: 360px;
  height: 360px;
  bottom: -140px;
  right: -120px;
  background: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.16);
}

.t-exception-page__card {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  max-width: 520px;
  padding: 48px 40px;
  background-color: var(--tnzi-container-bg);
  border-radius: var(--tnzi-admin-radius-lg, 12px);
  box-shadow: var(--tnzi-shadow-drawer, 0 8px 24px rgb(0 0 0 / 12%));
  animation: t-exception-rise 0.4s var(--tnzi-admin-motion-ease-out, ease-out);
}

@keyframes t-exception-rise {
  from {
    opacity: 0;
    transform: translateY(12px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.t-exception-page__icon {
  color: var(--tnzi-primary);
  margin-bottom: 16px;
}

.t-exception-page__title {
  margin: 0 0 8px;
  font-size: 56px;
  line-height: 1;
  font-weight: 700;
  color: var(--tnzi-base-text);
  letter-spacing: -0.02em;
}

.t-exception-page__subtitle {
  margin: 0 0 32px;
  font-size: 15px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted);
  max-width: 380px;
}

.t-exception-page__actions {
  display: flex;
  gap: 12px;
  align-items: center;
}

@media (max-width: 640px) {
  .t-exception-page__card {
    padding: 32px 24px;
  }
  .t-exception-page__title {
    font-size: 44px;
  }
}
</style>
