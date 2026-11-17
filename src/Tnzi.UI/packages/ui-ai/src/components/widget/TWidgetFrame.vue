<script setup lang="ts">
/**
 * @experimental
 * TWidgetFrame - one widget, live: the widget runner in an iframe, the widget's document handed
 * to it over `postMessage`, the frame as tall as the document says (within `maxHeight`; longer
 * content scrolls inside the frame), the page's theme handed in and kept up to date.
 *
 * Isolation does not depend on the page this sits in. The iframe is
 * `sandbox="allow-scripts allow-forms"` (no same-origin, no popups, no top navigation) and the
 * runner page carries its own policy (an origin-less sandbox, no network, `form-action 'none'`),
 * so a widget can reach neither this page's storage, cookies or token nor the network, and it runs
 * under the runner's policy, not this page's: a page under `script-src 'self'` only has to allow
 * the runner in `frame-src`. Serve the runner from a host name of its own (`runnerUrl`) to keep the
 * widget off the chat's site altogether.
 *
 * Forms: `allow-forms` is there so a widget's `<form>` fires `submit` (and Enter in a field submits
 * it), as widget authors expect; without it the browser drops the submission before the event.
 * It opens nothing else. A form submission is a navigation, and every navigation a form could make
 * is closed: the runner's `form-action 'none'` (which the widget's srcdoc frame inherits) blocks
 * any action URL, the bridge cancels every `submit` the widget did not (so a form never even tries
 * to leave), a `target` of `_blank`, `_top` or `_parent` stays blocked by the missing
 * `allow-popups` / `allow-top-navigation` and the sandbox's own navigation rule, and the origin
 * stays opaque with `connect-src 'none'`. Data leaves a widget only through the host's bridge.
 *
 * The host context carries the language (`locale`): the `locale` prop, else the language the app
 * provides with its catalogue (`createAiI18nPlugin` / `createAiI18n`), else `<html lang>`. A change
 * of any of them, like a change of theme, reaches the widget as `host-context-changed`.
 *
 * A frame far outside the viewport is unmounted (its height kept, so nothing jumps) and mounted
 * again when it comes near, so a long thread does not keep dozens of live frames. Coming back
 * restarts the widget.
 */
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { useAiI18n, useAiLocale } from '../../i18n/index'
import { composeWidgetDocument, widgetHostContext } from './widget-document'
import { createWidgetHost, type WidgetHost, type WidgetHostHandlers } from './widget-host'
import { readWidgetTheme, sameWidgetTheme, watchWidgetTheme, type WidgetTheme } from './widget-theme'

const props = withDefaults(
  defineProps<{
    /** The widget runner page (absolute, or relative to this page). */
    runnerUrl: string
    /** The widget's HTML document. */
    html: string
    /** The widget's title (the frame's accessible name). */
    title: string
    /** The tallest the frame grows, in pixels; beyond it the widget scrolls. */
    maxHeight?: number
    /** Fill the container's height instead of following the document (the larger view). */
    fill?: boolean
    /** What widgets may do beyond showing themselves (`ui/message`, `tools/call`, links). */
    handlers?: WidgetHostHandlers
    /** Unmount the frame while it is far outside the viewport. */
    lazy?: boolean
    /**
     * The iframe's sandbox. Scripts and forms: no same-origin, popups, top navigation or modals.
     * The runner page also sandboxes itself (its CSP `sandbox allow-scripts allow-forms`), so
     * adding tokens here cannot give a widget an origin; dropping `allow-forms` stops widgets'
     * forms from firing `submit`.
     */
    sandbox?: string
    /**
     * The widget's language (a BCP 47 tag) for its host context. Left out, the language the app
     * provides with its catalogue, else `<html lang>`.
     */
    locale?: string
  }>(),
  { maxHeight: 640, fill: false, handlers: undefined, lazy: true, sandbox: 'allow-scripts allow-forms', locale: undefined },
)

const t = useAiI18n()
const appLocale = useAiLocale()

/** Height before the widget has reported one. */
const INITIAL_HEIGHT = 160
const MIN_HEIGHT = 32
/** How far outside the viewport a frame stays mounted. */
const KEEP_MOUNTED_MARGIN = '1200px 0px'

const root = ref<HTMLElement | null>(null)
const frame = ref<HTMLIFrameElement | null>(null)
const reportedHeight = ref<number | null>(null)
const near = ref(!props.lazy || props.fill)
const navigated = ref(false)
/** Bumped to replace the iframe with a fresh one. */
const frameKey = ref(0)
const theme = shallowRef<WidgetTheme>(readWidgetTheme(null))
const pageLang = ref(readPageLang())
const locale = computed(() => props.locale?.trim() || appLocale.value?.trim() || pageLang.value || undefined)
/** A message the widget wants to send, waiting for the user. */
const messageRequest = shallowRef<{ text: string; resolve: (ok: boolean) => void } | null>(null)

const src = computed(() => {
  if (typeof window === 'undefined') return null
  try {
    const url = new URL(props.runnerUrl, window.location.href)
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null
  } catch {
    return null
  }
})

const height = computed(() => Math.max(MIN_HEIGHT, Math.min(reportedHeight.value ?? INITIAL_HEIGHT, props.maxHeight)))
const live = computed(() => near.value && !navigated.value && src.value !== null)

let host: WidgetHost | null = null

