import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import { EMPTY_DASH } from '../../utils/placeholders'

/**
 * The billing account a payment, refund or subscription belongs to, shown and
 * searched the same way on the three admin payment lists.
 *
 * Shown as `type:id` (`Installation:main`, `Tenant:<guid>`, `User:<guid>`):
 * the pair is the account's identity, and neither half means anything alone.
 */
export interface BillingAccountFields {
  billingAccountType?: string | null
  billingAccountId?: string | null
}

export function formatBillingAccount(row: BillingAccountFields): string {
  const type = row.billingAccountType?.trim()
  const id = row.billingAccountId?.trim()
  if (!type && !id) return EMPTY_DASH
  return `${type || EMPTY_DASH}:${id || EMPTY_DASH}`
}

/** The built-in account types; a deployment may resolve custom ones too (searchable by id). */
export const BILLING_ACCOUNT_TYPES = ['User', 'Installation', 'Tenant'] as const

export function billingAccountColumn<T extends BillingAccountFields>(): ColumnDef<T> {
  return {
    key: 'billingAccount',
    title: 'admin.shared.billingAccount.column',
    minWidth: 180,
    render: (row) => formatBillingAccount(row),
  }
}

/** Advanced-search fields that map onto the lists' `billingAccountType` / `billingAccountId` filters. */
export function billingAccountSearchFields(): FormSchemaItem[] {
  return [
    {
      key: 'billingAccountType',
      labelKey: 'admin.shared.billingAccount.type',
      label: 'Billing account type',
      type: 'select',
      placeholderKey: 'admin.shared.billingAccount.typeAny',
      placeholder: 'Any type',
      options: BILLING_ACCOUNT_TYPES.map((value) => ({ value, label: value })),
    },
    {
      key: 'billingAccountId',
      labelKey: 'admin.shared.billingAccount.id',
      label: 'Billing account id',
      type: 'text',
      placeholderKey: 'admin.shared.billingAccount.idPlaceholder',
      placeholder: 'Account id',
    },
  ]
}
