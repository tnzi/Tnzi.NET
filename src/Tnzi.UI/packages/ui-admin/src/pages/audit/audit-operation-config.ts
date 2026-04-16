import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

// Real AuditOperationDto fields from @tnzi/core/services/audit/types.ts
export const auditOperationColumns: ColumnDef[] = [
  { key: 'functionName',  title: 'Operation' },
  { key: 'permissionName', title: 'Permission' },
  { key: 'userId',        title: 'Operator' },
  { key: 'httpMethod',    title: 'Method' },
  { key: 'startTime',     title: 'Time' },
  { key: 'elapsed',       title: 'Duration (ms)' },
  { key: 'resultType',    title: 'Result' },
]

// Read-only view schema — shown in detail view only, not create/edit
export const auditOperationFormSchema: FormSchemaItem[] = [
  { key: 'functionName',      label: 'Operation',         type: 'text' },
  { key: 'permissionName',    label: 'Permission',        type: 'text' },
  { key: 'userId',            label: 'Operator',          type: 'text' },
  { key: 'ip',                label: 'IP',                type: 'text' },
  { key: 'httpMethod',        label: 'Method',            type: 'text' },
  { key: 'url',               label: 'URL',               type: 'text' },
  { key: 'httpStatusCode',    label: 'Status Code',       type: 'number' },
  { key: 'elapsed',           label: 'Duration (ms)',     type: 'number' },
  { key: 'resultType',        label: 'Result',            type: 'text' },
  { key: 'message',           label: 'Message',           type: 'textarea' },
  { key: 'exception',         label: 'Exception',         type: 'textarea' },
  { key: 'requestParameters', label: 'Request Params',    type: 'textarea' },
  { key: 'responseResult',    label: 'Response',          type: 'textarea' },
]
