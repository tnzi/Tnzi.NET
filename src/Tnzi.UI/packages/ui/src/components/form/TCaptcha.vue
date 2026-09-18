<script setup lang="ts">
/**
 * `TCaptcha` - one field for whichever captcha provider the deployment runs.
 *
 * Reads the provider from `config` (`GET /captcha/config`, or `/auth/config`'s
 * `captcha`) and renders it:
 *   - `image`   → the built-in picture + code input (`TImageCaptcha`); the token
 *                 is `{captchaId}:{code}`. The challenge comes from `loadImage`
 *                 (wired to `GET /auth/captcha/{purpose}/json`) or from `seed`
 *                 (the picture the backend pushed inline on
 *                 `IDENTITY_CAPTCHA_REQUIRED`).
 *   - reCAPTCHA v2 / v3, hCaptcha, Turnstile, Altcha → the provider's own widget
 *                 through `useCaptchaWidget`; the token is whatever it produced.
 *   - anything else (e.g. `sliding`) → an "unsupported" notice: those providers
 *                 need a bespoke UI the consumer builds.
 *
 * `v-model:token` is the string to submit as `captchaToken` (or in the
 * `X-Captcha-Token` header). Call `execute()` right before submitting - the
 * invisible providers (reCAPTCHA v3) only produce their token then - and
 * `reset()` after a rejected submit (tokens are single-use).
 *
 * Standalone by design: no login-shell context, so a consumer's contact form
 * can use it against its own `[RequireCaptcha("contact")]` endpoint.
 */
import { computed, ref, watch, onMounted } from 'vue'
import { NText } from 'naive-ui'
import { useI18n } from '@tnzi/core/adapters/i18n'
import {
  composeImageCaptchaToken,
  isScriptCaptchaProvider,
  useCaptchaWidget,
  type CaptchaChallengeDto,
  type CaptchaClientConfigDto,
} from '@tnzi/core/services/captcha'
import TImageCaptcha from './TImageCaptcha.vue'

defineOptions({ name: 'TCaptcha' })

const props = withDefaults(
  defineProps<{
    /** Provider config. `null` / disabled → the built-in image captcha (when `loadImage` or `seed` is given). */
    config?: CaptchaClientConfigDto | null
    /** The purpose the token is for (`login` / `register` / your endpoint's `[RequireCaptcha]` purpose). */
    purpose: string
    /**
     * A challenge the backend pushed inline (`IDENTITY_CAPTCHA_REQUIRED`). For the
     * `image` provider it seeds the picture; for others it only says "render now".
     */
    seed?: CaptchaChallengeDto | null
    /** Fetch a fresh image challenge (`image` provider only). Enables the refresh button. */
    loadImage?: (purpose: string) => Promise<CaptchaChallengeDto>
    /** Load the image challenge on mount (`image` provider only). Default true when `loadImage` is given and no `seed`. */
    autoLoad?: boolean
    /**
     * Resolves the config's API-relative challenge URL against the API base (Altcha fetches its
     * challenge itself). Usually `(u) => client.resolveUrl(u)`; the login shells supply it from
     * the app's client. Without it an Altcha deployment shows an error naming the missing client
     * rather than a widget that silently fetches the SPA's `index.html`.
     */
    resolveUrl?: (url: string) => string
    /** Widget colour scheme (script providers). */
    theme?: 'light' | 'dark' | 'auto'
    disabled?: boolean
    placeholder?: string
    refreshTitle?: string
  }>(),
  { config: null, seed: null, loadImage: undefined, autoLoad: undefined, resolveUrl: undefined, theme: undefined, disabled: false },
)

const token = defineModel<string>('token', { default: '' })
const emit = defineEmits<{
  (e: 'solved', token: string): void
  (e: 'expired'): void
  (e: 'error', message: string): void
}>()

const { t } = useI18n()

/** Which provider this field renders: the config's, else what the seed says, else the image captcha. */
const provider = computed<string>(() => {
  const cfg = props.config
  if (cfg?.enabled && cfg.provider) return cfg.provider
  return props.seed?.provider ?? 'image'
})
const isImage = computed(() => provider.value === 'image')
const isScript = computed(() => isScriptCaptchaProvider(provider.value))
const isUnsupported = computed(() => !isImage.value && !isScript.value)

