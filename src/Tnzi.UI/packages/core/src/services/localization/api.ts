/**
 * Localization Module API - admin access to the missing-translation tracker.
 *
 * Mirrors `Tnzi.Localization/Controllers/Admin/DefaultLocalizationAdminController`
 * - list missing keys, aggregate summary, export stubs, and clear the tracker
 * under `/admin/localization/missing*`.
 */

import type { HttpClient } from '../../http/http';
import type {
  MissingTranslationDto,
  MissingTranslationSummaryDto,
} from './types';

const ADMIN_LOCALIZATION_BASE = '/admin/localization';

/**
 * Admin Localization API - missing-translation tracking.
 *
 * Example:
 * ```ts
 * const api = useAdminLocalizationApi(client);
 * const missing = await api.getMissing('zh-CN');
 * const summary = await api.getSummary();
 * const stubs = await api.exportMissing();   // { culture: { key: '' } }
 * await api.clearMissing();
 * ```
 */
export function useAdminLocalizationApi(client: HttpClient) {
  return {
    /** Tracked missing keys, optionally filtered to a single culture. */
    getMissing: (culture?: string) =>
      client.get<MissingTranslationDto[]>(
        `${ADMIN_LOCALIZATION_BASE}/missing${culture ? `?culture=${encodeURIComponent(culture)}` : ''}`,
      ),

    /** Aggregate summary of missing keys across cultures. */
    getSummary: () =>
      client.get<MissingTranslationSummaryDto>(`${ADMIN_LOCALIZATION_BASE}/missing/summary`),

    /** Export missing keys as a culture → key → '' stub map for seeding. */
    exportMissing: (culture?: string) =>
      client.get<Record<string, Record<string, string>>>(
        `${ADMIN_LOCALIZATION_BASE}/missing/export${culture ? `?culture=${encodeURIComponent(culture)}` : ''}`,
      ),

    /** Clear the missing-translation tracker. */
    clearMissing: () =>
      client.delete(`${ADMIN_LOCALIZATION_BASE}/missing`),
  };
}
