/**
 * Localization bridge - delegates to `useAdminLocalizationApi`
 * (from `@tnzi/core/services/localization`) so admin pages get the standard
 * dependency-injection + single-mock-seam pattern other bridges use.
 *
 * Surfaces tracked keys missing from the resource files so the admin can
 * export stubs and seed them into resource files, then clear the tracker.
 *
 * DTO types are re-exported below so existing page imports keep resolving
 * after the contract moved into `@tnzi/core`.
 */
import {
  useAdminLocalizationApi,
  type MissingTranslationDto,
  type CultureMissingCountDto,
  type MissingTranslationSummaryDto,
} from '@tnzi/core/services/localization'
import { ensureOk, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminLocalizationApi>[0]

export type {
  MissingTranslationDto,
  CultureMissingCountDto,
  MissingTranslationSummaryDto,
}

export interface LocalizationBridgeDeps {
  client?: HttpClient
}

export interface LocalizationBridge {
  getMissing(culture?: string): Promise<MissingTranslationDto[]>
  getSummary(): Promise<MissingTranslationSummaryDto | null>
  exportMissing(culture?: string): Promise<Record<string, Record<string, string>>>
  clearMissing(): Promise<void>
}

export function createLocalizationBridge(deps: LocalizationBridgeDeps = {}): LocalizationBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createLocalizationBridge: no HttpClient provided'))
    return {
      getMissing: noOp as never,
      getSummary: noOp as never,
      exportMissing: noOp as never,
      clearMissing: noOp as never,
    }
  }

  const api = useAdminLocalizationApi(client)

  return {
    getMissing: async (culture?: string) =>
      unwrap<MissingTranslationDto[]>(await api.getMissing(culture)) ?? [],
    getSummary: async () =>
      unwrap<MissingTranslationSummaryDto | null>(await api.getSummary()),
    exportMissing: async (culture?: string) =>
      unwrap<Record<string, Record<string, string>>>(await api.exportMissing(culture)) ?? {},
    clearMissing: async () => {
      ensureOk(await api.clearMissing())
    },
  }
}
