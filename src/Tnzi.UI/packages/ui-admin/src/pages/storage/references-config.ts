import { h } from 'vue'
import { NTag } from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import type { FileReferenceDto } from '@tnzi/core/services/storage'
import { formatDateTime } from '@tnzi/core'

/** File-reference columns (translate fn injected by the page). */
export function buildReferenceColumns(
  t: (key: string) => string,
): DataTableColumns<FileReferenceDto> {
  return [
    { key: 'entityType', title: t('columns.entityType'), ellipsis: { tooltip: true } },
    { key: 'entityId', title: t('columns.entityId'), ellipsis: { tooltip: true } },
    { key: 'fieldName', title: t('columns.fieldName'), ellipsis: { tooltip: true } },
    {
      key: 'isTemporary',
      title: t('columns.isTemporary'),
      align: 'center',
      render: (row) =>
        row.isTemporary
          ? h(NTag, { size: 'small', type: 'warning', bordered: false }, { default: () => t('temporary') })
          : h(NTag, { size: 'small', type: 'success', bordered: false }, { default: () => t('permanent') }),
    },
    {
      key: 'creationTime',
      title: t('columns.creationTime'),
      render: (row) => formatDateTime(row.creationTime),
    },
  ]
}
