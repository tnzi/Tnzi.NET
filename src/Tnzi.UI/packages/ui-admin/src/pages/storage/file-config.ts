import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const fileColumns: ColumnDef[] = [
  { key: 'originalName', title: 'Name' },
  { key: 'size',         title: 'Size' },
  { key: 'contentType',  title: 'Type' },
  { key: 'creatorName',  title: 'Uploaded By' },
  { key: 'creationTime', title: 'Uploaded At' },
  { key: 'md5Hash',      title: 'Hash', visible: false },
]

export const fileFormSchema: FormSchemaItem[] = [
  { key: 'originalName', label: 'Name',        type: 'text' },
  { key: 'provider',     label: 'Provider',    type: 'text' },
]
