// Public component surface for `components/display`.
//
// The package root re-exports this barrel wholesale, so this file is what
// decides which display primitives consumers can import from `@tnzi/ui-admin`.

// Status pill; label keys resolve through the admin page translator.
export { default as TStatusBadge } from './TStatusBadge.vue'
export type { StatusType } from './TStatusBadge.vue'

// Small display primitives rendered with naive.
export { default as TRelativeTime } from './TRelativeTime.vue'
export { default as TButtonIcon } from './TButtonIcon.vue'
export { default as THint } from './THint.vue'
export { default as TSourceBadge } from './TSourceBadge.vue'
export type { SourceKind } from './TSourceBadge.vue'
export { default as TStatToCards } from './TStatToCards.vue'
export type { StatCard } from './TStatToCards.vue'
export { default as TAttachmentWall } from './TAttachmentWall.vue'
export type { Attachment, AttachmentReorderPayload } from './TAttachmentWall.vue'

// Private-file rendering: the URL has to be swapped for a short-lived signed
// token first. These two own that step (and the pending state while it is in
// flight) so the call site is a single tag. Safe inside v-for - batching
// happens in the resolver layer, so N tags mounting in one tick still issue a
// single request.
export { default as TFileImage } from './TFileImage.vue'
export { default as TFileLink } from './TFileLink.vue'

// Sandboxed renderer for HTML strings the admin did not author (rendered
// template / notification previews). Use this instead of `v-html`: the markup
// is author-controlled and would otherwise execute at the admin's own origin
// inside an authenticated session.
export { default as THtmlPreview } from './THtmlPreview.vue'

// Generic ECharts panel - takes a pre-built EChartsOption (or a builder fn) and
// owns lifecycle / theme reactivity / resize. Degrades to a static placeholder
// where echarts cannot run (jsdom), keeping host pages mountable under vitest.
// TChartPanelInner stays internal: it is the lazily-loaded implementation half.
export { default as TChartPanel } from './TChartPanel.vue'
