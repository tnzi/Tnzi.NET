/**
 * Back-compat re-export. The implementation moved to `src/i18n/` in 0.2.72+.
 *
 * It had to move: this module is the package's lowest-level primitive
 * (~45 components, widgets and layout pieces resolve labels through it), and
 * having it live under `pages/` inverted the layering - `./components` could
 * not be imported without dragging in the whole page layer, and with it both
 * bundled locale dictionaries.
 *
 * Page-level imports keep working through this file; new code should import
 * from the package root (`@tnzi/ui-admin`) instead.
 */
export {
  humanise,
  translatePageKey,
  interpolate,
  makePageTranslator,
  maybeTranslate,
  maybeTranslateKey,
  resolveBackendLabel,
} from '../../i18n/translate'
