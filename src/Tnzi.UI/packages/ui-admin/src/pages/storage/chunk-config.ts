import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

// Aligned to Tnzi.Storage.Dtos.FileChunkAuditDto (Plan E 2026-04-14).
export const chunkColumns: ColumnDef[] = [
  { key: 'id',               title: 'columns.id' },
  { key: 'uploadSessionId',  title: 'columns.uploadSessionId' },
  { key: 'chunkIndex',       title: 'columns.chunkIndex' },
  { key: 'chunkSize',        title: 'columns.chunkSize' },
  { key: 'md5Hash',          title: 'columns.md5Hash' },
  { key: 'creationTime',     title: 'columns.creationTime' },
]

export const chunkFormSchema: FormSchemaItem[] = [
  { key: 'id',               labelKey: 'form.id', label: 'Chunk ID',        type: 'text' },
  { key: 'uploadSessionId',  labelKey: 'form.uploadSessionId', label: 'Upload Session',  type: 'text' },
  { key: 'chunkIndex',       labelKey: 'form.chunkIndex', label: 'Chunk Index',     type: 'number' },
  { key: 'chunkSize',        labelKey: 'form.chunkSize', label: 'Size (bytes)',    type: 'number' },
  { key: 'md5Hash',          labelKey: 'form.md5Hash', label: 'MD5',             type: 'text' },
]
