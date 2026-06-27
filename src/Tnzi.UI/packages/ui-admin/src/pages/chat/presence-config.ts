import { h } from 'vue'
import { NTag } from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { UserPresenceStatus, type PresenceUserDto } from '@tnzi/core/services/chat'
import { formatDateTime } from '@tnzi/core'

/** Lowercase i18n segment for a presence status enum value. */
export function statusKey(s: UserPresenceStatus): string {
  switch (s) {
    case UserPresenceStatus.Online: return 'online'
    case UserPresenceStatus.Away: return 'away'
    case UserPresenceStatus.Busy: return 'busy'
    case UserPresenceStatus.Invisible: return 'invisible'
    default: return 'offline'
  }
}

/** Naive UI tag tone for a presence status. */
export function statusTone(s: UserPresenceStatus): 'success' | 'warning' | 'error' | 'default' {
  switch (s) {
    case UserPresenceStatus.Online: return 'success'
    case UserPresenceStatus.Away: return 'warning'
    case UserPresenceStatus.Busy: return 'error'
    default: return 'default'
  }
}

function statusTag(t: (k: string) => string, s: UserPresenceStatus) {
  return h(
    NTag,
    { size: 'small', type: statusTone(s), bordered: false },
    { default: () => t(statusKey(s)) },
  )
}

export function buildPresenceColumns(t: (k: string) => string): DataTableColumns<PresenceUserDto> {
  return [
    { title: t('columns.name'), key: 'name', render: (r) => r.name || r.userId },
    { title: t('columns.intentStatus'), key: 'intentStatus', width: 120, render: (r) => statusTag(t, r.intentStatus) },
    { title: t('columns.effectiveStatus'), key: 'effectiveStatus', width: 130, render: (r) => statusTag(t, r.effectiveStatus) },
    {
      title: t('columns.connection'),
      key: 'hasConnection',
      width: 120,
      render: (r) => (r.hasConnection ? t('columns.connected') : t('columns.disconnected')),
    },
    { title: t('columns.lastSeen'), key: 'lastSeenAt', width: 170, render: (r) => formatDateTime(r.lastSeenAt) },
    { title: t('columns.lastChanged'), key: 'lastChangedAt', width: 170, render: (r) => formatDateTime(r.lastChangedAt) },
  ]
}
