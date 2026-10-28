import { describe, it, expect, vi } from 'vitest'
import { isVNode, type VNode } from 'vue'
import { DevicePlatform } from '@tnzi/core/services/notification'
import { notificationDeviceColumns } from '../../../src/pages/notification/device-config'
import { createNotificationBridge } from '../../../src/services/bridges/notification-bridge'

/**
 * The server writes the device platform by member name ("Android"). The badge
 * map used to be keyed by number and looked the row up with
 * `Number(row.platform)`, which is NaN for every real row, so the page showed
 * the raw member name and never a badge.
 */
describe('push device platform', () => {
  const column = notificationDeviceColumns.find((c) => c.key === 'platform')!

  it.each([
    ['Android', 'android'],
    ['Ios', 'ios'],
    ['Web', 'web'],
  ])('renders the %s badge for the wire value', (wire, suffix) => {
    const cell = column.render!({ platform: wire } as never) as VNode
    expect(isVNode(cell)).toBe(true)
    const mapping = (cell.props as { mapping: Record<string, { labelKey: string }> }).mapping
    expect(mapping.true.labelKey).toBe(`tnzi.admin.modules.notification.devices.platform.${suffix}`)
  })

  it('falls back to the raw value for a platform it does not know', () => {
    expect(column.render!({ platform: 'Tv' } as never)).toBe('Tv')
  })

  it('sends the platform filter by member name and drops unknown values', async () => {
    const getList = vi.fn(async () => ({ succeeded: true, data: { items: [], totalCount: 0, pageIndex: 1, pageSize: 20 } }))
    const bridge = createNotificationBridge({ notificationApi: {} as never, deviceApi: { getList } as never })

    await bridge.devices.fetch({ pageIndex: 1, pageSize: 20, filters: { platform: DevicePlatform.Ios } })
    expect(getList).toHaveBeenLastCalledWith(expect.objectContaining({ platform: 'Ios' }))

    await bridge.devices.fetch({ pageIndex: 1, pageSize: 20, filters: { platform: 2 } })
    expect(getList).toHaveBeenLastCalledWith(expect.objectContaining({ platform: undefined }))
  })
})
