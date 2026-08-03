<script setup lang="ts">
/**
 * `THtmlPreview` - render an HTML **string** you did not author, safely.
 *
 * The case this exists for: a template/notification/document preview where the
 * backend hands back rendered markup and the admin wants to see what it will
 * look like. Dropping that into `v-html` puts author-controlled markup into the
 * admin's own DOM, at the admin's own origin, inside an authenticated session -
 * so whoever can edit a template can run script in a super-admin's browser.
 * That is a privilege-escalation path, not a styling detail.
 *
 * The fix is the same one `@tnzi/ui-ai`'s `TArtifactPanel` already applies to
 * model-generated HTML: put it in a sandboxed iframe. Here the content is a
 * string rather than a URL, so it goes in via `srcdoc`.
 *
 * ## Why the default sandbox is `''`
 *
 * `sandbox=""` applies **every** restriction: no scripts, no forms, no popups,
 * no top-level navigation, and - critically - a unique opaque origin. A
 * `srcdoc` document would otherwise inherit the embedding page's origin and be
 * able to reach `parent.document`, the host's storage and its cookies.
 *
 * Note that `allow-scripts` + `allow-same-origin` together **void** the sandbox:
 * the framed document can then remove its own `sandbox` attribute and reload.
 * Never pass both for content you do not control.
 *
 * A preview is something you look at, so the strict default costs nothing:
 * email and print templates are static markup. Widen it only for first-party
 * content that genuinely needs more (`:sandbox="'allow-scripts'"`), and pass
 * `null` to drop the attribute entirely - which is exactly as dangerous as
 * `v-html` and should be treated as such.
 *
 * ## Sizing
 *
 * Height is a prop and the frame scrolls internally. Auto-sizing to the content
 * would require reading `contentDocument`, which the opaque origin forbids -
 * measuring is precisely the access being denied. Trading the sandbox for a
 * tidier height is not a trade worth making.
 */
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    /** Raw HTML to render. Treated as untrusted. */
    html?: string | null
    /** Frame height. Number is px. */
    height?: string | number
    /**
     * `sandbox` attribute value. Default `''` = maximum restriction.
     * `null` removes the attribute (unsandboxed - avoid).
     */
    sandbox?: string | null
    /**
     * Paper-white backdrop. Templates are authored against white (email
     * clients, print), so a dark admin theme showing them on a dark surface
     * misrepresents what the recipient will see.
     */
    paper?: boolean
    /** Accessible name for the frame. */
    title?: string
  }>(),
  {
    html: '',
    height: 320,
    sandbox: '',
    paper: true,
    title: 'Preview',
  },
)

const frameHeight = computed(() =>
  typeof props.height === 'number' ? `${props.height}px` : props.height,
)

// `srcdoc` must be a string; `null`/`undefined` would render the attribute as
// the literal text "null".
const srcdoc = computed(() => props.html ?? '')
</script>

<template>
  <iframe
    class="t-html-preview"
    :class="{ 't-html-preview--paper': paper }"
    :style="{ height: frameHeight }"
    :srcdoc="srcdoc"
    :sandbox="sandbox === null ? undefined : sandbox"
    :title="title"
    loading="lazy"
    referrerpolicy="no-referrer"
  />
</template>

<style scoped>
.t-html-preview {
  display: block;
  width: 100%;
  border: 1px solid var(--tnzi-border);
  border-radius: 4px;
  background: var(--tnzi-bg-deep);
}

.t-html-preview--paper {
  background: #fff;
}
</style>
