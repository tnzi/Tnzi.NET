/**
 * Boot-time check that the naive-ui the app actually loaded is new enough for
 * the APIs this package calls.
 *
 * ## Why a runtime check exists at all
 *
 * `peerDependencies` is the normal way to express this, and it IS declared
 * (`naive-ui: ^2.45.0`). It just does not reach the consumers this framework
 * actually has:
 *
 * - **pnpm does not check peerDependencies for packages consumed via `link:`.**
 *   Every local consumer links `@tnzi/*` straight into the framework checkout,
 *   so `pnpm install` is silent no matter how far behind their naive-ui is.
 * - Those consumers also alias naive-ui to their OWN `node_modules` in
 *   `vite.config.ts` (to avoid a second instance), which means this package's
 *   code executes against the CONSUMER's copy. The version that governs is
 *   theirs, not the one resolved next to this package.
 *
 * Measured 2026-08-20: a consumer sat on naive-ui 2.44.1 while linking a
 * `@tnzi/ui-admin` that calls `form.validate(cb, string[])`. That path-filter
 * overload landed in 2.45; on 2.44 the array reaches `Array.prototype.filter`
 * and throws `filter is not a function`, from inside naive-ui, only when the
 * user clicks a send-code button. Nothing anywhere warned first.
 *
 * So the failure was already loud - just late and unattributable. This turns it
 * into one line at boot that names the version and the reason.
 *
 * ## Why it is dev-only
 *
 * In production the version is whatever shipped; a console error there helps
 * nobody and costs every user a string. The check is for the person wiring the
 * app up.
 *
 * ## Keeping the constant honest
 *
 * `MIN_NAIVE_UI` duplicates `peerDependencies.naive-ui` from this package's
 * `package.json`, and a duplicated version floor is exactly the kind of thing
 * that drifts the next time someone raises the peer range. It does not drift
 * here because `__tests__/plugin/peer-check.test.ts` reads the manifest and
 * fails if the two disagree.
 */
import { version as naiveUiVersion } from 'naive-ui';

/** Must equal the lower bound of `peerDependencies.naive-ui`; a test enforces that. */
export const MIN_NAIVE_UI = '2.45.0';

/** `[major, minor, patch]`, missing or non-numeric segments read as 0. */
function parse(v: string): [number, number, number] {
  const [a, b, c] = v.split('.').map((n) => Number.parseInt(n, 10));
  return [a || 0, b || 0, c || 0];
}

/** True when `actual` is at least `min`. */
export function satisfiesMin(actual: string, min: string): boolean {
  // Destructured rather than indexed in a loop: under noUncheckedIndexedAccess
  // a variable index widens the tuple element to `number | undefined`.
  const [aMajor, aMinor, aPatch] = parse(actual);
  const [mMajor, mMinor, mPatch] = parse(min);
  if (aMajor !== mMajor) return aMajor > mMajor;
  if (aMinor !== mMinor) return aMinor > mMinor;
  return aPatch >= mPatch;
}

/**
 * Report a too-old naive-ui once, at boot.
 *
 * @param actual  Version to test. Defaults to the `version` naive-ui exports,
 *                which is the copy this code will actually run against.
 * @param report  Sink for the message; defaults to `console.error`. Injected so
 *                the test does not have to spy on the console.
 * @returns whether the loaded version is acceptable.
 */
export function checkNaiveUiVersion(
  actual: string = naiveUiVersion,
  report: (message: string) => void = (m) => console.error(m),
): boolean {
  if (satisfiesMin(actual, MIN_NAIVE_UI)) return true;
  report(
    `[@tnzi/ui-admin] naive-ui ${actual} is older than the required ${MIN_NAIVE_UI}. ` +
      'Form validation by field path (used by the login send-code buttons and by ' +
      'useNaiveForm().validateFields) will throw "filter is not a function" at the ' +
      'moment it runs. Note that pnpm does not verify peerDependencies for packages ' +
      'linked from a local checkout, which is why the install stayed silent.',
  );
  return false;
}

/** True in a dev build. Kept isolated because this package's tsconfig does not
 *  pull in `vite/client`, so `import.meta.env` needs a cast to be read at all. */
function isDev(): boolean {
  const env = (import.meta as unknown as { env?: Record<string, unknown> }).env;
  // Absent env (a plain node/test context) is treated as dev: a missing build
  // flag should not silently disable a diagnostic.
  return env === undefined || env.DEV === true || env.MODE === 'development';
}

/** Called once from `install()`. No-op outside dev builds. */
export function runPeerChecks(): void {
  if (isDev()) checkNaiveUiVersion();
}
