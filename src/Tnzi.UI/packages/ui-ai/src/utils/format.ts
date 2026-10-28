/** Format large numbers compactly: 1500 → 1.5K, 1500000 → 1.5M */
/**
 * When something resets, for a sentence like "available again at about {time}": the time
 * of day when it is today, else the day and the time. Null for a missing or unreadable date.
 */
export function formatResetTime(iso: string | null | undefined, now: Date = new Date(), locale?: string): string | null {
  if (!iso) return null;
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return null;
  const sameDay = at.toDateString() === now.toDateString();
  return sameDay
    ? at.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })
    : at.toLocaleString(locale, { weekday: 'short', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export function formatCompactNumber(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`;
  return n.toLocaleString();
}
