<script setup lang="ts">
/**
 * @experimental
 * TThreadMessage - one turn in a Manus-style thread.
 *
 * The package ships **two** message renderers because they are two different
 * layouts, not two skins:
 *
 *   - `TChatMessage` - classic transcript. Avatar on both sides, bubble on
 *     both sides. Reads as a conversation between two parties.
 *   - `TThreadMessage` (this one) - Manus/agent layout. The user gets a right
 *     aligned bubble with no avatar; the assistant gets a left byline row and
 *     **no bubble at all**, so its answer reads as document body rather than
 *     as chat. That asymmetry is the whole point: it gives long agent output
 *     the full column width.
 *
 * Extracted from TChatApp, which rendered it inline. Anyone building a custom
 * shell can now get the same thread visuals without adopting the whole
 * TChatApp.
 *
 * State stays outside: `copied` is a prop and the clipboard write happens in
 * the consumer, so a thread of N messages keeps one reset timer instead of N.
 */
import { Icon } from '@iconify/vue'
import TMessageResponse from './TMessageResponse.vue'
import TReasoningStage from '../reasoning/TReasoningStage.vue'
import { useAiI18n } from '../../i18n/index'
import type { ChatMessage } from '../../headless/useChat'

withDefaults(
  defineProps<{
    message: ChatMessage
    /** Shown when the message carries no `agentName` of its own. */
    agentName?: string
    /** Small tag after the agent name (e.g. "Pro", "Lite"). */
    agentLabel?: string
    /** Renders the copy button in its confirmed state. */
    copied?: boolean
    /** Render the action row under assistant turns. */
    showActions?: boolean
  }>(),
  {
    agentName: 'Assistant',
    agentLabel: '',
    copied: false,
    showActions: true,
  },
)

const emit = defineEmits<{
  copy: [messageId: string]
  regenerate: [messageId: string]
  feedback: [messageId: string, type: 'positive' | 'negative']
}>()

const t = useAiI18n()
</script>

<template>
  <div class="t-thread-message" :class="`t-thread-message--${message.role}`">
    <div
      v-if="message.role === 'user'"
      class="t-thread-message__bubble t-thread-message__bubble--user"
    >
      {{ message.content }}
    </div>

    <template v-else>
      <slot name="role" :message="message">
        <div class="t-thread-message__role">
          <span class="t-thread-message__brand" aria-hidden="true">
            <Icon icon="lucide:sparkles" />
          </span>
          <strong>{{ message.agentName || agentName }}</strong>
          <span v-if="agentLabel" class="t-thread-message__tag">{{ agentLabel }}</span>
          <span
            v-if="message.isStreaming"
            class="t-thread-message__streaming"
            :aria-label="t.chat.streaming"
          >●</span>
        </div>
      </slot>

      <TReasoningStage
        v-if="message.reasoning"
        :status="message.isStreaming ? 'running' : 'done'"
      >
        {{ message.reasoning }}
      </TReasoningStage>

      <div v-if="message.status === 'error'" class="t-thread-message__error">
        <Icon icon="lucide:circle-alert" class="t-thread-message__error-icon" />
        <span>{{ message.error || t.chat.errorGeneric }}</span>
        <button
          type="button"
          class="t-thread-message__error-retry"
          @click="emit('regenerate', message.id)"
        >
          <Icon icon="lucide:rotate-ccw" />
          {{ t.common.retry }}
        </button>
      </div>
      <template v-else>
        <TMessageResponse
          class="t-thread-message__body"
          :content="message.content"
          :streaming="message.isStreaming ?? false"
        />
        <div v-if="message.status === 'stopped'" class="t-thread-message__stopped">
          <span class="t-thread-message__stopped-mark" aria-hidden="true" />
          {{ t.chat.generationStopped }}
        </div>
      </template>

      <div
        v-if="showActions && !message.isStreaming"
        class="t-thread-message__actions"
      >
        <slot name="actions" :message="message" :copied="copied">
          <button
            type="button"
            class="t-thread-message__action"
            :aria-label="copied ? t.chat.copied : t.chat.copy"
            @click="emit('copy', message.id)"
          >
            <Icon :icon="copied ? 'lucide:check' : 'lucide:copy'" />
          </button>
          <button
            type="button"
            class="t-thread-message__action"
            :aria-label="t.chat.regenerate"
            @click="emit('regenerate', message.id)"
          >
            <Icon icon="lucide:refresh-ccw" />
          </button>
          <button
            type="button"
            class="t-thread-message__action"
            :aria-label="t.chat.like"
            @click="emit('feedback', message.id, 'positive')"
          >
            <Icon icon="lucide:thumbs-up" />
          </button>
          <button
            type="button"
            class="t-thread-message__action"
            :aria-label="t.chat.dislike"
            @click="emit('feedback', message.id, 'negative')"
          >
            <Icon icon="lucide:thumbs-down" />
          </button>
        </slot>
      </div>

      <slot name="after" :message="message" />
    </template>
  </div>
