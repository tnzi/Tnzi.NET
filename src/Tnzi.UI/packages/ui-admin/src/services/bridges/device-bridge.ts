/**
 * Device bridge — wraps `/admin/devices*` exposed by
 * `Tnzi.AI.Device.Controllers.Admin.DefaultDeviceAdminController`.
 *
 * Surfaces connected device nodes + pending pairing requests, and exposes
 * approve / reject / revoke operations. The module is an optional sub-module
 * of Tnzi.AI; bridge returns empty on 404 so the page renders gracefully.
 *
 * DTOs + the raw API factory live in `@tnzi/core/services/ai` (`device.ts`).
 * This bridge delegates to `useDeviceAdminApi(client)` and adds the tolerant
 * `unwrap` + empty-on-404 shaping the pages expect.
 */
import type { HttpClient } from '@tnzi/core/http'
import { useDeviceAdminApi } from '@tnzi/core/services/ai'
import { unwrapResult as unwrap } from '../_mappers'

export type {
  DevicePlatform,
  DeviceConnectionState,
  DeviceNodeDto,
  PairingRequestDto,
  DevicePairingInfoDto,
} from '@tnzi/core/services/ai'

import type {
  DeviceNodeDto,
  PairingRequestDto,
  DevicePairingInfoDto,
} from '@tnzi/core/services/ai'

export interface DeviceBridgeDeps {
  client?: HttpClient
}

export interface DeviceBridge {
  getDevices(): Promise<DeviceNodeDto[]>
  getPendingPairings(): Promise<PairingRequestDto[]>
  approvePairing(requestId: string): Promise<DevicePairingInfoDto | null>
  rejectPairing(requestId: string): Promise<void>
  revokeDevice(nodeId: string): Promise<void>
}

export function createDeviceBridge(deps: DeviceBridgeDeps = {}): DeviceBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createDeviceBridge: no HttpClient provided'))
    return {
      getDevices: noOp as never,
      getPendingPairings: noOp as never,
      approvePairing: noOp as never,
      rejectPairing: noOp as never,
      revokeDevice: noOp as never,
    }
  }

  const api = useDeviceAdminApi(client)

  return {
    getDevices: async () =>
      unwrap<DeviceNodeDto[]>(await api.getDevices()) ?? [],
    getPendingPairings: async () =>
      unwrap<PairingRequestDto[]>(await api.getPendingPairings()) ?? [],
    approvePairing: async (requestId: string) =>
      unwrap<DevicePairingInfoDto | null>(await api.approvePairing(requestId)),
    rejectPairing: async (requestId: string) => {
      await api.rejectPairing(requestId)
    },
    revokeDevice: async (nodeId: string) => {
      await api.revokeDevice(nodeId)
    },
  }
}
