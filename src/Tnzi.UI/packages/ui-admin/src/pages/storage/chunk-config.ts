import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

// Aligned to Tnzi.Storage.Dtos.FileChunkAuditDto (Plan E 2026-04-14).
export const chunkColumns: ColumnDef[] = [
  { key: 'id',               title: 'Chunk ID' },
  { key: 'uploadSessionId',  title: 'Upload Session' },
  { key: 'chunkIndex',       title: 'Index' },
  { key: 'chunkSize',        title: 'Size (bytes)' },
  { key: 'md5Hash',          title: 'MD5' },
  { key: 'creationTime',     title: 'Created At' },
]

export const chunkFormSchema: FormSchemaItem[] = [
  { key: 'id',               label: 'Chunk ID',        type: 'text' },
  { key: 'uploadSessionId',  label: 'Upload Session',  type: 'text' },
  { key: 'chunkIndex',       label: 'Chunk Index',     type: 'number' },
  { key: 'chunkSize',        label: 'Size (bytes)',    type: 'number' },
  { key: 'md5Hash',          label: 'MD5',             type: 'text' },
]
