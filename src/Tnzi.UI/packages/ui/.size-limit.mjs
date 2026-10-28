/**
 * Size budgets for `@tnzi/ui`.
 *
 * No `ignore` list here, unlike the sibling packages: this one has no eagerly
 * bundled heavyweight. `vue`, `naive-ui` and `echarts` are all externals in the
 * vite build, which is why a barrel holding 60+ components measures about
 * 60 kB rather than megabytes.
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
 * `style.css` is at 10.34 kB against 11 kB (2026-10-12). It is the budget most
 * likely to creep - every new component's scoped styles land in it, and it
 * has drifted past its re-measures twice (see its note below).
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
//
// `headless` re-measured 2026-09-12 at 15.54 kB and raised 15 -> 17 kB. The
// growth is the form host (`headless/form/form-host.ts`: provideFormHost /
// useFormHost / useFormHostRegistration) that lets the container owning the
// Save button validate slotted TSchemaForms; it is shipped weight, not churn.
// 17 kB keeps the usual ~8% headroom above the measurement.
//
// `index` re-measured 2026-09-21 at 78.27 kB against 77.8 kB at HEAD (both
// built the same way) and raised 78 -> 80 kB. The 0.47 kB is the passkey
// second-factor leg of the default login callbacks (`verifyTwoFactorWithPasskey`
// in `headless/auth/default-auth.ts`) and the ceremony it pulls from core.
// 0.2 kB of headroom was a tripwire, not a budget: it would have fired for
// whichever change landed next rather than for the one that filled it.
//
// `components` re-measured 2026-10-11 at 59.01 kB against 58.92 kB before the
// change and raised 59 -> 60 kB. The 0.09 kB is the core captcha client that
// TCaptcha pulls in: the ALTCHA challenge is now read raw and parsed, because the
// endpoint answers the bare challenge document rather than an envelope.
//
// `index` and `components` re-measured 2026-10-12 at 80.45 / 59.45 kB against
// 80.01 / 59.01 kB at the parent commit (both built the same way). `index` was
// raised 80 -> 82 kB: the parent was already 10 B over it, because earlier
// fixes in the same batch did not re-measure. The 0.44 kB is shipped
// behaviour: TImageUpload keeping the source format, naming the cropped file
// and reporting a failed crop; TStatusBadge's empty placeholder; TTable's
// cleared sort and `emptyText`; TButtonIcon forwarding attributes to the
// button; TAttachmentWall's allow-list link check.
//
// `components` went 60 -> 61 kB in the same step with no overage behind it:
// its own 10 B had already been paid for by the 59 -> 60 kB raise above, and
// 59.45 kB fitted under 60 kB with 0.55 kB to spare. It stays at 61 kB as
// headroom: the round's final tree measures 59.78 kB (below), which would
// leave 0.22 kB under 60 kB, a tripwire by the rule in the `index` note.
//
// `resolvers` re-measured 2026-10-12 at 510 B (was 112 B) and raised
// 512 -> 560 B. The growth is the list of the 63 component names the resolver
// now answers for; it used to claim every `T*` name, including other packages'
// components. The list grows with the components barrel, so expect this one to
// move whenever a component is added.
//
// Re-measured 2026-10-12 on the final tree of the audit round (`pnpm build`,
// then `pnpm size`), against the round's start (ee8187699, 2026-10-03) built
// the same way:
//
//   index        78.72 -> 81.14 kB    stores/app    4.33 ->  4.34 kB
//   components   58.80 -> 59.78 kB    stores/auth   5.76 ->  6.67 kB
//   stores       10.25 -> 11.09 kB    stores/user   4.01 ->  4.08 kB
//   headless     16.52 -> 16.99 kB    style.css    10.31 -> 10.34 kB
//   utils         1.00 ->  1.26 kB    resolvers     0.11 ->  0.51 kB
//
// The stores move with the core auth state they wrap (never adopting another
// user's session from a sibling tab, a default permission source, a clean
// start per session, refresh without waiting for permissions). `utils` is
// `naive-helpers` treating whitespace-only input as missing for a required
// `IFormRule`.
//
// `headless` raised 17 -> 18 kB: 16.99 kB is 9 B under the line. The last
// entry above (15.54 kB, 2026-09-12) was never followed by another; the
// barrel already measured 16.52 kB when the round began, the difference being
// login-stack work that landed in between without a re-measure (the captcha
// provider layer, the passkey second-factor leg, the challenge handling of
// the default callbacks, code inputs following the configured length). This
// round's 0.47 kB, read off a per-module breakdown of the two builds: the
// `@tnzi/core` utils and identity client code the login stack reaches, sign-up
// and recovery reading their own channels, the default callbacks' register
// retry, the theme context following the OS colour scheme in `auto` mode, the
// password rule's default range and whitespace-only check, and `useEcharts`'
// SVG renderer; less the two `ThemeSettings` fields that were removed.
//
// `index` and `components` re-measured 2026-10-17 at 83.96 / 62.58 kB against
// 81.14 / 59.78 kB at the parent commit (both built the same way) and raised
// 82 -> 86 kB / 61 -> 65 kB. The whole 2.8 kB is the cropperjs 2.1 -> 2.3
// upgrade, read off an esbuild metafile of both builds: the `@cropper/*`
// elements TImageUpload uses grew by 8.5 kB minified (element-selection +3.8,
// element-image +2.7, utils +1.2, the rest small) while `@tnzi/ui`'s own code
// shrank by 0.1 kB. Every other dependency bump in the same upgrade measured
// within 0.2 kB when reverted on its own. It is shipped behaviour: the initial
// crop box now covers 90% of the image instead of 90% of the canvas (what
// TImageUpload's `initialCoverage` comment always meant), and two load bugs are
// fixed (an image already loaded never firing `load`, and an image whose `src`
// is set after it connects staying at 0x0).
//
// `headless` re-measured 2026-10-20 at 18.48 kB against 17.91 kB at the parent
// commit (both built the same way) and raised 18 -> 19.5 kB; `index` went
// 84.69 -> 85.15 kB and `components` 63.05 -> 63.11 kB, both inside their
// budgets. The 0.57 kB is the theme context's new reach: `buildCssVars` now
// derives the primary's readable companions (`--tnzi-on-primary`, and the
// primary as text on light and dark surfaces) and `buildNaiveThemeOverrides`
// the foreground naive paints on a light primary's fills, through the contrast
// helpers in `theme/palette.ts` (`contrastRatio`, `getContrastTextColor`,
// `getReadableColor`). They move lightness in colord's built-in HSL rather
// than through a colour-space plugin, which measured 1.32 kB here instead of
// 0.57 kB; the ratio check on every candidate makes HSL as safe. Shipped behaviour: a
// brand colour picked at runtime gets a readable foreground on its fills and a
// readable text variant, whatever colour it is. The parent was 89 B under the
// old line, so 19.5 kB restores the usual ~5% headroom instead of a tripwire.
export default [
  budget('index (whole package barrel)', 'dist/index.js', '86 kB'),
  budget('components barrel (61 SFCs; naive-ui external)', 'dist/components.js', '65 kB'),
  budget('stores barrel', 'dist/stores.js', '14 kB'),
  budget('headless barrel (incl. the login stack and the theme context)', 'dist/headless.js', '19.5 kB'),
  budget('utils barrel', 'dist/utils.js', '1.5 kB'),
  budget('resolvers barrel', 'dist/resolvers.js', '560 B'),

  budget('stores/app', 'dist/stores/app/index.js', '9 kB'),
  budget('stores/auth', 'dist/stores/auth/index.js', '9 kB'),
  budget('stores/user', 'dist/stores/user/index.js', '8.5 kB'),

  // Every consumer loads this in full - it is not tree-shaken. See the note above.
  //
  // Re-measured 2026-09-02 at 10.02 kB and raised 10 -> 10.5 kB. Two parts, and
  // only the small one belongs to that commit:
  //   - 34 B: TModalShell's height-settling rules (the transition, its
  //     reduced-motion counterpart, and the measured wrapper's `flow-root`).
  //     Measured by stripping exactly those three rules out of the built
  //     stylesheet and re-gzipping.
  //   - the rest: this budget was ALREADY over before that change - stripping
  //     those 34 B still leaves 10.05 kB. It has been drifting up from the 9.36
  //     kB measured on 2026-08-01 through late-August work (surface tiers,
  //     media chips), none of which re-measured it. Same failure the `headless`
  //     note above records: a budget nobody reads is a budget that is already
  //     red. Worth a pass of its own to see what can come out.
  //
  // 10.5 -> 11 kB on 2026-10-12, measured 10.34 kB on the audit round's final
  // tree. It already read 10.31 kB when the round began (2026-10-03), so the
  // 0.29 kB since the 10.02 kB above came in without a re-measure, as before;
  // this round's component fixes add 0.03 kB. 0.16 kB of headroom is a
  // tripwire, not a budget.
  budget('style.css (shipped whole, no tree-shaking)', 'dist/style.css', '11 kB'),
]
