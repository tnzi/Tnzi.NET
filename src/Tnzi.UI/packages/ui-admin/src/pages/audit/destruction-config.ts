/**
 * Column and filter config for destruction certificates.
 *
 * `heldCount` earns a column of its own: without it a "destroyed 3" row cannot
 * tell you whether only three records were due or twenty-seven were skipped by
 * a litigation hold.
 */
import type { FormSchemaItem } from '@tnzi/ui'
import type { ColumnDef } from '../../headless/useColumnSettings'

export const destructionColumns: ColumnDef[] = [
  { key: 'creationTime', title: 'columns.creationTime', width: 170 },
  { key: 'policyName', title: 'columns.policyName', width: 200 },
  { key: 'entityType', title: 'columns.entityType', width: 220 },
  { key: 'destroyedCount', title: 'columns.destroyedCount', width: 110 },
  { key: 'heldCount', title: 'columns.heldCount', width: 110 },
  { key: 'mode', title: 'columns.mode', width: 140 },
  { key: 'sequence', title: 'columns.sequence', width: 90 },
]

export const destructionSearchFields: FormSchemaItem[] = [
  { key: 'policyName', label: 'columns.policyName', type: 'input' },
  {
    key: 'isDryRun',
    label: 'columns.isDryRun',
    type: 'select',
    // Values are strings: FormSchemaItem options are `string | number`, and the
    // page converts back to a boolean before it reaches the API.
    options: [
      { label: 'filters.realOnly', value: 'false' },
      { label: 'filters.dryRunOnly', value: 'true' },
    ],
  },
]
