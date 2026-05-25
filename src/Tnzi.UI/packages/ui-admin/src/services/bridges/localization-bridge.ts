/**
 * Localization bridge — wraps `/admin/localization/missing*` exposed by
 * `Tnzi.Localization.Controllers.Admin.DefaultLocalizationAdminController`.
 *
 * Surfaces tracked keys missing from the resource files so the admin can
 * export stubs and seed them into resource files, then clear the tracker.
 */
import type { HttpClient } from '@tnzi/core/http'

export interface MissingTranslationDto {
  culture: string
  key: string
  accessCount: number
  firstAccessTime: string
  lastAccessTime: string
}

export interface CultureMissingCountDto {
  culture: string
  missingKeyCount: number
  totalAccessCount: number
}

export interface MissingTranslationSummaryDto {
  totalMissingKeys: number
  totalAccessCount: number
  affectedCultureCount: number
  cultureBreakdown: CultureMissingCountDto[]
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

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
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

  return {
    getMissing: async (culture?: string) => {
      const qs = culture ? `?culture=${encodeURIComponent(culture)}` : ''
      return (
        unwrap<MissingTranslationDto[]>(
          await client.get<MissingTranslationDto[]>(`/admin/localization/missing${qs}`),
        ) ?? []
      )
    },
    getSummary: async () =>
      unwrap<MissingTranslationSummaryDto | null>(
        await client.get<MissingTranslationSummaryDto>('/admin/localization/missing/summary'),
      ),
    exportMissing: async (culture?: string) => {
      const qs = culture ? `?culture=${encodeURIComponent(culture)}` : ''
      return (
        unwrap<Record<string, Record<string, string>>>(
          await client.get<Record<string, Record<string, string>>>(
            `/admin/localization/missing/export${qs}`,
          ),
        ) ?? {}
      )
    },
    clearMissing: async () => {
      await client.delete('/admin/localization/missing')
    },
  }
}
