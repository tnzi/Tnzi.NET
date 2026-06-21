import { h } from 'vue'
import { NTag } from 'naive-ui'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import type { FileShareSummaryDto } from '@tnzi/core/services/storage'
import { formatDateTime } from '@tnzi/core'

// Aligned to Tnzi.Storage.Dtos.FileShareSummaryDto. `allColumns` is typed as
// the untyped `ColumnDef[]`, so the render row is cast to the DTO locally.
const asShare = (row: Record<string, unknown>) => row as unknown as FileShareSummaryDto

/** Share columns (translate fn injected by the page for status / password value labels). */
export function buildShareColumns(t: (key: string) => string): ColumnDef[] {
  return [
    { key: 'originalName', title: 'columns.originalName', primary: true, ellipsis: { tooltip: true } },
    { key: 'shareToken', title: 'columns.shareToken', ellipsis: { tooltip: true } },
    {
      key: 'accessCount',
      title: 'columns.accessCount',
      align: 'right',
      render: (r) => {
        const row = asShare(r)
        return row.maxAccessCount != null
          ? `${row.accessCount ?? 0} / ${row.maxAccessCount}`
          : String(row.accessCount ?? 0)
      },
    },
    {
      key: 'requirePassword',
      title: 'columns.requirePassword',
      align: 'center',
      render: (r) =>
        asShare(r).requirePassword
          ? h(NTag, { size: 'small', type: 'warning', bordered: false }, { default: () => t('passwordRequired') })
          : '—',
    },
    {
      key: 'status',
      title: 'columns.status',
      render: (r) => {
        const row = asShare(r)
        if (!row.isEnabled) return h(NTag, { size: 'small', bordered: false }, { default: () => t('status.disabled') })
        if (row.isExpired) return h(NTag, { size: 'small', type: 'error', bordered: false }, { default: () => t('status.expired') })
        if (row.isExhausted) return h(NTag, { size: 'small', type: 'error', bordered: false }, { default: () => t('status.exhausted') })
        return h(NTag, { size: 'small', type: 'success', bordered: false }, { default: () => t('status.active') })
      },
    },
    {
      key: 'expiresAt',
      title: 'columns.expiresAt',
      render: (r) => {
        const v = asShare(r).expiresAt
        return v ? formatDateTime(v) : '—'
      },
    },
    {
      key: 'creationTime',
      title: 'columns.creationTime',
      render: (r) => formatDateTime(asShare(r).creationTime),
    },
  ]
}

// Advanced search fields (drive query.filters). Free-text keyword is unused by
// the backend share query — these typed filters are the supported surface.
export const shareSearchFields: FormSchemaItem[] = [
  { key: 'fileId', labelKey: 'search.fileId', label: 'File ID', type: 'text' },
  { key: 'creatorId', labelKey: 'search.creatorId', label: 'Creator ID', type: 'text' },
  { key: 'includeExpired', labelKey: 'search.includeExpired', label: 'Include expired', type: 'switch' },
  { key: 'includeDisabled', labelKey: 'search.includeDisabled', label: 'Include disabled', type: 'switch' },
]
