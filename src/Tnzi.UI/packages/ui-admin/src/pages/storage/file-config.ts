import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const fileColumns: ColumnDef[] = [
  { key: 'originalName', title: 'columns.originalName' },
  { key: 'size',         title: 'columns.size' },
  { key: 'contentType',  title: 'columns.contentType' },
  { key: 'creatorName',  title: 'columns.creatorName' },
  { key: 'creationTime', title: 'columns.creationTime' },
  { key: 'md5Hash',      title: 'columns.md5Hash', visible: false },
]

export const fileFormSchema: FormSchemaItem[] = [
  { key: 'originalName', label: 'Name',        type: 'text' },
  { key: 'provider',     label: 'Provider',    type: 'text' },
]

/**
 * Advanced search fields — exposed to TCrudPage's high-level filter row.
 * Backend accepts these via the standard FileQueryDto filter pass-through.
 */
export const fileSearchFields: FormSchemaItem[] = [
  { key: 'originalName',  label: 'columns.originalName',  type: 'text', placeholder: 'columns.originalName' },
  {
    key: 'contentType',
    label: 'columns.contentType',
    type: 'select',
    options: [
      { label: 'Image',     value: 'image/' },
      { label: 'Video',     value: 'video/' },
      { label: 'Audio',     value: 'audio/' },
      { label: 'PDF',       value: 'application/pdf' },
      { label: 'Text',      value: 'text/' },
      { label: 'Archive',   value: 'application/zip' },
    ],
  },
  { key: 'creatorName',  label: 'columns.creatorName',   type: 'text', placeholder: 'columns.creatorName' },
]
