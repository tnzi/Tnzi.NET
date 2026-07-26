/**
 * Presence Service Types - user online status
 * Aligned with Tnzi.NET backend Tnzi.Identity.Presence module (Dtos/PresenceDtos.cs).
 *
 * Presence is an independent mechanism (an extension of the Identity module); Chat
 * depends on it. Apps that only need "see who's online in real time" can use this
 * without loading the Chat module.
 */

// String enum (member name = value): the backend registers a global JsonStringEnumConverter
// (including SignalR's AddJsonProtocol), so every enum field serializes as its PascalCase
// member name; inbound params accept both the string and the legacy number.
export enum UserPresenceStatus {
  Online = 'Online',
  Away = 'Away',
  Busy = 'Busy',
  Invisible = 'Invisible',
  Offline = 'Offline',
}

/** Effective presence for a user (resolved server-side). Backend: UserPresenceDto (GET /presence). */
export interface UserPresenceDto {
  userId: string
  status: UserPresenceStatus
  lastSeenAt?: string | null
}

/** Set my manual status intent. Backend: SetPresenceDto (PUT /presence). */
export interface SetPresenceDto {
  status: UserPresenceStatus
}

/** auto-away activity report. Backend: PresenceActivityDto (POST /presence/activity). */
export interface PresenceActivityDto {
  /** true = active (returned from idle / heartbeat); false = client crossed its local idle threshold. */
  active: boolean
}

/** Presence client config. Backend: PresenceClientConfigDto (GET /presence/config). */
export interface PresenceClientConfigDto {
  /** Whether presence display (status dots / picker / avatar dot) is enabled. */
  enablePresence: boolean
  /** Whether users may set the "Invisible" status (false hides the option). */
  allowInvisible: boolean
  /** Whether idle-based auto-away is enabled. */
  autoAwayEnabled: boolean
  /** Minutes of inactivity before the client reports idle (auto-away). */
  autoAwayMinutes: number
}

/** Realtime payload for the generic presence hub (/hubs/presence → `Presence.Changed`). */
export interface PresenceChangedPayload {
  userId: string
  status: UserPresenceStatus
  lastSeenAt?: string | null
}
