import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import { NTag } from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import type { FileIntegrityResultDto, FileIntegrityStatus } from '@tnzi/core/services/storage'

const STATUS_TONE: Record<FileIntegrityStatus, 'success' | 'warning' | 'error'> = {
  Healthy: 'success',
  Missing: 'error',
  Corrupted: 'error',
  Error: 'warning',
}

/** Problem-files table columns (translate fn injected by the page). */
export function buildProblemColumns(
  t: (key: string) => string,
): DataTableColumns<FileIntegrityResultDto> {
  return [
    { key: 'originalName', title: t('columns.originalName'), ellipsis: { tooltip: true } },
    { key: 'fileId', title: t('columns.fileId'), ellipsis: { tooltip: true } },
    {
      key: 'physicalFileExists',
      title: t('columns.physicalFileExists'),
      align: 'center',
      render: (row) => (row.physicalFileExists ? '✓' : '✗'),
    },
    {
      key: 'status',
      title: t('columns.status'),
      render: (row) =>
        h(
          NTag,
          { size: 'small', type: STATUS_TONE[row.status] ?? 'default', bordered: false },
          { default: () => t(`status.${row.status}`) },
        ),
    },
    {
      key: 'error',
      title: t('columns.error'),
      ellipsis: { tooltip: true },
      render: (row) => row.error ?? EMPTY_DASH,
    },
  ]
}
