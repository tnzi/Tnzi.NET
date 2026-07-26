<script setup lang="ts">
/**
 * `TLoginCaptcha` - image-captcha field for the login / register forms.
 *
 * Presentational: shows the base64 PNG next to a code input; clicking the image
 * (when refreshable) asks the parent to fetch a new one. The parent owns the
 * captcha state via `useLoginCaptcha` and binds `v-model` (the typed code) plus
 * `:image` / `:loading` / `:refreshable`.
 */
import { NInput, NSpin } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

defineOptions({ name: 'TLoginCaptcha' })

const props = defineProps<{
  /** Base64 PNG (no data-uri prefix). Empty while loading / before the first fetch. */
  image: string
  /** A fetch is in flight. */
  loading?: boolean
  /** Whether the refresh affordance is active (the `getCaptcha` callback is wired). */
  refreshable?: boolean
  /** Placeholder for the code input. */
  placeholder?: string
  /** Accessible title for the refresh button. */
  refreshTitle?: string
}>()

const code = defineModel<string>({ default: '' })
const emit = defineEmits<{ (e: 'refresh'): void }>()

function onRefresh(): void {
  if (props.refreshable && !props.loading) emit('refresh')
}
</script>

<template>
  <div class="t-login-captcha">
    <NInput
      v-model:value="code"
      class="t-login-captcha__input"
      :placeholder="placeholder"
      autocomplete="off"
    />
    <button
      type="button"
      class="t-login-captcha__image"
      :class="{ 't-login-captcha__image--refreshable': refreshable }"
      :title="refreshable ? refreshTitle : undefined"
      :aria-label="refreshTitle"
      :disabled="!refreshable"
      @click="onRefresh"
    >
      <NSpin v-if="loading" :size="16" />
      <img v-else-if="image" :src="`data:image/png;base64,${image}`" alt="captcha" />
      <TSvgIcon v-else icon="mdi:image-broken-variant" :size="20" />
    </button>
  </div>
</template>

<style scoped>
.t-login-captcha {
  display: flex;
  align-items: stretch;
  gap: 12px;
  width: 100%;
}
.t-login-captcha__input {
  flex: 1;
  min-width: 0;
}
.t-login-captcha__image {
  flex: 0 0 auto;
  width: 116px;
  height: 40px;
  padding: 0;
  border: 1px solid var(--tnzi-border, #e0e0e6);
  border-radius: var(--tnzi-admin-radius-md, 6px);
  background: var(--tnzi-base-bg, #fff);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  cursor: default;
  color: var(--tnzi-base-text-muted, #999);
  transition: border-color 0.15s;
}
.t-login-captcha__image--refreshable {
  cursor: pointer;
}
.t-login-captcha__image--refreshable:hover {
  border-color: var(--tnzi-primary, #2080f0);
}
.t-login-captcha__image img {
  width: 100%;
  height: 100%;
  /* contain (never cover) - a captcha must never be cropped; letterbox instead. */
  object-fit: contain;
  display: block;
}
</style>
