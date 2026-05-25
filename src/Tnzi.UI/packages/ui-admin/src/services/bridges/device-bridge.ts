/**
 * Device bridge — wraps `/admin/devices*` exposed by
 * `Tnzi.AI.Device.Controllers.Admin.DefaultDeviceAdminController`.
 *
 * Surfaces connected device nodes + pending pairing requests, and exposes
 * approve / reject / revoke operations. The module is an optional sub-module
 * of Tnzi.AI; bridge returns empty on 404 so the page renders gracefully.
 */
import type { HttpClient } from '@tnzi/core/http'

export type DevicePlatform = 0 | 1 | 2 | 3 | 4 | 5
export type DeviceConnectionState = 0 | 1 | 2

export interface DeviceNodeDto {
  nodeId: string
  name: string
  platform: DevicePlatform
  state: DeviceConnectionState
  capabilities: string[]
}

export interface PairingRequestDto {
  id: string
  code: string
  nodeId: string
  deviceName: string
  platform: DevicePlatform
  createdAt: string
  expiresAt: string
}

export interface DevicePairingInfoDto {
  nodeId: string
  name: string
  platform: DevicePlatform
  capabilities: Array<{ name: string; description?: string | null; permissionLevel: number }>
}

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

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
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

  return {
    getDevices: async () =>
      unwrap<DeviceNodeDto[]>(await client.get<DeviceNodeDto[]>('/admin/devices')) ?? [],
    getPendingPairings: async () =>
      unwrap<PairingRequestDto[]>(await client.get<PairingRequestDto[]>('/admin/devices/pairing')) ?? [],
    approvePairing: async (requestId: string) =>
      unwrap<DevicePairingInfoDto | null>(
        await client.post<DevicePairingInfoDto>(
          `/admin/devices/pairing/${encodeURIComponent(requestId)}/approve`,
        ),
      ),
    rejectPairing: async (requestId: string) => {
      await client.post(`/admin/devices/pairing/${encodeURIComponent(requestId)}/reject`)
    },
    revokeDevice: async (nodeId: string) => {
      await client.delete(`/admin/devices/${encodeURIComponent(nodeId)}`)
    },
  }
}
