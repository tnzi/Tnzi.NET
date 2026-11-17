/**
 * Size budgets for `@tnzi/ui`.
 *
 * No `ignore` list here, unlike the sibling packages: this one has no eagerly
 * bundled heavyweight. `vue`, `pinia`, `@vueuse/core`, `@iconify/*` and
 * `@tnzi/core` are externals in the vite build, and the package renders with
 * no component library at all.
 *
 * Measured 2026-11-09, once the naive-rendered components, the naive theme
 * bridge and adapters, `useEcharts` / `useReorderable` and the cropper had
 * moved to `@tnzi/ui-admin` and the stores had left the root entry:
 *
 *   index         31.56 kB  (85.15 before the move)
 *   components    15.42 kB  (63.11; 27 components left of 63)
 *   stores        11.18 kB
 *   headless      15.02 kB  (18.48; useOverlayTheme, useSafeMessage, the
 *                            form host, address fields, widgets, echarts and
 *                            reordering left)
 *   utils           485 B   (naive-helpers left)
 *   resolvers       287 B   (the names of 27 components instead of 63)
 *   stores/app     4.34 kB
 *   stores/auth    6.76 kB
 *   stores/user    4.07 kB
 *   style.css      6.62 kB  (10.34; the scoped styles of the moved
 *                            components, media-chip.css and form-schema.css
 *                            left with them)
 *
 * Limits sit about 5-8% above the measurement, so a real regression trips
 * them and normal churn does not. Every limit is a ratchet: tighten it as
 * weight comes off, and never raise one to turn a red run green without saying
 * why in the same commit, with a before/after measurement of the change.
 *
 * `resolvers` moves whenever a component is added to or removed from the
 * barrel: it carries the list of component names the resolver answers for.
 * `style.css` is loaded in full by every consumer (no tree-shaking): every new
 * component's scoped styles land in it.
 */

const budget = (name, path, limit) => ({ name, path, limit, gzip: true })

export default [
  budget('index (whole package barrel)', 'dist/index.js', '33.5 kB'),
  budget('components barrel (27 library-neutral SFCs)', 'dist/components.js', '16.5 kB'),
  budget('stores barrel', 'dist/stores.js', '12 kB'),
  budget('headless barrel (incl. the login stack and the theme context)', 'dist/headless.js', '16 kB'),
  budget('utils barrel', 'dist/utils.js', '600 B'),
  budget('resolvers barrel', 'dist/resolvers.js', '320 B'),

  budget('stores/app', 'dist/stores/app/index.js', '4.7 kB'),
  budget('stores/auth', 'dist/stores/auth/index.js', '7.2 kB'),
  budget('stores/user', 'dist/stores/user/index.js', '4.4 kB'),

  budget('style.css (shipped whole, no tree-shaking)', 'dist/style.css', '7 kB'),
]
