<script setup lang="ts">
/**
 * @experimental
 * TGuestWelcome - the welcome screen a signed-out visitor sees instead of the sign-in page
 * (`defineChatApp({ guestWelcome })`).
 *
 * The visitor types first. Sending keeps the message (`stashWelcomeMessage`, this tab
 * only) and opens the sign-in page with the conversation screen as the destination; once
 * signed in, `useChatThreads` sends the kept message as the first turn. A visitor who is
 * already signed in is taken straight to the conversation.
 *
 * Its composer is its own and deliberately small (a text box and a send button): this is
 * the page a signed-out visitor downloads first, so it does not carry the conversation
 * composer's voice input and attachment handling (files could not be kept across the
 * sign-in detour anyway). It looks like the landing page it leads into: same headline,
 * same surface and radius tokens.
 */
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { Icon } from '@iconify/vue'
import { isImeComposing } from '@tnzi/core/utils'
import type { AdminAuthRuntime } from '@tnzi/ui'
import { stashWelcomeMessage } from '../headless/welcome-message'
import { useAiI18n } from '../i18n/index'

const props = withDefaults(
  defineProps<{
    runtime: AdminAuthRuntime
    brandName?: string
    brandIcon?: string
    /** The brand name as a drawn logotype (an iconify name), shown in place of the
     *  `brandName` text: 1em of the name's font size high, as wide as its aspect
     *  ratio makes it, centred in the line the text occupied (which keeps its
     *  height), `currentColor` following the theme. `brandName` stays its
     *  accessible name. */
    brandWordmark?: string
    /** Headline. Defaults to the locale's landing greeting. */
    greeting?: string
    subline?: string
    /** Composer placeholder. Defaults to the locale's. */
    placeholder?: string
    /** Under the composer: what sending does. Defaults to the locale's; '' for none. */
    hint?: string
    /** Route name of the sign-in page. */
    loginName?: string
    /** Path of the conversation screen (the post-sign-in destination). */
    homePath?: string
    /** Query key carrying the post-sign-in destination. */
    redirectQuery?: string
  }>(),
  {
    brandName: '',
    brandIcon: '',
    brandWordmark: '',
    greeting: '',
    subline: '',
    placeholder: '',
    hint: undefined,
    loginName: 'login',
    homePath: '/',
    redirectQuery: 'redirect',
  },
)

const t = useAiI18n()
const router = useRouter()
const text = ref('')
const canSend = computed(() => text.value.trim().length > 0)

function signIn(): void {
  void router.push({ name: props.loginName, query: { [props.redirectQuery]: props.homePath } })
}

function send(): void {
  if (!canSend.value) return
  // Kept only if this tab can keep it; either way the visitor signs in next
  stashWelcomeMessage(text.value)
  signIn()
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Enter' && !event.shiftKey && !isImeComposing(event)) {
    event.preventDefault()
    send()
  }
}

onMounted(() => {
  if (props.runtime.auth.isLoggedIn) void router.replace(props.homePath)
})
</script>

<template>
  <div class="t-guest-welcome">
    <header class="t-guest-welcome__bar">
      <span class="t-guest-welcome__brand">
        <Icon v-if="brandIcon" :icon="brandIcon" class="t-guest-welcome__brand-icon" />
        <span v-if="brandWordmark" class="t-guest-welcome__brand-wordmark">
          <Icon :icon="brandWordmark" class="t-guest-welcome__brand-wordmark-glyph" />
          <span v-if="brandName" class="sr-only">{{ brandName }}</span>
        </span>
        <span v-else-if="brandName">{{ brandName }}</span>
      </span>
      <button type="button" class="t-guest-welcome__sign-in" @click="signIn">
        {{ t.welcome.signIn }}
      </button>
    </header>

    <main class="t-guest-welcome__main">
      <h1 class="t-guest-welcome__headline">{{ greeting || t.landing.greeting }}</h1>
      <p v-if="subline" class="t-guest-welcome__subline">{{ subline }}</p>

      <form class="t-guest-welcome__composer" @submit.prevent="send">
        <textarea
          v-model="text"
          class="t-guest-welcome__input"
          rows="3"
          :placeholder="placeholder || t.landing.placeholder"
          :aria-label="placeholder || t.landing.placeholder"
          @keydown="onKeydown"
        />
        <button
          type="submit"
          class="t-guest-welcome__send"
          :disabled="!canSend"
          :aria-label="t.composer.send"
        >
          <Icon icon="lucide:arrow-up" />
        </button>
      </form>
      <p v-if="hint !== ''" class="t-guest-welcome__hint">{{ hint ?? t.welcome.hint }}</p>
    </main>
  </div>
</template>

<style scoped>
.t-guest-welcome {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  min-height: 100dvh;
  background: var(--tnzi-ai-bg);
  color: var(--tnzi-ai-text);
}
.t-guest-welcome__bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 20px;
}
.t-guest-welcome__brand {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
}
.t-guest-welcome__brand-icon { font-size: 20px; }
/* The line the name's text occupied (`1lh`), with the wordmark 1em high in the
   middle of it. */
.t-guest-welcome__brand-wordmark {
  display: flex;
  align-items: center;
  min-height: 1lh;
}
.t-guest-welcome__brand-wordmark-glyph {
  display: block;
  max-width: 100%;
}
.t-guest-welcome__sign-in {
  padding: 6px 16px;
  border-radius: 999px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: 14px;
  cursor: pointer;
}
.t-guest-welcome__sign-in:hover { background: var(--tnzi-ai-hover); }
.t-guest-welcome__main {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  width: min(100% - 32px, 720px);
  margin: 0 auto;
  padding-bottom: 12vh;
}
.t-guest-welcome__headline {
  margin: 0 0 28px;
  font-family: var(--tnzi-ai-font-display, 'Libre Baskerville', Georgia, serif);
  font-size: 36px;
  line-height: 1.5;
  font-weight: 400;
  text-align: center;
}
.t-guest-welcome__subline {
  margin: -16px 0 28px;
  font-size: 16px;
  text-align: center;
  color: var(--tnzi-ai-text-tertiary);
}
.t-guest-welcome__composer {
  position: relative;
  width: 100%;
  border-radius: var(--tnzi-ai-composer-radius, 22px);
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  box-shadow: var(--tnzi-ai-composer-shadow, none);
}
.t-guest-welcome__composer:focus-within { border-color: var(--tnzi-ai-border-strong); }
.t-guest-welcome__input {
  display: block;
  width: 100%;
  min-height: 96px;
  resize: none;
  padding: 16px 64px 16px 18px;
  border: none;
  border-radius: inherit;
  background: transparent;
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: 15px;
  line-height: 1.6;
  outline: none;
}
.t-guest-welcome__send {
  position: absolute;
  right: 12px;
  bottom: 12px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border: none;
  border-radius: 50%;
  background: var(--tnzi-ai-accent);
  color: var(--tnzi-ai-on-accent);
  font-size: 18px;
  cursor: pointer;
}
.t-guest-welcome__send:disabled { opacity: 0.4; cursor: default; }
.t-guest-welcome__hint {
  margin: 12px 0 0;
  text-align: center;
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
</style>
