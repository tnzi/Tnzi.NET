import type { DataTableColumns } from 'naive-ui'
import type { UserStorageUsageDto } from '@tnzi/core/services/storage'

/** Per-user storage usage columns (translate fn injected by the page). */
export function buildUsageColumns(
  t: (key: string) => string,
): DataTableColumns<UserStorageUsageDto> {
  return [
    {
      key: 'userId',
      title: t('columns.userId'),
      ellipsis: { tooltip: true },
      render: (row) => row.userId ?? '—',
    },
    {
      key: 'fileCount',
      title: t('columns.fileCount'),
      align: 'right',
      render: (row) => (row.fileCount ?? 0).toLocaleString(),
    },
    {
      key: 'formattedSize',
      title: t('columns.size'),
      align: 'right',
      render: (row) => row.formattedSize || String(row.totalSize ?? 0),
    },
  ]
}
