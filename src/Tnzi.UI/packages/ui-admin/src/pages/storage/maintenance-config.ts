import type { DataTableColumns } from 'naive-ui'
import type { FileRecordDto } from '@tnzi/core/services/storage'
import { formatDateTime, formatFileSize } from '@tnzi/core'

/** Temporary-file columns (translate fn injected by the page). */
export function buildTemporaryColumns(
  t: (key: string) => string,
): DataTableColumns<FileRecordDto> {
  return [
    { key: 'originalName', title: t('columns.originalName'), ellipsis: { tooltip: true } },
    {
      key: 'size',
      title: t('columns.size'),
      align: 'right',
      render: (row) => formatFileSize(row.size),
    },
    { key: 'contentType', title: t('columns.contentType'), ellipsis: { tooltip: true } },
    { key: 'provider', title: t('columns.provider') },
    {
      key: 'creationTime',
      title: t('columns.creationTime'),
      render: (row) => formatDateTime(row.creationTime),
    },
  ]
}