// ── image provider ──────────────────────────────────────────────────────────
const captchaId = ref('')
const imageBase64 = ref('')
const code = ref('')
const imageLoading = ref(false)
const imageError = ref('')
const canRefresh = computed(() => !!props.loadImage)

async function refreshImage(): Promise<void> {
  if (!props.loadImage) return
  imageLoading.value = true
  imageError.value = ''
  try {
    const c = await props.loadImage(props.purpose)
    captchaId.value = c.captchaId ?? ''
    imageBase64.value = c.imageBase64 ?? ''
    code.value = ''
  } catch (e) {
    imageError.value = e instanceof Error ? e.message : t('auth.captchaLoadFailed')
    emit('error', imageError.value)
  } finally {
    imageLoading.value = false
  }
}

function applySeed(seed: CaptchaChallengeDto | null | undefined): void {
  if (!seed || !isImage.value) return
  if (seed.captchaId && seed.imageBase64) {
    captchaId.value = seed.captchaId
    imageBase64.value = seed.imageBase64
    code.value = ''
    imageError.value = ''
  } else if (!imageBase64.value) {
    // The backend asked for a captcha but sent no picture (cache unavailable): fetch one.
    void refreshImage()
  }
}

watch(() => props.seed, (seed) => applySeed(seed), { immediate: true })

watch([captchaId, code], () => {
  if (!isImage.value) return
  token.value = composeImageCaptchaToken(captchaId.value, code.value) ?? ''
})

onMounted(() => {
  const auto = props.autoLoad ?? (!!props.loadImage && !props.seed)
  if (isImage.value && auto && !imageBase64.value) void refreshImage()
})

// ── script providers ────────────────────────────────────────────────────────
const widget = useCaptchaWidget({
  config: () => (isScript.value ? props.config : null),
  purpose: props.purpose,
  client: props.resolveUrl ? { resolveUrl: props.resolveUrl } : undefined,
  theme: props.theme,
  translate: (key, fallback) => {
    const value = t(key)
    return value === key ? (fallback ?? key) : value
  },
})
/** Bound by `ref="container"` in the template: the element the script widget renders into. */
const container = widget.container

watch(widget.token, (value, previous) => {
  if (!isScript.value) return
  token.value = value
  if (value) emit('solved', value)
  else if (previous) emit('expired')
})

watch(widget.error, (message) => {
  if (message) emit('error', message)
})

// ── public surface ──────────────────────────────────────────────────────────
/** Clear the field so the user solves it again (image: a fresh picture; widget: provider reset). */
function reset(): void {
  if (isImage.value) {
    code.value = ''
    token.value = ''
    if (props.loadImage) void refreshImage()
    return
  }
  widget.reset()
  token.value = ''
}

/**
 * Obtain the token to submit. Invisible providers run their check here; visible ones
 * return what the user produced and reject when nothing is solved yet.
 */
async function execute(): Promise<string> {
  if (isImage.value) {
    const value = composeImageCaptchaToken(captchaId.value, code.value)
    if (!value) throw new Error(t('auth.captchaRequired'))
    token.value = value
    return value
  }
  if (isUnsupported.value) throw new Error(t('auth.captchaUnsupported', { provider: provider.value }))
  const value = await widget.execute()
  token.value = value
  return value
}

const errorMessage = computed(() => (isImage.value ? imageError.value : widget.error.value))

defineExpose({ reset, execute, provider, isImage })
</script>

<template>
  <div class="t-captcha" :data-provider="provider">
    <TImageCaptcha
      v-if="isImage"
      v-model="code"
      :image="imageBase64"
      :loading="imageLoading"
      :refreshable="canRefresh"
      :disabled="disabled"
      :placeholder="placeholder"
      :refresh-title="refreshTitle"
      @refresh="refreshImage"
    />
    <div v-else-if="isScript" ref="container" class="t-captcha__widget" :class="{ 't-captcha__widget--disabled': disabled }" />
    <NText v-else depth="3" class="t-captcha__unsupported">
      {{ t('auth.captchaUnsupported', { provider }) }}
    </NText>
    <NText v-if="errorMessage" type="error" class="t-captcha__error">{{ errorMessage }}</NText>
  </div>
</template>

<style scoped>
.t-captcha {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
}
.t-captcha__widget {
  min-height: 1px;
}
.t-captcha__widget--disabled {
  pointer-events: none;
  opacity: 0.6;
}
.t-captcha__error {
  font-size: 12px;
}
</style>
