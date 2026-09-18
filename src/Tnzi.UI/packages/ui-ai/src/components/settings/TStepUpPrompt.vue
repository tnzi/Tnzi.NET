<script setup lang="ts">
/**
 * @experimental
 * TStepUpPrompt - inline re-authentication for `[RequireStepUp]` writes.
 *
 * Renders a `@tnzi/core` `StepUpPromptController` (the one
 * `useAccountSettings` builds and exposes as `stepUp`). The controller owns the
 * state machine and the calls; this is markup. It sits at the top of the
 * settings page that triggered the challenge rather than in a modal: the
 * settings dialog is already an overlay, and the user has to see which row
 * they were acting on while they confirm.
 *
 * Flow: a write answers `IDENTITY_STEP_UP_REQUIRED` -> `withStepUp` calls
 * `prompt.verify(scope)` -> this panel appears -> passkey or code -> the write
 * is replayed once. Cancel leaves the challenge as the page's visible error.
 */
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { NButton, NInput } from 'naive-ui'
import type { StepUpMethod, StepUpPromptController } from '@tnzi/core/services/identity'
import { useAiI18n, formatAiMessage } from '../../i18n'

const props = defineProps<{
  prompt: StepUpPromptController
}>()

const t = useAiI18n()

// `verify()` settles only through this renderer's choose / submitCode /
// cancel. Closing the settings dialog mid-prompt would otherwise unmount the
// panel with the prompt still open and the calling write (and its `busy`)
// pending until the dialog was reopened; unmounting is a cancel.
onBeforeUnmount(() => {
  if (props.prompt.open) props.prompt.cancel()
})

const code = ref('')

// Fresh entry whenever the prompt (re)opens or the method changes.
watch(
  () => [props.prompt.open, props.prompt.method] as const,
  () => {
    code.value = ''
  },
)

const methodLabel = computed<Record<StepUpMethod, string>>(() => ({
  passkey: t.value.settings.stepUpMethodPasskey,
  totp: t.value.settings.stepUpMethodTotp,
  sms: t.value.settings.stepUpMethodSms,
  email: t.value.settings.stepUpMethodEmail,
}))

const codeHint = computed(() => {
  const { method, sentTo } = props.prompt
  if (method === 'totp') return t.value.settings.stepUpCodeHintTotp
  if (sentTo) return formatAiMessage(t.value.settings.stepUpCodeSentTo, { to: sentTo })
  return t.value.settings.stepUpCodeSent
})

const canResend = computed(() => props.prompt.method === 'sms' || props.prompt.method === 'email')

// Keyed on `method`, not `stage`: while a typed code is being verified the
// stage is `busy` but the user is still in the code flow, and flipping back to
// the chooser mid-request would read as "it rejected me". `method` is set only
// once a code path is committed to and cleared by `back()`.
const inCodeFlow = computed(() => props.prompt.method !== null)
const verifying = computed(() => inCodeFlow.value && props.prompt.stage === 'busy')

async function submit(): Promise<void> {
  await props.prompt.submitCode(code.value)
  if (props.prompt.error) code.value = ''
}
</script>

<template>
  <section v-if="prompt.open" class="t-step-up" role="region" :aria-label="t.settings.stepUpTitle" :aria-busy="prompt.busy">
    <h3 class="t-step-up__title">{{ t.settings.stepUpTitle }}</h3>
    <p class="t-step-up__intro">{{ t.settings.stepUpIntro }}</p>

    <!-- Stage: choose a method -->
    <div v-if="!inCodeFlow" class="t-step-up__methods">
      <NButton
        v-for="method in prompt.methods"
        :key="method"
        size="small"
        :loading="prompt.stage === 'busy'"
        :disabled="prompt.busy"
        @click="prompt.choose(method)"
      >
        {{ methodLabel[method] }}
      </NButton>
      <p v-if="prompt.stage === 'choose' && !prompt.canVerify" class="t-settings-field__hint">
        {{ t.settings.stepUpNoMethods }}
      </p>
    </div>

    <!-- Stage: enter the code -->
    <div v-else class="t-step-up__code">
      <p class="t-settings-field__hint">{{ codeHint }}</p>
      <NInput
        v-model:value="code"
        class="t-settings-field__control"
        size="small"
        :maxlength="8"
        :disabled="prompt.busy"
        :status="prompt.error ? 'error' : undefined"
        :placeholder="t.settings.stepUpCodePlaceholder"
        @keyup.enter="submit"
      />
      <div class="t-step-up__links">
        <NButton text size="small" :disabled="prompt.busy" @click="prompt.back()">
          {{ t.settings.stepUpBack }}
        </NButton>
        <NButton v-if="canResend" text size="small" :disabled="prompt.busy" @click="prompt.resendCode()">
          {{ t.settings.stepUpResend }}
        </NButton>
      </div>
    </div>

    <p v-if="prompt.error" class="t-settings-field__error" role="alert">{{ prompt.error }}</p>

    <div class="t-settings-field__actions">
      <NButton size="small" :disabled="prompt.stage === 'busy'" @click="prompt.cancel()">
        {{ t.settings.stepUpCancel }}
      </NButton>
      <NButton
        v-if="inCodeFlow"
        size="small"
        type="primary"
        :loading="verifying"
        :disabled="!code.trim() || prompt.busy"
        @click="submit"
      >
        {{ t.settings.stepUpVerify }}
      </NButton>
    </div>
  </section>
</template>

<style scoped>
.t-step-up {
  margin-bottom: 16px;
  padding: 14px 16px;
  border: 1px solid color-mix(in srgb, var(--tnzi-ai-accent) 40%, transparent);
  border-radius: 10px;
  background: var(--tnzi-ai-accent-soft);
}
.t-step-up__title {
  margin: 0 0 4px;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-ai-text);
}
.t-step-up__intro {
  margin: 0 0 12px;
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-secondary);
}
.t-step-up__methods {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.t-step-up__code {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-step-up__links {
  display: flex;
  gap: 16px;
}
</style>
