/**
 * Admin i18n layer.
 *
 * This is the package's lowest-level primitive - `translatePageKey` is reached
 * from ~45 components, widgets and layout pieces - so it lives here rather than
 * under `pages/`, where it used to sit. A component reaching up into `pages/`
 * for its translator meant the `./components` subpath export dragged the entire
 * page layer (and, through it, both locale dictionaries) into any consumer that
 * imported one component.
 *
 * `pages/_shared/translate` remains as a re-export so existing page imports keep
 * resolving.
 */
export {
  humanise,
  translatePageKey,
  interpolate,
  makePageTranslator,
  maybeTranslate,
  maybeTranslateKey,
  resolveBackendLabel,
} from './translate'

export {
  getLocaleMessages,
  setLocaleMessages,
  loadLocaleMessages,
  type AdminLocale,
} from './messages'
