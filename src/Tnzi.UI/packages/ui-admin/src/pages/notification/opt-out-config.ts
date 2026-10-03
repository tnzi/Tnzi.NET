import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { formatDateTime } from '@tnzi/core'

/**
 * Opt-outs page config - aligned with OptOutDto (Tnzi.Notification, Notification_OptOut).
 *
 * The row is keyed by ADDRESS, not user: a bulk recipient is often not a user
 * of this system at all (imported lists, former customers, closed accounts).
 * `category` empty means the whole channel - shown as a badge rather than a
 * blank cell, because a blank cell reads like a row that failed to resolve.
 */
const WHOLE_CHANNEL_BADGE = {
  true: { type: 'warning' as const, labelKey: 'tnzi.admin.modules.notification.optOuts.wholeChannel' },
}

export const notificationOptOutColumns: ColumnDef[] = [
  { key: 'address', title: 'columns.address' },
  { key: 'channel', title: 'columns.channel', width: 100 },
  {
    key: 'category',
    title: 'columns.category',
    width: 160,
    render: (row) =>
      row.category
        ? String(row.category)
        : h(TStatusBadge, { value: true, mapping: WHOLE_CHANNEL_BADGE }),
  },
  // "one-click link" / "one-click header" come from the recipient's own click;
  // "admin:{userId}" from this page. The two must stay tellable apart: a
  // compliance answer has to say whether the recipient asked or an operator did.
  { key: 'source', title: 'columns.source', width: 200, render: (row) => String(row.source || EMPTY_DASH) },
  { key: 'reason', title: 'columns.reason', visible: false, render: (row) => String(row.reason || EMPTY_DASH) },
  { key: 'creationTime', title: 'columns.creationTime', width: 180, render: (row) => formatDateTime(row.creationTime as string) },
]

/**
 * Hand-registration form (provider complaint, phone call, imported blocklist).
 * There is no edit form: the (address, channel, category) triple is the row's
 * identity, so changing one is a revoke plus a new registration.
 */
export const notificationOptOutFormSchema: FormSchemaItem[] = [
  { key: 'address', labelKey: 'form.address', label: 'Address', type: 'text', required: true, placeholderKey: 'form.addressPlaceholder' },
  // Only the channels that deliver - the preference form lists InApp / Webhook
  // too, but an opt-out row for a channel that never sends is never consulted.
  { key: 'channel', labelKey: 'form.channel', label: 'Channel', type: 'select', required: true, options: [
    { label: 'Email', value: 'Email' },
    { label: 'SMS', value: 'Sms' },
    { label: 'Push', value: 'Push' },
    { label: 'Fax', value: 'Fax' },
  ] },
  { key: 'category', labelKey: 'form.category', label: 'Category (optional)', type: 'text', placeholderKey: 'form.categoryPlaceholder' },
  { key: 'reason', labelKey: 'form.reason', label: 'Reason (optional)', type: 'textarea' },
]
