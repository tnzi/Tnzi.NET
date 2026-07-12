/**
 * Shared list-cell renderer for a Setting's `value` column (Parameters +
 * Dictionaries — both map to the same `SettingDto`).
 *
 * Setting values range from a short "false" to a multi-hundred-character JSON
 * blob (e.g. the global admin-theme snapshot). Rendered raw, the long ones wrap
 * over a dozen lines and blow the row height up. This clamps the preview to two
 * monospace lines with an ellipsis and exposes the full value in a
 * width/height-bounded, scrollable hover tooltip.
 */
import { h, type VNode } from 'vue'
import { NEllipsis } from 'naive-ui'

export function renderSettingValue(value?: string): VNode {
  const text = value ?? ''
  return h(
    NEllipsis,
    {
      lineClamp: 2,
      // Bound the tooltip so a giant JSON value becomes a scrollable box
      // instead of an oversized popover that overflows the viewport.
      tooltip: {
        contentStyle: {
          maxWidth: '520px',
          maxHeight: '360px',
          overflow: 'auto',
          wordBreak: 'break-all',
          whiteSpace: 'pre-wrap',
        },
      },
      style:
        'font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; word-break: break-all;',
    },
    { default: () => text },
  )
}
