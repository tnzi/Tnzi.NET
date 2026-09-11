import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import { DevicePlatform } from '@tnzi/core/services/notification'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

/**
 * Push devices page config - aligned with PushDeviceDto
 * (Tnzi.Notification.Push, Notification_PushDevice).
 *
 * ★ There is no form schema on purpose. The page is read + delete only:
 * every field is reported by the client when it posts its own token, so an
 * operator-edited row would drift from reality, and an operator-created row
 * would match no real device at all.
 */
const PLATFORM_LABELS: Record<number, { type: 'info' | 'success' | 'warning'; labelKey: string }> = {
  [DevicePlatform.Android]: { type: 'success', labelKey: 'tnzi.admin.modules.notification.devices.platform.android' },
  [DevicePlatform.Ios]: { type: 'info', labelKey: 'tnzi.admin.modules.notification.devices.platform.ios' },
  [DevicePlatform.Web]: { type: 'warning', labelKey: 'tnzi.admin.modules.notification.devices.platform.web' },
}

const ANONYMOUS_BADGE = {
  true: { type: 'default' as const, labelKey: 'tnzi.admin.modules.notification.devices.anonymous' },
}

export const notificationDeviceColumns: ColumnDef[] = [
  // Shows the raw userId: PushDeviceDto carries no user name, and resolving it
  // would need a backend join - same call as the subscriptions page.
  //
  // ★ An empty userId is an anonymous device (an app with no accounts registers
  // by device key), so it gets an explicit badge rather than a blank cell: a
  // blank cell is indistinguishable from a row that failed to resolve.
  {
    key: 'userId',
    title: 'columns.userId',
    render: (row) =>
      row.userId
        ? String(row.userId)
        : h(TStatusBadge, { value: true, mapping: ANONYMOUS_BADGE }),
  },
  {
    key: 'platform',
    title: 'columns.platform',
    width: 120,
    render: (row) => {
      const meta = PLATFORM_LABELS[Number(row.platform)]
      return meta
        ? h(TStatusBadge, { value: true, mapping: { true: meta } })
        : String(row.platform ?? '')
    },
  },
  { key: 'deviceName', title: 'columns.deviceName' },
  // Client-reported platform identifier (IDFV / Firebase installation id / SSAID).
  // ★ Recognition only - it addresses nothing. Operators usually have this value
  // and nothing else (the token is masked and the row id never reaches a client),
  // which is the whole reason it is shown and filterable.
  { key: 'externalDeviceId', title: 'columns.externalDeviceId', width: 200, visible: false },
  // ★ Masked tail only (`…a1b2c3d4`). The full token is never sent to the
  // client: what needs protecting is not the token (it is a delivery address,
  // not a credential) but the list - one query would otherwise export
  // "who has this app installed on which devices".
  { key: 'tokenMask', title: 'columns.tokenMask', width: 160 },
  { key: 'lastSeenAt', title: 'columns.lastSeenAt', width: 180 },
  { key: 'creationTime', title: 'columns.creationTime', width: 180, visible: false },
]
