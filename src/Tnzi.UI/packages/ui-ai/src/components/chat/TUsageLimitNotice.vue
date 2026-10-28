<script setup lang="ts">
/**
 * @experimental
 * TUsageLimitNotice - says that a usage limit was reached (or is close), and
 * when it resets, in the reader's language.
 *
 * One component for every place a limit shows up, so they cannot drift apart:
 *   - `variant="banner"`: above the composer text box (`TChatApp` puts it
 *     there when a turn was refused, when the usage figures say a window is at
 *     its limit, and as a quieter `tone="warning"` hint when one is close);
 *   - `variant="inline"`: on a reply that could not be given (`TThreadMessage`).
 *
 * `scope="account"` is a different fact: the external CLI's own subscription
 * reached its limit, not one this deployment set for the user. It keeps its
 * own wording.
 *
 * It is a notice, not an error: the reset time is the point. The countdown is
 * re-read every 30 seconds while it is on screen, and the notice emits
 * `expired` once the reset has passed so its owner can drop it.
 */
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { UsageLimitWindow } from '@tnzi/core/services/ai'
import { useAiI18n, formatAiMessage } from '../../i18n/index'
import { formatResetTime } from '../../utils/format'
import {
  formatLimitReached,
  formatResetLabel,
  formatResetSentence,
  isResetAhead,
  wholePercent,
} from '../../headless/usage-limits'

const props = withDefaults(
  defineProps<{
    /** The window at (or near) its limit; null when not known. */
    window?: UsageLimitWindow | null
    /** When it resets (ISO, UTC); null when it does not reset on its own. */
    resetsAt?: string | null
    /** `limit`: at the limit, sending is refused. `warning`: past the warning threshold. */
    tone?: 'limit' | 'warning'
    /** With `tone="warning"`: how much of the window is used (0-100). */
    usedPercent?: number | null
    /** `user`: a limit this deployment set. `account`: the external CLI's own subscription. */
    scope?: 'user' | 'account'
    variant?: 'banner' | 'inline'
    /** Show a dismiss button (emits `dismiss`). */
    dismissible?: boolean
    /** Add "your message was not sent and is still in the box". */
    draftKept?: boolean
  }>(),
  {
    window: null,
    resetsAt: null,
    tone: 'limit',
    usedPercent: null,
    scope: 'user',
    variant: 'banner',
    dismissible: false,
    draftKept: false,
  },
)

const emit = defineEmits<{
  dismiss: []
  /** The reset time passed while the notice was on screen. */
  expired: []
}>()

const t = useAiI18n()

const now = ref(new Date())
let timer: ReturnType<typeof setInterval> | null = null
onMounted(() => {
  timer = setInterval(() => { now.value = new Date() }, 30_000)
})
onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})

watch(
  () => isResetAhead(props.resetsAt, now.value),
  (ahead) => {
    if (!ahead) emit('expired')
  },
)

const texts = computed(() => t.value.usageLimits)

const headline = computed(() => {
  if (props.scope === 'account') return t.value.chat.usageLimit
  if (props.tone === 'warning') {
    const percent = wholePercent(props.usedPercent)
    const template = props.window === UsageLimitWindow.Weekly ? texts.value.warningWeekly : texts.value.warningSession
    return formatAiMessage(template, { percent })
  }
  return formatLimitReached(props.window, texts.value)
})

const detail = computed(() => {
  if (props.scope === 'account') {
    const time = formatResetTime(props.resetsAt, now.value, texts.value.dateLocale || undefined)
    return time ? formatAiMessage(t.value.chat.usageLimitResets, { time }) : null
  }
  if (props.tone === 'warning') return formatResetLabel(props.resetsAt, texts.value, now.value)
  return formatResetSentence(props.resetsAt, texts.value, now.value)
})

const icon = computed(() => (props.tone === 'warning' ? 'lucide:gauge' : 'lucide:hourglass'))
</script>

<template>
  <div
    class="t-usage-limit-notice"
    :class="[`t-usage-limit-notice--${variant}`, `t-usage-limit-notice--${tone}`]"
    :role="tone === 'limit' ? 'alert' : 'status'"
  >
    <Icon :icon="icon" class="t-usage-limit-notice__icon" />
    <span class="t-usage-limit-notice__text">
      <span class="t-usage-limit-notice__headline">{{ headline }}</span>
      <template v-if="detail"> <span class="t-usage-limit-notice__detail">{{ detail }}</span></template>
      <template v-if="draftKept"> <span class="t-usage-limit-notice__detail">{{ texts.draftKept }}</span></template>
    </span>
    <button
      v-if="dismissible"
      type="button"
      class="t-usage-limit-notice__dismiss"
      :aria-label="texts.dismiss"
      @click="emit('dismiss')"
    >
      <Icon icon="lucide:x" />
    </button>
  </div>
</template>

<style scoped>
.t-usage-limit-notice {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  color: var(--tnzi-ai-text);
  line-height: 1.5;
}
.t-usage-limit-notice__icon {
  flex-shrink: 0;
  margin-top: 2px;
  color: var(--tnzi-ai-text-secondary);
}
.t-usage-limit-notice__text {
  flex: 1;
  min-width: 0;
}
.t-usage-limit-notice__headline {
  font-weight: 500;
}
.t-usage-limit-notice__detail {
  color: var(--tnzi-ai-text-secondary);
}

/* Above the composer's text box: the same frame as the composer's own
   "not attached" row, so the two read as one family. */
.t-usage-limit-notice--banner {
  margin: 0 14px 8px;
  padding: 6px 6px 6px 10px;
  border-radius: 8px;
  font-size: 12px;
}
.t-usage-limit-notice--banner.t-usage-limit-notice--limit {
  border: 1px solid color-mix(in srgb, var(--tnzi-ai-warning) 32%, transparent);
  background: color-mix(in srgb, var(--tnzi-ai-warning) 10%, transparent);
}
.t-usage-limit-notice--banner.t-usage-limit-notice--limit .t-usage-limit-notice__icon {
  color: var(--tnzi-ai-warning);
}
.t-usage-limit-notice--banner.t-usage-limit-notice--warning {
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-hover);
}
.t-usage-limit-notice--banner .t-usage-limit-notice__icon {
  font-size: 14px;
}

/* On a reply that could not be given. */
.t-usage-limit-notice--inline {
  padding: 10px 12px;
  border-radius: 10px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  font-size: 14px;
}

.t-usage-limit-notice__dismiss {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 20px;
  height: 20px;
  padding: 0;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  cursor: pointer;
}
.t-usage-limit-notice__dismiss:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
</style>
