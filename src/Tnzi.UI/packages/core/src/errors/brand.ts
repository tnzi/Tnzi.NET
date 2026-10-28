/**
 * @tnzi/core/errors/brand
 *
 * Cross-bundle identity for the error classes.
 *
 * This package is built by tsup with `splitting: false`, so every entry that
 * imports an error class inlines its own copy of it: `dist/state` throws its
 * `HttpError`, `dist/http` and `dist/errors` each export a different one. An
 * `instanceof` guard therefore only recognises errors created by the copy in
 * its own entry. The documented sign-in flow (`createTnziClient` from
 * `@tnzi/core/state`, `isHttpError(e) && e.errorCode === '2FA_REQUIRED'` with
 * the guard from `@tnzi/core/http`) answered false for every challenge, so a
 * user with 2FA enabled could not finish signing in.
 *
 * Each constructor stamps a `Symbol.for(...)` key on the instance, and the
 * guards accept either `instanceof` or the stamp. `Symbol.for` reads the
 * process-wide symbol registry, so every inlined copy agrees on the key - the
 * same reasoning as the adapter registry on `globalThis` (`adapters/singleton`).
 * The key strings are the only thing tying the copies together and must stay
 * stable across releases.
 */

export const API_ERROR_BRAND: symbol = Symbol.for('tnzi.core.ApiError');
export const HTTP_ERROR_BRAND: symbol = Symbol.for('tnzi.core.HttpError');
export const NETWORK_ERROR_BRAND: symbol = Symbol.for('tnzi.core.NetworkError');
export const TIMEOUT_ERROR_BRAND: symbol = Symbol.for('tnzi.core.TimeoutError');
export const ABORT_ERROR_BRAND: symbol = Symbol.for('tnzi.core.AbortError');

/** Stamp `brand` on `target` (non-enumerable, so it never shows up in logs or JSON). */
export function brandError(target: object, brand: symbol): void {
  Object.defineProperty(target, brand, { value: true, enumerable: false });
}

/** Whether `value` carries `brand`, whichever bundle copy created it. */
export function hasErrorBrand(value: unknown, brand: symbol): boolean {
  return typeof value === 'object' && value !== null && (value as Record<symbol, unknown>)[brand] === true;
}
