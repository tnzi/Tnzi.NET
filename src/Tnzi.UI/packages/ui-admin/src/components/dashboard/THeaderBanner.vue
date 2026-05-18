<script setup lang="ts">
/**
 * `THeaderBanner` — soybean-style dashboard welcome banner: greeting +
 * username + live datetime on the left, optional decorative SVG / image
 * slot on the right.
 *
 * The greeting auto-switches by hour (`good morning` 5-11, `good
 * afternoon` 12-17, `good evening` 18-22, `working late` 23-4) so
 * consumers don't have to wire that themselves. Pass `greeting` to
 * override or `translate` to localize.
 */
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

interface Props {
  /** Display name to show after the greeting. */
  userName?: string
  /** Static greeting; when omitted falls back to the time-of-day default. */
  greeting?: string
  /** Subtitle / motto under the greeting. */
  subtitle?: string
  /** Hide the live datetime ticker. */
  hideTime?: boolean
  /** Datetime format (Intl.DateTimeFormat options friendly). */
  locale?: string
  /** Translation function for `admin.banner.greeting.{morning|afternoon|evening|night}`. */
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  userName: '',
  greeting: '',
  subtitle: '',
  hideTime: false,
  locale: 'en',
  translate: undefined,
})

const now = ref(new Date())
let timerId: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  timerId = setInterval(() => {
    now.value = new Date()
  }, 1000)
})
onBeforeUnmount(() => {
  if (timerId) clearInterval(timerId)
})

const defaultGreeting = computed(() => {
  const h = now.value.getHours()
  const key =
    h >= 5 && h < 12
      ? 'morning'
      : h >= 12 && h < 18
        ? 'afternoon'
        : h >= 18 && h < 23
          ? 'evening'
          : 'night'
  const fallback: Record<string, string> = {
    morning: 'Good morning',
    afternoon: 'Good afternoon',
    evening: 'Good evening',
    night: 'Working late',
  }
  return props.translate
    ? props.translate(`admin.banner.greeting.${key}`)
    : fallback[key]
})

const displayGreeting = computed(() => props.greeting || defaultGreeting.value)

const formattedTime = computed(() => {
  try {
    return new Intl.DateTimeFormat(props.locale, {
      weekday: 'long',
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    }).format(now.value)
  } catch {
    return now.value.toLocaleString()
  }
})
</script>

<template>
  <div class="t-header-banner">
    <div class="t-header-banner__main">
      <h2 class="t-header-banner__greeting">
        {{ displayGreeting }}<template v-if="userName">, {{ userName }}</template>
      </h2>
      <p v-if="subtitle" class="t-header-banner__subtitle">{{ subtitle }}</p>
      <p v-if="!hideTime" class="t-header-banner__time">{{ formattedTime }}</p>
    </div>
    <div v-if="$slots.illustration" class="t-header-banner__illustration">
      <slot name="illustration" />
    </div>
  </div>
</template>

<style scoped>
.t-header-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  /* soybean parity: banner spans the full content width like soybean's
     home-page hero card — flex default `width: auto` was leaving it
     stuck at content width inside a `flex-direction: column` parent. */
  width: 100%;
  padding: 20px 24px;
  border-radius: var(--tnzi-admin-radius-lg, 12px);
  background: linear-gradient(
    135deg,
    rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08) 0%,
    rgb(var(--tnzi-info-rgb, 32 128 240) / 0.04) 100%
  );
  border: 1px solid rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.12);
}
.t-header-banner__main {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}
.t-header-banner__greeting {
  margin: 0;
  font-size: 22px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-header-banner__subtitle {
  margin: 0;
  font-size: 14px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-header-banner__time {
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  font-variant-numeric: tabular-nums;
}
.t-header-banner__illustration {
  flex-shrink: 0;
}
@media (max-width: 640px) {
  .t-header-banner {
    padding: 16px 16px;
    gap: 12px;
  }
  .t-header-banner__illustration {
    display: none;
  }
  .t-header-banner__greeting {
    font-size: 18px;
  }
  .t-header-banner__subtitle {
    font-size: 13px;
  }
  .t-header-banner__time {
    font-size: 11px;
  }
}
</style>
