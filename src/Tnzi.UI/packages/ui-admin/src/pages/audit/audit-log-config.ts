import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

// Real AuditOperationDto fields from @tnzi/core/services/audit/types.ts
export const auditLogColumns: ColumnDef[] = [
  { key: 'userId',       title: 'User' },
  { key: 'functionName', title: 'Action' },
  { key: 'url',          title: 'Resource' },
  { key: 'ip',           title: 'IP' },
  { key: 'startTime',    title: 'Time' },
  { key: 'resultType',   title: 'Result' },
  { key: 'elapsed',      title: 'Duration (ms)' },
]

// Read-only view schema — shown in detail view only, not create/edit
export const auditLogFormSchema: FormSchemaItem[] = [
  { key: 'userId',             label: 'User',             type: 'text' },
  { key: 'functionName',       label: 'Action',           type: 'text' },
  { key: 'url',                label: 'Resource',         type: 'text' },
  { key: 'httpMethod',         label: 'Method',           type: 'text' },
  { key: 'ip',                 label: 'IP',               type: 'text' },
  { key: 'resultType',         label: 'Result',           type: 'text' },
  { key: 'message',            label: 'Message',          type: 'textarea' },
  { key: 'exception',          label: 'Exception',        type: 'textarea' },
  { key: 'requestParameters',  label: 'Request Params',   type: 'textarea' },
]
