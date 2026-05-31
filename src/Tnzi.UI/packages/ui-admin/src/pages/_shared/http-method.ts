/**
 * Maps an HTTP method string to a Naive UI tag tone, so request-method
 * columns / chips share one colour scheme across the system pages
 * (access log, diagnostics, performance).
 *
 *   GET            → info     (read)
 *   POST           → success  (create)
 *   PUT / PATCH    → warning  (update)
 *   DELETE         → error    (destroy)
 *   anything else  → default
 *
 * The param is optional / case-insensitive; unknown or missing methods
 * fall back to `'default'`.
 */
export function methodTone(
  method?: string,
): 'info' | 'success' | 'warning' | 'error' | 'default' {
  switch (method?.toUpperCase()) {
    case 'GET': return 'info'
    case 'POST': return 'success'
    case 'PUT':
    case 'PATCH': return 'warning'
    case 'DELETE': return 'error'
    default: return 'default'
  }
}
