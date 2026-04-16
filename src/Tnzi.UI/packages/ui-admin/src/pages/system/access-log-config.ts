import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

// Real AccessLogInfoDto fields from @tnzi/core/services/system/types.ts
export const accessLogColumns: ColumnDef[] = [
  { key: 'path',         title: 'Path' },
  { key: 'method',       title: 'Method' },
  { key: 'statusCode',   title: 'Status' },
  { key: 'responseTime', title: 'Duration (ms)' },
  { key: 'ipAddress',    title: 'IP' },
  { key: 'userName',     title: 'User' },
  { key: 'creationTime', title: 'Requested At' },
]

// Read-only view schema — form is shown for detail view only, not create/edit
export const accessLogFormSchema: FormSchemaItem[] = [
  { key: 'path',        label: 'Path',        type: 'text' },
  { key: 'method',      label: 'Method',      type: 'text' },
  { key: 'statusCode',  label: 'Status Code', type: 'number' },
  { key: 'responseTime', label: 'Duration',   type: 'number' },
  { key: 'requestBody', label: 'Payload',     type: 'textarea' },
  { key: 'responseBody', label: 'Response',   type: 'textarea' },
]
