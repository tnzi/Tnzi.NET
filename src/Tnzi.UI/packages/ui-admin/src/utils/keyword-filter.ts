/**
 * Keyword filter for lists loaded whole: pages that hold the full set (chart
 * of accounts, fiscal years, tax agencies / rates / codes, CLI runtimes,
 * persisted tool permission rules) and bridges that page a flat array through
 * `pageArray` (function modules, settings, scheduled jobs, features).
 *
 * Their endpoints take no keyword, so the list shell's search box filters the
 * loaded set here; without it the box was shown and silently ignored, which
 * reads as "no such record". Lists served page by page filter on the server
 * instead, and a server list with no keyword at all hides the box.
 *
 * `fields` names what the keyword is matched against: the text a user sees on
 * the row. Matching anything wider (the row's JSON, say) lets a field name or
 * a `true` match every row.
 */
export function filterByKeyword<T>(
  items: readonly T[],
  searchText: string | null | undefined,
  fields: (row: T) => ReadonlyArray<string | null | undefined>,
): T[] {
  const keyword = (searchText ?? '').trim().toLowerCase()
  if (!keyword) return [...items]
  return items.filter((row) => fields(row).some((value) => (value ?? '').toLowerCase().includes(keyword)))
}
