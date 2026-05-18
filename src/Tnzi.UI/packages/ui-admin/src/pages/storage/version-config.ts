import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

// Aligned to Tnzi.Storage.Dtos.FileVersionAuditDto (Plan E 2026-04-14).
export const versionColumns: ColumnDef[] = [
  { key: 'fileId',        title: 'columns.fileId' },
  { key: 'version',       title: 'columns.version' },
  { key: 'size',          title: 'columns.size' },
  { key: 'isCurrent',     title: 'columns.isCurrent' },
  { key: 'creatorId',     title: 'columns.creatorId' },
  { key: 'creationTime',  title: 'columns.creationTime' },
  { key: 'description',   title: 'columns.description' },
]

export const versionFormSchema: FormSchemaItem[] = [
  { key: 'fileId',       label: 'File ID',     type: 'text' },
  { key: 'version',      label: 'Version',     type: 'number' },
  { key: 'path',         label: 'Path',        type: 'text' },
  { key: 'size',         label: 'Size',        type: 'number' },
  { key: 'md5Hash',      label: 'MD5',         type: 'text' },
  { key: 'description',  label: 'Note',        type: 'textarea' },
]
