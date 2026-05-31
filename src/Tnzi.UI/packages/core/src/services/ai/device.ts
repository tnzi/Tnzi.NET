/**
 * Device admin API wrappers — `/admin/devices*` exposed by
 * `Tnzi.AI.Device.Controllers.Admin.DefaultDeviceAdminController`.
 *
 * Surfaces connected device nodes + pending pairing requests, and exposes
 * approve / reject / revoke operations. `Tnzi.AI.Device` is an optional
 * sub-module of `Tnzi.AI`; the backend returns 404 when not loaded.
 */

import type { HttpClient } from '../../http/http';

// ---------------------------------------------------------------------------
// Enums (numeric unions mirroring backend Tnzi.AI.Device enums)
// ---------------------------------------------------------------------------

/** Device platform: 0=Unknown / 1=Windows / 2=macOS / 3=Linux / 4=iOS / 5=Android */
export type DevicePlatform = 0 | 1 | 2 | 3 | 4 | 5;

/** Device connection state: 0=Connected / 1=Disconnected / 2=Pairing */
export type DeviceConnectionState = 0 | 1 | 2;

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

/** A registered device node. */
export interface DeviceNodeDto {
  nodeId: string;
  name: string;
  platform: DevicePlatform;
  state: DeviceConnectionState;
  capabilities: string[];
}

/**
 * An open device pairing request awaiting operator approval.
 *
 * Mirrors backend `PendingPairingRequestDto`. The 8-char pairing `Code` is a
 * device-side secret and is deliberately OMITTED from the admin list response
 * (see `Tnzi.AI.Device.Models.PairingRequest` — "must not appear in API list
 * responses"), so it is intentionally absent here.
 */
export interface PairingRequestDto {
  id: string;
  nodeId: string;
  deviceName: string;
  platform: DevicePlatform;
  /** ISO-8601 timestamp (backend `DateTimeOffset`). */
  createdAt: string;
  /** ISO-8601 timestamp (backend `DateTimeOffset`). */
  expiresAt: string;
}

/**
 * Pairing info returned after approving a request.
 *
 * Mirrors backend `DevicePairingInfo`; `capabilities` items are serialized
 * `DeviceCapability` instances (`{ family; commands; requiredLevel }`).
 */
export interface DevicePairingInfoDto {
  nodeId: string;
  name: string;
  platform: DevicePlatform;
  capabilities: Array<{ family: string; commands: string[]; requiredLevel: number }>;
}

// ---------------------------------------------------------------------------
// Admin: Device API
// Route: /admin/devices
// ---------------------------------------------------------------------------

/** Admin device-node management API */
export function useDeviceAdminApi(client: HttpClient) {
  const base = '/admin/devices';

  return {
    /** List the registered device nodes */
    getDevices: () =>
      client.get<DeviceNodeDto[]>(base),

    /** List the open pairing requests awaiting approval */
    getPendingPairings: () =>
      client.get<PairingRequestDto[]>(`${base}/pairing`),

    /** Approve a pairing request — returns the resulting device pairing info */
    approvePairing: (requestId: string) =>
      client.post<DevicePairingInfoDto>(
        `${base}/pairing/${encodeURIComponent(requestId)}/approve`,
      ),

    /** Reject a pairing request */
    rejectPairing: (requestId: string) =>
      client.post<void>(`${base}/pairing/${encodeURIComponent(requestId)}/reject`),

    /** Revoke (unregister) a device node */
    revokeDevice: (nodeId: string) =>
      client.delete<void>(`${base}/${encodeURIComponent(nodeId)}`),
  };
}
