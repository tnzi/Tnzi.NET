<script setup lang="ts">
/**
 * @experimental
 * TLandingPage — empty-state hero for chat applications.
 *
 * Renders a serif display headline above a chat composer and a row of
 * suggestion chips, matching the "New task" idiom used by contemporary
 * agent products. All content is slot-driven so consumers can tailor
 * every region without forking the component:
 *
 *   <TLandingPage
 *     v-model="draft"
 *     :greeting="$t('chat.greeting')"
 *     :chips="chipList"
 *     @submit="send"
 *   >
 *     <template #plan>
 *       <MyPlanBadge />
 *     </template>
 *     <template #composer-left>
 *       <button><i-lucide-paperclip /></button>
 *     </template>
 *   </TLandingPage>
 *
 * Slots:
 *   - plan       ........ content above the headline (plan badge, trial pill)
 *   - headline   ........ overrides the default serif greeting
 *   - subline    ........ optional subtitle beneath the headline
 *   - composer-left  .... left cluster of the composer toolbar
 *   - composer-right ..... right cluster of the composer toolbar
 *   - chips      ........ overrides the default chip row
 *   - footer     ........ free area below everything
 */
import { computed } from 'vue'
import { Icon } from '@iconify/vue'

export interface LandingChip {
  readonly id?: string
  readonly icon?: string
  readonly label: string
  readonly prompt?: string
}

const props = withDefaults(
  defineProps<{
    /** Composer text (two-way bound). */
    modelValue?: string
    /** Main serif greeting text. Ignored if the `headline` slot is used. */
    greeting?: string
    /** Composer placeholder. */
    placeholder?: string
    /** Suggestion chips. Ignored if the `chips` slot is used. */
    chips?: readonly LandingChip[]
    /** Composer max width in pixels. */
    maxWidth?: number
    /** Show the send button even when the input is empty. */
    alwaysShowSend?: boolean
  }>(),
  {
    modelValue: '',
    greeting: 'What can I do for you?',
    placeholder: 'Assign a task or ask anything',
    chips: () => [],
    maxWidth: 768,
    alwaysShowSend: false,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  submit: [value: string]
  'chip-click': [chip: LandingChip]
}>()

const text = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v),
})

const isReady = computed(() => text.value.trim().length > 0 || props.alwaysShowSend)

function onSubmit(): void {
  if (!isReady.value) return
  emit('submit', text.value)
}

function onKeydown(ev: KeyboardEvent): void {
  if (ev.key === 'Enter' && !ev.shiftKey && !ev.isComposing) {
    ev.preventDefault()
    onSubmit()
  }
}

function onChipClick(chip: LandingChip): void {
  emit('chip-click', chip)
  if (chip.prompt != null) {
    text.value = chip.prompt
  }
}
</script>

<template>
  <section class="t-landing">
    <div v-if="$slots.plan" class="t-landing__plan">
      <slot name="plan" />
    </div>

    <h1 class="t-landing__headline">
      <slot name="headline">{{ greeting }}</slot>
    </h1>

    <div v-if="$slots.subline" class="t-landing__subline">
      <slot name="subline" />
    </div>

    <div
      class="t-landing__composer"
      :style="{ maxWidth: `${maxWidth}px` }"
    >
      <textarea
        v-model="text"
        class="t-landing__input"
        rows="2"
        :placeholder="placeholder"
        @keydown="onKeydown"
      />
      <div class="t-landing__composer-bar">
        <div class="t-landing__composer-left">
          <slot name="composer-left" />
        </div>
        <div class="t-landing__composer-right">
          <slot name="composer-right" />
          <button
            type="button"
            class="t-landing__send"
            :class="{ 'is-ready': isReady }"
            :disabled="!isReady"
            aria-label="Send"
            @click="onSubmit"
          >
            <Icon icon="lucide:arrow-up" />
          </button>
        </div>
      </div>
    </div>

    <div v-if="$slots.chips || (chips && chips.length > 0)" class="t-landing__chips">
      <slot name="chips">
        <button
          v-for="(chip, i) in chips"
          :key="chip.id ?? chip.label ?? i"
          type="button"
          class="t-landing__chip"
          @click="onChipClick(chip)"
        >
          <Icon v-if="chip.icon" :icon="chip.icon" />
          <span>{{ chip.label }}</span>
        </button>
      </slot>
    </div>

    <div v-if="$slots.footer" class="t-landing__footer">
      <slot name="footer" />
    </div>
  </section>
</template>

<style scoped>
.t-landing {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px 24px 80px;
  min-height: 0;
  overflow-y: auto;
}

.t-landing__plan {
  display: inline-flex;
  align-items: center;
  margin-bottom: 22px;
}

.t-landing__headline {
  margin: 0 0 28px;
  padding: 0;
  font-family: var(--tnzi-ai-font-display, 'Libre Baskerville', Georgia, serif);
  font-size: 36px;
  line-height: 1.5;
  font-weight: 400;
  color: var(--tnzi-ai-text, #34322d);
  text-align: center;
  letter-spacing: normal;
}

.t-landing__subline {
  font-size: 14px;
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  margin: -18px 0 28px;
  text-align: center;
}

.t-landing__composer {
  width: 100%;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border-strong, rgba(0, 0, 0, 0.2));
  border-radius: var(--tnzi-ai-composer-radius, 22px);
  box-shadow: var(--tnzi-ai-composer-shadow, 0 12px 32px rgba(0, 0, 0, 0.02));
  padding: 14px 4px 10px;
  transition: border-color 120ms var(--tnzi-ai-easing, ease);
}
.t-landing__composer:focus-within {
  border-color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
}

.t-landing__input {
  width: 100%;
  border: none;
  outline: none;
  resize: none;
  font-family: inherit;
  font-size: 15px;
  line-height: 22px;
  background: transparent;
  color: var(--tnzi-ai-text, #34322d);
  padding: 0 18px;
  min-height: 22px;
  max-height: 200px;
}
.t-landing__input::placeholder {
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
}

.t-landing__composer-bar {
  display: flex;
  align-items: center;
  padding: 8px 10px 0;
}
.t-landing__composer-left,
.t-landing__composer-right {
  display: flex;
  align-items: center;
  gap: 2px;
}
.t-landing__composer-right { margin-left: auto; }

.t-landing__send {
  width: 32px;
  height: 32px;
  border: none;
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
  border-radius: 999px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  margin-left: 4px;
  transition: background 120ms var(--tnzi-ai-easing, ease),
              color 120ms var(--tnzi-ai-easing, ease);
}
.t-landing__send:disabled { cursor: not-allowed; }
.t-landing__send.is-ready {
  background: var(--tnzi-ai-text, #34322d);
  color: var(--tnzi-ai-bg, #f8f8f7);
}

.t-landing__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: center;
  margin-top: 18px;
  max-width: 768px;
}
.t-landing__chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 32px;
  padding: 0 14px;
  border: 1px solid var(--tnzi-ai-border, rgba(0, 0, 0, 0.08));
  background: var(--tnzi-ai-surface, #ffffff);
  color: var(--tnzi-ai-text, #34322d);
  border-radius: 999px;
  font-family: inherit;
  font-size: 13px;
  cursor: pointer;
  transition: background 120ms var(--tnzi-ai-easing, ease);
}
.t-landing__chip:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
}
.t-landing__chip > .iconify {
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  font-size: 15px;
}

.t-landing__footer {
  margin-top: 24px;
  width: 100%;
  max-width: 768px;
  display: flex;
  justify-content: center;
}
</style>
