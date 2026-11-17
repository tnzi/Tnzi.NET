// Public component surface for `components/data`.
//
// This barrel is the single source of truth for what the folder exposes: the
// package root re-exports it wholesale (`export * from './data'`). Adding a
// component here is what makes it importable from `@tnzi/ui-admin`; a
// component left out of this file cannot be reached by consumers no matter
// what the docs promise.

// Responsive table - near drop-in NDataTable replacement that auto-stacks into
// cards on phones - and the card-list primitive underneath it.
// The plain naive data table wrapper (sortable columns, remote pagination) and
// a paginated list of rows; list pages use the `crud/` shells instead.
export { default as TTable } from './TTable.vue'
export { default as TDataList } from './TDataList.vue'

export { default as TResponsiveTable } from './TResponsiveTable.vue'
export type {
  TResponsiveTableProps,
  TResponsivePagination,
  TResponsiveSummaryRow,
} from './TResponsiveTable.vue'
export { default as TDataCardList } from './TDataCardList.vue'
export type { CardColumn } from './TDataCardList.vue'

// List footer pager ("Total N" + size picker, `simple` on phones). Rendered by
// TListShell; exported so lists that do not ride the shell match it.
export { default as TListPager } from './TListPager.vue'

// Financial-report table - money/total columns + auto totals row + drill-down.
export { default as TReportTable } from './TReportTable.vue'
export type { ReportColumn } from './TReportTable.vue'

// Card primitives for the two non-table list shapes: tile grid (TEntityCard,
// rendered by TCardPage) and document rows (TItemCard, rendered by TItemPage).
export { default as TEntityCard } from './TEntityCard.vue'
export { default as TItemCard } from './TItemCard.vue'
export type { ItemCardTag, ItemCardMeta, ItemCardTone } from './TItemCard.vue'

// KPI primitives - unified KPI card + responsive KPI strip (one per page,
// rendered between the page header and the list/content per the content-page
// standard). TEmpty is the unified empty-state visual used by the card
// renderers and available to bespoke pages.
// TKpiCard is the content-page KPI card; `TStatCard` (`card/`) is a different
// component, an `NStatistic` with a trend.
export { default as TKpiCard } from './TKpiCard.vue'
export type { TKpiCardProps, TKpiCardTone } from './TKpiCard.vue'
export { default as TKpiRow } from './TKpiRow.vue'
// Moved to `@tnzi/ui` on 2026-08-02 (framework-wide empty state, also used by
// `@tnzi/ui-ai`). Re-exported so existing imports keep resolving.
export { TEmpty, type TEmptyProps, type TEmptySize } from '@tnzi/ui'

// Record collaboration primitives: entity-agnostic on purpose, so any module
// (or a consuming app's own records) reuses them instead of re-deriving.
export { default as TAttachmentPanel } from './TAttachmentPanel.vue'
export type { AttachmentItem } from './TAttachmentPanel.vue'
export { default as TCommentThread } from './TCommentThread.vue'
export type { CommentItem } from './TCommentThread.vue'

// Chunked/resumable file upload.
export { default as TChunkFileUpload } from './TChunkFileUpload.vue'
export type { ChunkUploader } from './TChunkFileUpload.vue'
