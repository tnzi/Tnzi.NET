<script setup lang="ts">
/**
 * TArtifactPreview - iframe preview with address bar and console
 *
 * The address bar is a draft: typing does not navigate the frame, Enter
 * does. Refresh goes back to `src` and reloads the frame even when it is
 * already there. With `srcdoc` the bar is read-only.
 */

import { ref, watch } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import TIconButton from '../controls/TIconButton.vue';
const t = useAiI18n();

const props = withDefaults(
  defineProps<{
    src?: string;
    srcdoc?: string;
    logs?: Array<{ level: 'log' | 'warn' | 'error'; message: string; timestamp?: string }>;
    /**
     * Iframe `sandbox` attribute, applied to both the `srcdoc` and the `src`
     * frame.
     *
     * Defaults to `allow-scripts allow-forms allow-popups` and deliberately
     * omits `allow-same-origin` - same rule and same reason as
     * `TArtifactPanel`: this component previews untrusted, model-generated
     * HTML, and `allow-scripts` + `allow-same-origin` together void the
     * sandbox (the framed document runs on the host's origin, so it can read
     * the host's storage - the bearer token by default - call the API as the
     * user, and strip its own `sandbox` attribute).
     *
     * Only opt back in when the previewed content is first-party and
     * genuinely needs same-origin access:
     * `:iframe-sandbox="'allow-scripts allow-same-origin'"`.
     *
     * An empty string renders `sandbox=""` (every restriction applied). To
     * remove the attribute entirely and run the frame unsandboxed, pass
     * `null` explicitly.
     */
    iframeSandbox?: string | null;
  }>(),
  {
    iframeSandbox: 'allow-scripts allow-forms allow-popups',
  },
);

/** The address the frame shows. */
const localUrl = ref(props.src ?? '');
/** What is typed in the address bar; becomes `localUrl` on Enter. */
const draftUrl = ref(localUrl.value);
/** Bumped by Refresh, so the frame reloads even when its address is unchanged. */
const reloadKey = ref(0);
const consoleOpen = ref(false);

watch(() => props.src, (v) => {
  if (!v) return;
  localUrl.value = v;
  draftUrl.value = v;
});

function navigate(): void {
  localUrl.value = draftUrl.value.trim();
}

function refresh(): void {
  localUrl.value = props.src ?? '';
  draftUrl.value = localUrl.value;
  reloadKey.value += 1;
}

function openExternal(): void {
  window.open(localUrl.value || props.src, '_blank', 'noopener,noreferrer');
}

const logLevelClass: Record<string, string> = {
  log: '',
  warn: 't-preview-log--warn',
  error: 't-preview-log--error',
};
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="t-artifact-preview__bar">
      <TIconButton icon="lucide:refresh-cw" :label="t.common.refresh" size="xs" @click="refresh" />
      <input
        v-model="draftUrl"
        type="text"
        class="t-input t-mono t-artifact-preview__url"
        :aria-label="t.artifact.address"
        :readonly="!!srcdoc"
        spellcheck="false"
        autocomplete="off"
        @keydown.enter="navigate"
      />
      <TIconButton icon="lucide:square-arrow-out-up-right" :label="t.artifact.openExternal" size="xs" @click="openExternal" />
    </div>
    <div class="relative flex-1 min-h-0">
      <iframe v-if="srcdoc" :key="`doc-${reloadKey}`" :srcdoc="srcdoc" class="h-full w-full border-0" :sandbox="iframeSandbox ?? undefined" />
      <iframe v-else-if="localUrl" :key="`src-${reloadKey}`" :src="localUrl" class="h-full w-full border-0" :sandbox="iframeSandbox ?? undefined" />
      <div v-else class="flex h-full items-center justify-center text-sm text-tnzi-muted">{{ t.artifact.noPreview }}</div>
    </div>
    <div v-if="logs?.length">
      <button type="button" :aria-expanded="consoleOpen" class="flex w-full items-center gap-1.5 border-t px-3 py-1 text-xs text-tnzi-muted hover:bg-tnzi-layout/50 transition-colors" @click="consoleOpen = !consoleOpen">
        <Icon icon="lucide:terminal" class="size-3" />
        <span>{{ t.artifact.console }}</span>
        <span class="ml-auto tabular-nums">{{ logs.length }}</span>
        <Icon icon="lucide:chevron-down" class="size-3 transition-transform" :class="{ 'rotate-180': consoleOpen }" />
      </button>
      <div v-show="consoleOpen" class="t-ai-scroll relative max-h-[120px] overflow-auto border-t bg-tnzi-layout/30 p-2 font-mono text-[11px] leading-[var(--tnzi-ai-code-line-height,1.5)]">
        <div v-for="(log, i) in logs" :key="i" class="py-0.5" :class="logLevelClass[log.level]">{{ log.message }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-artifact-preview__bar {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px 6px;
  border-bottom: 1px solid var(--tnzi-ai-border);
}
input.t-artifact-preview__url {
  flex: 1;
  height: 28px;
}
.t-preview-log--warn {
  color: var(--tnzi-ai-warning);
}
.t-preview-log--error {
  color: var(--tnzi-ai-danger);
}
</style>
