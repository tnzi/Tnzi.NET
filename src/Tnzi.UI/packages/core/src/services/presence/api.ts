/**
 * Presence API - user online status (manual intent + auto-away).
 * Backend: DefaultPresenceController [Route("presence")] (Tnzi.Identity.Presence).
 */

import type { HttpClient } from '../../http/http';
import type { ApiResult } from '../../types/api';
import { chunk } from '../../utils/array';
import type { UserPresenceDto, UserPresenceStatus, PresenceClientConfigDto } from './types';

/**
 * Most user ids sent in one `GET /presence` request.
 *
 * Every id costs about 45 bytes of query string (`userIds=<guid>&`). IIS
 * rejects a query string over 2048 bytes by default (404.15) and Kestrel's
 * request line tops out at 8 KB, so a chat host asking for all of its peers at
 * once got no presence at all past a few dozen contacts. 40 ids stay near
 * 1.8 KB.
 */
export const PRESENCE_BATCH_SIZE = 40;

export function usePresenceApi(client: HttpClient) {
  const getPresenceBatch = (userIds: string[]) =>
    client.get<UserPresenceDto[]>('/presence', { params: { userIds } });

  return {
    /** Set my manual status - PUT /presence */
    setStatus: (status: UserPresenceStatus) =>
      client.put<void>('/presence', { status }),

    /** Get my (manual) status - GET /presence/me */
    getMyStatus: () =>
      client.get<UserPresenceStatus>('/presence/me'),

    /**
     * Resolve effective status for a set of users - GET /presence?userIds=...
     *
     * Any number of ids: duplicates are collapsed and the list goes out in
     * batches of {@link PRESENCE_BATCH_SIZE}, merged back in order. The call
     * fails as a whole when any batch fails, so a caller never mistakes a
     * partial answer for "everyone else is offline".
     */
    getPresence: async (userIds: string[]): Promise<ApiResult<UserPresenceDto[]>> => {
      const unique = [...new Set(userIds)];
      if (unique.length <= PRESENCE_BATCH_SIZE) return getPresenceBatch(unique);

      const results = await Promise.all(chunk(unique, PRESENCE_BATCH_SIZE).map(getPresenceBatch));
      const failed = results.find((result) => !result.succeeded);
      if (failed) return failed;
      return { ...results[0]!, data: results.flatMap((result) => result.data ?? []) };
    },

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
