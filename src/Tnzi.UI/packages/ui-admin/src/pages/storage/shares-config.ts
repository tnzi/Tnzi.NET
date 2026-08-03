import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import { NTag } from 'naive-ui'
import type { StatusType } from '@tnzi/ui'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import { formatDateTime } from '@tnzi/core'
import type { FileShareSummaryDto } from '@tnzi/core/services/storage'

// Aligned to Tnzi.Storage.Dtos.FileShareSummaryDto. `allColumns` is typed as
// the untyped `ColumnDef[]`, so the render row is cast to the DTO locally.
const asShare = (row: Record<string, unknown>) => row as unknown as FileShareSummaryDto

// Absolute i18n namespace - TStatusBadge's admin wrapper resolves `labelKey`
// from the locale root, so mapping keys must be fully qualified.
const SHARE_NS = 'admin.modules.storage.shares'

// The share's effective state is derived (priority: disabled > expired >
// exhausted > active) into a single token, then mapped to the status pill.
const shareStatusMapping: Record<string, { type: StatusType; labelKey: string }> = {
  disabled: { type: 'default', labelKey: `${SHARE_NS}.status.disabled` },
  expired: { type: 'error', labelKey: `${SHARE_NS}.status.expired` },
  exhausted: { type: 'error', labelKey: `${SHARE_NS}.status.exhausted` },
  active: { type: 'success', labelKey: `${SHARE_NS}.status.active` },
}

function shareState(row: FileShareSummaryDto): 'disabled' | 'expired' | 'exhausted' | 'active' {
  if (!row.isEnabled) return 'disabled'
  if (row.isExpired) return 'expired'
  if (row.isExhausted) return 'exhausted'
  return 'active'
}

/** Share columns (translate fn injected by the page for status / password value labels). */
export function buildShareColumns(t: (key: string) => string): ColumnDef[] {
  return [
    { key: 'originalName', title: 'columns.originalName', primary: true, ellipsis: { tooltip: true } },
    { key: 'shareToken', title: 'columns.shareToken', ellipsis: { tooltip: true } },
    {
      // 分享列表最常被打开是为了回答"这条链接到底有没有人用过"。
      key: 'lastAccessedAt',
      title: 'columns.lastAccessedAt',
      render: (r) => {
        const at = asShare(r).lastAccessedAt
        return at ? formatDateTime(at) : EMPTY_DASH
      },
    },
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
          : EMPTY_DASH,
    },
    {
      key: 'status',
      title: 'columns.status',
      render: (r) => h(TStatusBadge, { value: shareState(asShare(r)), mapping: shareStatusMapping }),
    },
    {
      key: 'expiresAt',
      title: 'columns.expiresAt',
      render: (r) => {
        const v = asShare(r).expiresAt
        return v ? formatDateTime(v) : EMPTY_DASH
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
// the backend share query - these typed filters are the supported surface.
export const shareSearchFields: FormSchemaItem[] = [
  { key: 'fileId', labelKey: 'search.fileId', label: 'File ID', type: 'text' },
  { key: 'creatorId', labelKey: 'search.creatorId', label: 'Creator ID', type: 'text' },
  { key: 'includeExpired', labelKey: 'search.includeExpired', label: 'Include expired', type: 'switch' },
  { key: 'includeDisabled', labelKey: 'search.includeDisabled', label: 'Include disabled', type: 'switch' },
]
