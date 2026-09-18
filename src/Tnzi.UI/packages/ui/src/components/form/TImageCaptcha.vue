<script setup lang="ts">
/**
 * `TImageCaptcha` - the built-in picture captcha as a form field: a code input
 * next to the picture; clicking the picture (when refreshable) asks the parent
 * for a new one.
 *
 * Presentational only. The parent owns the challenge (id + image) and the
 * typed code; `TCaptcha` wraps this for the `image` provider and composes the
 * `{captchaId}:{code}` token. Moved down from `@tnzi/ui-admin` (`TLoginCaptcha`)
 * on 2026-09-16 so every UI package renders the same field.
 */
import { NInput, NSpin } from 'naive-ui'
import { useI18n } from '@tnzi/core/adapters/i18n'
import TSvgIcon from '../display/TSvgIcon.vue'

defineOptions({ name: 'TImageCaptcha' })

const props = defineProps<{
  /** Base64 PNG (no data-uri prefix). Empty while loading / before the first fetch. */
  image: string
  /** A fetch is in flight. */
  loading?: boolean
  /** Whether the refresh affordance is active (a loader is wired). */
  refreshable?: boolean
  /** Disable the input. */
  disabled?: boolean
  /** Placeholder for the code input. */
  placeholder?: string
  /** Accessible title for the refresh button. */
  refreshTitle?: string
}>()

const code = defineModel<string>({ default: '' })
const emit = defineEmits<{ (e: 'refresh'): void }>()

const { t } = useI18n()

function onRefresh(): void {
  if (props.refreshable && !props.loading) emit('refresh')
}
</script>

<template>
  <div class="t-image-captcha">
    <NInput
      v-model:value="code"
      class="t-image-captcha__input"
      :placeholder="placeholder ?? t('auth.enterCaptcha')"
      :disabled="disabled"
      autocomplete="off"
    />
    <button
      type="button"
      class="t-image-captcha__image"
      :class="{ 't-image-captcha__image--refreshable': refreshable }"
      :title="refreshable ? (refreshTitle ?? t('auth.refreshCaptcha')) : undefined"
      :aria-label="refreshTitle ?? t('auth.refreshCaptcha')"
      :disabled="!refreshable || disabled"
      @click="onRefresh"
    >
      <NSpin v-if="loading" :size="16" />
      <img v-else-if="image" :src="`data:image/png;base64,${image}`" alt="captcha" />
      <TSvgIcon v-else icon="mdi:image-broken-variant" :size="20" />
    </button>
  </div>
</template>

<style scoped>
.t-image-captcha {
  display: flex;
  align-items: stretch;
  gap: 12px;
  width: 100%;
}
.t-image-captcha__input {
  flex: 1;
  min-width: 0;
}
.t-image-captcha__image {
  flex: 0 0 auto;
  width: 116px;
  height: 40px;
  padding: 0;
  border: 1px solid var(--tnzi-border, #e0e0e6);
  border-radius: var(--tnzi-radius-md, 6px);
  background: var(--tnzi-base-bg, #fff);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  cursor: default;
  color: var(--tnzi-base-text-muted, #999);
  transition: border-color 0.15s;
}
.t-image-captcha__image--refreshable {
  cursor: pointer;
}
.t-image-captcha__image--refreshable:hover {
  border-color: var(--tnzi-primary, #2080f0);
}
.t-image-captcha__image img {
  width: 100%;
  height: 100%;
  /* contain (never cover) - a captcha must never be cropped; letterbox instead. */
  object-fit: contain;
  display: block;
}
</style>
