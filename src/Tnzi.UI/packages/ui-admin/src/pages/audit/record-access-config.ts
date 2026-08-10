/**
 * Column and filter config for the record-level read trail.
 *
 * The columns answer the two questions this table exists for: "who opened this
 * record" and "what did this person open". Everything else (hash, sequence)
 * belongs in the detail drawer - it is evidence, not something you scan a list by.
 */
import type { FormSchemaItem } from '@tnzi/ui'
import type { ColumnDef } from '../../headless/useColumnSettings'

export const recordAccessColumns: ColumnDef[] = [
  { key: 'creationTime', title: 'columns.creationTime', width: 170 },
  { key: 'userName', title: 'columns.userName', width: 150 },
  { key: 'resourceType', title: 'columns.resourceType', width: 200 },
  { key: 'resourceId', title: 'columns.resourceId', width: 200 },
  { key: 'purpose', title: 'columns.purpose', width: 140 },
  { key: 'sequence', title: 'columns.sequence', width: 90 },
]

/**
 * Filters.
 *
 * `resourceType` + `resourceId` together are the compliance question
 * ("who viewed this file"); `userId` alone is the abuse question
 * ("what has this account been reading").
 */
export const recordAccessSearchFields: FormSchemaItem[] = [
  { key: 'resourceType', label: 'columns.resourceType', type: 'input' },
  { key: 'resourceId', label: 'columns.resourceId', type: 'input' },
  { key: 'purpose', label: 'columns.purpose', type: 'input' },
]