function readPageLang(): string {
  return typeof document === 'undefined' ? '' : document.documentElement.lang.trim()
}

function context() {
  return widgetHostContext(theme.value, props.fill ? 'fullscreen' : 'inline', locale.value)
}

function confirmMessage(text: string): Promise<boolean> {
  messageRequest.value?.resolve(false)
  return new Promise((resolve) => {
    messageRequest.value = { text, resolve }
  })
}

function answerMessage(ok: boolean): void {
  messageRequest.value?.resolve(ok)
  messageRequest.value = null
}

function stopHost(): void {
  host?.dispose()
  host = null
  answerMessage(false)
}

function startHost(): void {
  stopHost()
  if (typeof window === 'undefined') return
  const currentTheme = theme.value
  host = createWidgetHost({
    window,
    frame: () => frame.value?.contentWindow,
    document: composeWidgetDocument(props.html, currentTheme, context()),
    title: props.title,
    context,
    onSize: ({ height: next }) => {
      reportedHeight.value = next
    },
    onNavigated: () => {
      navigated.value = true
    },
    confirmMessage,
    handlers: props.handlers,
  })
}

function reload(): void {
  navigated.value = false
  frameKey.value += 1
}

// A new iframe element (first mount, coming back into view, reload) gets a fresh host.
watch(frame, (element) => (element ? startHost() : stopHost()))
// A language switch reaches a running widget as a context change, as a theme switch does.
watch(locale, () => host?.contextChanged())
watch(() => props.html, () => {
  if (frame.value) frameKey.value += 1
})

let stopTheme: (() => void) | null = null
let observer: IntersectionObserver | null = null

onMounted(() => {
  theme.value = readWidgetTheme(root.value)
  stopTheme = watchWidgetTheme(() => {
    pageLang.value = readPageLang()
    const next = readWidgetTheme(root.value)
    if (sameWidgetTheme(next, theme.value)) return
    theme.value = next
    host?.contextChanged()
  })
  if (!near.value) {
    if (typeof IntersectionObserver === 'undefined' || !root.value) {
      near.value = true
    } else {
      observer = new IntersectionObserver((entries) => {
        const entry = entries[entries.length - 1]
        if (entry) near.value = entry.isIntersecting
      }, { rootMargin: KEEP_MOUNTED_MARGIN })
      observer.observe(root.value)
    }
  }
})

onBeforeUnmount(() => {
  observer?.disconnect()
  stopTheme?.()
  stopHost()
})
</script>

<template>
  <div
    ref="root"
    class="t-widget-frame"
    :class="{ 'is-fill': fill }"
    :style="fill ? undefined : { height: `${height}px` }"
  >
    <iframe
      v-if="live"
      :key="frameKey"
      ref="frame"
      class="t-widget-frame__iframe"
      :src="src ?? undefined"
      :title="title"
      :sandbox="sandbox"
      referrerpolicy="no-referrer"
      allow=""
      :style="{ colorScheme: theme.scheme }"
    />
    <div v-else-if="navigated" class="t-widget-frame__notice" role="status">
      <span>{{ t.widget.navigated }}</span>
      <button type="button" class="t-widget-frame__button" @click="reload">{{ t.widget.reload }}</button>
    </div>
    <div v-else-if="src === null" class="t-widget-frame__notice" role="status">{{ t.widget.noRunner }}</div>

    <div v-if="messageRequest" class="t-widget-frame__confirm" role="alertdialog" :aria-label="t.widget.messageRequest">
      <span class="t-widget-frame__confirm-label">{{ t.widget.messageRequest }}</span>
      <q class="t-widget-frame__confirm-text t-ai-scroll">{{ messageRequest.text }}</q>
      <span class="t-widget-frame__confirm-actions">
        <button type="button" class="t-widget-frame__button" @click="answerMessage(false)">{{ t.widget.cancel }}</button>
        <button type="button" class="t-widget-frame__button is-primary" @click="answerMessage(true)">{{ t.widget.send }}</button>
      </span>
    </div>
  </div>
</template>

<style scoped>
.t-widget-frame {
  position: relative;
  width: 100%;
  min-width: 0;
  transition: height var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-widget-frame.is-fill {
  height: 100%;
}
.t-widget-frame__iframe {
  display: block;
  width: 100%;
  height: 100%;
  border: 0;
  background: transparent;
}
.t-widget-frame__notice {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: 8px;
  height: 100%;
  padding: 12px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 13px;
  text-align: center;
}
.t-widget-frame__confirm {
  position: absolute;
  inset: auto 8px 8px 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 10px 12px;
  border: 1px solid var(--tnzi-ai-border-strong);
  border-radius: 10px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-size: 13px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
}
.t-widget-frame__confirm-label {
  color: var(--tnzi-ai-text-secondary);
}
.t-widget-frame__confirm-text {
  position: relative;
  max-height: 6lh;
  overflow: auto;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.t-widget-frame__confirm-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.t-widget-frame__button {
  padding: 4px 12px;
  border: 1px solid var(--tnzi-ai-border-strong);
  border-radius: 8px;
  background: transparent;
  color: inherit;
  font: inherit;
  cursor: pointer;
}
.t-widget-frame__button:hover {
  background: var(--tnzi-ai-hover);
}
.t-widget-frame__button.is-primary {
  border-color: transparent;
  background: var(--tnzi-ai-accent);
  color: var(--tnzi-ai-on-accent);
}
</style>
