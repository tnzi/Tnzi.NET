import { h } from 'vue'
import { NTag, NPopover } from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { BroadcastTargetType, type BroadcastLogDto } from '@tnzi/core/services/chat'
import { formatDateTime } from '@tnzi/core'

function targetKey(type: BroadcastTargetType): string {
  return type === BroadcastTargetType.All ? 'all'
    : type === BroadcastTargetType.Roles ? 'roles'
      : 'users'
}

function targetTone(type: BroadcastTargetType): 'success' | 'info' | 'default' {
  return type === BroadcastTargetType.All ? 'success'
    : type === BroadcastTargetType.Roles ? 'info'
      : 'default'
}

function targetTag(t: (k: string) => string, r: BroadcastLogDto) {
  return h(
    NTag,
    { size: 'small', type: targetTone(r.targetType), bordered: false },
    { default: () => r.targetSummary || t(`targetType.${targetKey(r.targetType)}`) },
  )
}

/**
 * Content cell: shows a single-line ellipsised preview that, on click, opens a
 * popover with the full broadcast body. Replaces a separate "view" column so the
 * whole table fits inside the host NModal without horizontal clipping.
 */
function contentCell(r: BroadcastLogDto) {
  return h(
    NPopover,
    {
      trigger: 'click',
      placement: 'top-end',
      // Above the host NModal (~2000) so the popover isn't clipped behind it.
      zIndex: 4000,
      style: 'max-width: 360px; max-height: 280px; overflow: auto',
    },
    {
      trigger: () =>
        h(
          'div',
          {
            title: r.content,
            style: 'cursor: pointer; color: var(--n-primary-color, #2080f0); overflow: hidden; text-overflow: ellipsis; white-space: nowrap',
          },
          r.content || '-',
        ),
      default: () =>
        h('div', { style: 'white-space: pre-wrap; word-break: break-word; font-size: 13px; line-height: 1.6' }, r.content),
    },
  )
}

export function buildBroadcastColumns(t: (k: string) => string): DataTableColumns<BroadcastLogDto> {
  return [
    { title: t('history.columns.time'), key: 'creationTime', width: 150, render: (r) => formatDateTime(r.creationTime) },
    { title: t('history.columns.sender'), key: 'senderName', width: 90, render: (r) => r.senderName || '-' },
    { title: t('history.columns.target'), key: 'targetType', width: 110, render: (r) => targetTag(t, r) },
    { title: t('history.columns.recipients'), key: 'recipientCount', width: 80 },
    { title: t('history.columns.content'), key: 'content', render: (r) => contentCell(r) },
  ]
}
