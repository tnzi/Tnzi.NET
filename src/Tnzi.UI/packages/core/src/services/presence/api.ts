/**
 * Presence API - user online status (manual intent + auto-away).
 * Backend: DefaultPresenceController [Route("presence")] (Tnzi.Identity.Presence).
 */

import type { HttpClient } from '../../http/http';
import type { UserPresenceDto, UserPresenceStatus, PresenceClientConfigDto } from './types';

export function usePresenceApi(client: HttpClient) {
  return {
    /** Set my manual status - PUT /presence */
    setStatus: (status: UserPresenceStatus) =>
      client.put<void>('/presence', { status }),

    /** Get my (manual) status - GET /presence/me */
    getMyStatus: () =>
      client.get<UserPresenceStatus>('/presence/me'),

    /** Resolve effective status for a batch of users - GET /presence?userIds=... */
    getPresence: (userIds: string[]) =>
      client.get<UserPresenceDto[]>('/presence', { params: { userIds } }),

    /** Report activity / idle for auto-away - POST /presence/activity.
     *  `active:true` = returned from idle (server pushes Online); `active:false` = crossed
     *  the local idle threshold (server resolves Away + pushes). */
    reportActivity: (active: boolean) =>
      client.post<void>('/presence/activity', { active }),

    /** Presence client config (auto-away threshold, invisible toggle) - GET /presence/config */
    getConfig: () =>
      client.get<PresenceClientConfigDto>('/presence/config'),
  };
}
