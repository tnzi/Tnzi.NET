/**
 * Display primitives - pure props-driven visual components.
 *
 * Sunk from `@tnzi/ui-admin` in 0.2.x so site/chat/mobile can reuse them
 * without depending on the admin framework.
 */
export { default as TRelativeTime } from './TRelativeTime.vue'
export { default as TCountTo } from './TCountTo.vue'
export { default as TSvgIcon } from './TSvgIcon.vue'
export { default as TAvatar } from './TAvatar.vue'
export { default as TButtonIcon } from './TButtonIcon.vue'
export { default as THint } from './THint.vue'
export { default as TSourceBadge } from './TSourceBadge.vue'
export type { SourceKind } from './TSourceBadge.vue'
export { default as TStatToCards } from './TStatToCards.vue'
export type { StatCard } from './TStatToCards.vue'
export { default as TWaveBg } from './TWaveBg.vue'
export { default as TSkeleton } from './TSkeleton.vue'
export { default as TStatusBadge } from './TStatusBadge.vue'
export type { StatusType } from './TStatusBadge.vue'
export { default as TMetricBars } from './TMetricBars.vue'
export type { MetricBarItem, MetricBarClickEvent } from './TMetricBars.vue'
export { default as TNoteCard } from './TNoteCard.vue'
export { default as TActivityFeed } from './TActivityFeed.vue'
export { default as TAttachmentWall } from './TAttachmentWall.vue'
export type { Attachment } from './TAttachmentWall.vue'
// Read-only counterpart of a form: a record's fields as `label: value` rows in
// a container-derived grid, so a detail surface stops rendering a column of
// switched-off inputs.
export { default as TDescriptions } from './TDescriptions.vue'
export type { DescriptionItem } from './TDescriptions.vue'
