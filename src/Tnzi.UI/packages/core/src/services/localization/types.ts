/**
 * Localization Module Types - mirrors `Tnzi.Localization.Dtos.*` on the .NET side.
 *
 * Models the missing-translation tracker surface exposed by
 * `Tnzi.Localization/Controllers/Admin/DefaultLocalizationAdminController`
 * (`/admin/localization/missing*`). Surfaces tracked keys missing from the
 * resource files so an admin can export stubs, seed them, then clear the tracker.
 */

/** A single tracked key that was requested but missing from resources. */
export interface MissingTranslationDto {
  culture: string;
  key: string;
  accessCount: number;
  firstAccessTime: string;
  lastAccessTime: string;
}

/** Per-culture rollup of missing-key counts. */
export interface CultureMissingCountDto {
  culture: string;
  missingKeyCount: number;
  totalAccessCount: number;
}

/** Aggregate summary across all cultures. */
export interface MissingTranslationSummaryDto {
  totalMissingKeys: number;
  totalAccessCount: number;
  affectedCultureCount: number;
  cultureBreakdown: CultureMissingCountDto[];
}