</template>

<style scoped>
.t-thread-message {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-thread-message--user { align-items: flex-end; }
.t-thread-message__bubble--user {
  padding: 10px 16px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 18px;
  max-width: 75%;
  font-size: 15px;
  line-height: 1.6;
  color: var(--tnzi-ai-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.t-thread-message__role {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.t-thread-message__role strong {
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.t-thread-message__brand {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 6px;
  background: var(--tnzi-ai-text);
  color: var(--tnzi-ai-bg);
  font-size: 13px;
  flex-shrink: 0;
}
.t-thread-message__tag {
  font-size: 10px;
  padding: 1px 6px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 4px;
  color: var(--tnzi-ai-text-tertiary);
  font-weight: 500;
  letter-spacing: 0.02em;
}
.t-thread-message__streaming {
  color: var(--tnzi-ai-accent);
  animation: t-thread-message-pulse 1s infinite;
}
@keyframes t-thread-message-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}
.t-thread-message__body {
  font-size: 15px;
  line-height: 1.6;
  word-break: break-word;
  color: var(--tnzi-ai-text);
}
.t-thread-message__error {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  border: 1px solid color-mix(in srgb, var(--tnzi-ai-danger) 28%, transparent);
  background: color-mix(in srgb, var(--tnzi-ai-danger) 6%, var(--tnzi-ai-surface));
  border-radius: 10px;
  color: var(--tnzi-ai-danger);
  font-size: 14px;
}
.t-thread-message__error-icon {
  flex-shrink: 0;
  font-size: 16px;
}
.t-thread-message__error-retry {
  margin-left: auto;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  border: none;
  background: none;
  color: var(--tnzi-ai-danger);
  font: inherit;
  font-size: 13px;
  cursor: pointer;
  padding: 2px 6px;
  border-radius: 6px;
}
.t-thread-message__error-retry:hover {
  background: color-mix(in srgb, var(--tnzi-ai-danger) 10%, transparent);
}
.t-thread-message__stopped {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 6px;
  font-size: 11.5px;
  color: var(--tnzi-ai-text-tertiary);
  user-select: none;
}
.t-thread-message__stopped-mark {
  width: 9px;
  height: 9px;
  border-radius: 2px;
  background: currentColor;
  flex-shrink: 0;
}

/* Action chips. Icon-only by default (a 30x30 circle); adding a label span
   next to the icon widens it to a pill via `--with-label`. */
.t-thread-message__actions {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: 8px;
}
.t-thread-message__action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  width: 30px;
  height: 30px;
  padding: 0;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  border-radius: 999px;
  font-family: inherit;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-thread-message__action:hover { background: var(--tnzi-ai-hover); }
.t-thread-message__action > .iconify {
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.t-thread-message__action--with-label {
  width: auto;
  padding: 0 12px;
}
</style>
