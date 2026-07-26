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
  { key: 'fileId',       labelKey: 'form.fileId', label: 'File ID',     type: 'text' },
  { key: 'version',      labelKey: 'form.version', label: 'Version',     type: 'number' },
  { key: 'path',         labelKey: 'form.path', label: 'Path',        type: 'text' },
  { key: 'size',         labelKey: 'form.size', label: 'Size',        type: 'number' },
  { key: 'md5Hash',      labelKey: 'form.md5Hash', label: 'MD5',         type: 'text' },
  { key: 'description',  labelKey: 'form.description', label: 'Note',        type: 'textarea' },
]

// Advanced search - GetVersions filters by fileId (cross-file audit) and
// currentOnly. The backend treats currentOnly=false/absent as "all versions"
// and only filters when true, so a switch off = all, on = current-only.
export const versionSearchFields: FormSchemaItem[] = [
  { key: 'fileId',      labelKey: 'search.fileId', label: 'File ID', type: 'text' },
  { key: 'currentOnly', labelKey: 'search.currentOnly', label: 'Current only', type: 'switch' },
]
