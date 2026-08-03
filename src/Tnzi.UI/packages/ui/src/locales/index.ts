/**
 * @tnzi/ui/locales
 *
 * Naive UI locale bridging only.
 *
 * This directory used to also ship an `en` / `zhCN` message bundle under a
 * `tnzi.*` namespace (auth form labels, layout chrome, feedback copy). Nothing
 * read it: no component in this package imported it, no consumer imported it,
 * and the components it claimed to translate (the `TLoginForm` generation,
 * itself removed on 2026-08-02) took their strings from `*Label` props. Its
 * `Locale` interface also restated all 80-odd keys by hand. It was deleted
 * rather than wired up, because `@tnzi/core/adapters/i18n` already owns the
 * framework message catalogue - a second one here would be the same
 * duplication in the other direction.
 */
export { getNaiveLocale, type NaiveLocaleBundle } from './naive'
