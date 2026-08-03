/**
 * Size budgets for `@tnzi/ui`.
 *
 * No `ignore` list here, unlike the sibling packages: this one has no eagerly
 * bundled heavyweight. `vue`, `naive-ui` and `echarts` are all externals in the
 * vite build, which is why a barrel holding 60+ components measures 55 kB
 * rather than megabytes.
 *
 * ## Why the limits moved
 *
 * They had drifted into meaninglessness in both directions (measured
 * 2026-07-31):
 *
 *   index         74.5 kB against a 320 kB limit   - 4.3x headroom
 *   stores/auth    8.3 kB against a  35 kB limit   - 4x
 *   stores/user    7.7 kB against a  30 kB limit   - 4x
 *   stores/app     8.06 kB against an 8 kB limit   - over by 57 B
 *
 * Only the last one was ever going to say anything, and what it said was "57 B",
 * which is noise. The rest could have quadrupled in silence. Limits now sit just
 * above measured weight so a real regression trips them, and the four public
 * subpaths that had no budget at all (components / stores / headless / utils /
 * resolvers) have one.
 *
 * ## Watch this one
 *
 * `style.css` is at 9.7 kB against 11 kB. It is the only budget here without
 * comfortable headroom, and it is the one most likely to creep - every new
 * component's scoped styles land in it.
 *
 * Every limit is a ratchet: tighten as weight comes off, and never raise one to
 * turn a red run green without saying why in the same commit.
 */

const budget = (name, path, limit) => ({ name, path, limit, gzip: true })

// Ratcheted 2026-08-01 after deleting `components/control/` (4 components),
// `components/icon/TIcon`, and the dead `locales/` message bundle. Measured
// after that removal:
//
//   index         71.78 kB  (was 74.5)
//   components    54.05 kB
//   stores        12.72 kB
//   headless      11.03 kB
//   utils          1.00 kB
//   resolvers        112 B
//   stores/app     8.19 kB
//   stores/auth    8.25 kB
//   stores/user    7.82 kB
//   style.css      9.36 kB  (was 9.7)
//
// Limits sit ~8% above measured so a real regression trips them and normal
// churn does not.
//
// `headless` re-measured 2026-08-02 at 13.45 kB and raised 12 -> 15 kB. The
// growth is the login stack (8 modules) that moved down from `@tnzi/ui-admin`
// earlier the same day; the budget was simply not re-measured in that commit,
// so `pnpm size` has been red ever since without anyone reading it. The rename
// of this directory (`composables` -> `headless`, one name per concept across
// all five packages) moved no code and changed no byte.
export default [
  budget('index (whole package barrel)', 'dist/index.js', '78 kB'),
  budget('components barrel (61 SFCs; naive-ui external)', 'dist/components.js', '59 kB'),
  budget('stores barrel', 'dist/stores.js', '14 kB'),
  budget('headless barrel (incl. the login stack)', 'dist/headless.js', '15 kB'),
  budget('utils barrel', 'dist/utils.js', '1.5 kB'),
  budget('resolvers barrel', 'dist/resolvers.js', '512 B'),

  budget('stores/app', 'dist/stores/app/index.js', '9 kB'),
  budget('stores/auth', 'dist/stores/auth/index.js', '9 kB'),
  budget('stores/user', 'dist/stores/user/index.js', '8.5 kB'),

  // Every consumer loads this in full - it is not tree-shaken. See the note above.
  budget('style.css (shipped whole, no tree-shaking)', 'dist/style.css', '10 kB'),
]
