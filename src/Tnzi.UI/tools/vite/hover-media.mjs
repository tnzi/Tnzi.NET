/**
 * PostCSS plugin: every `:hover` rule applies only where hovering exists.
 *
 * A touch screen has no hover, but it reports one: after a tap the element
 * stays `:hover` until the finger touches something else, so a row tapped on a
 * phone keeps its hover fill and a button keeps its hover colour. Wrapping the
 * hover selectors in `@media (hover: hover)` leaves them to devices with a
 * pointer that hovers. Done at build time, so a stylesheet is written with
 * plain `:hover` and cannot forget the media query.
 *
 * - A selector list is split: the selectors with `:hover` move into the media
 *   query (placed right after the rule, so the cascade order is unchanged), the
 *   others (`:focus-visible`, `.is-menu-open`, ...) stay where they are.
 * - Rules already inside a media query that names `hover` (`(hover: hover)`,
 *   `(hover: none)`, `(any-hover: ...)`) are left alone, as are keyframes.
 * - What a hover reveals on a pointer device needs its own `@media (hover: none)`
 *   rule to be reachable on a touch screen; this plugin does not invent one.
 *
 * @returns {import('postcss').Plugin}
 */
export function hoverMediaPlugin() {
  return {
    postcssPlugin: 'tnzi-hover-media',
    Rule(rule, { AtRule }) {
      if (!rule.selector.includes(':hover')) return
      for (let parent = rule.parent; parent; parent = parent.parent) {
        if (parent.type !== 'atrule') continue
        if (/keyframes$/i.test(parent.name)) return
        if (parent.name === 'media' && /\bhover\b/.test(parent.params)) return
      }
      const hovering = rule.selectors.filter((selector) => selector.includes(':hover'))
      const others = rule.selectors.filter((selector) => !selector.includes(':hover'))
      const media = new AtRule({ name: 'media', params: '(hover: hover)' })
      media.append(rule.clone({ selectors: hovering }))
      rule.after(media)
      if (others.length > 0) rule.selectors = others
      else rule.remove()
    },
  }
}
hoverMediaPlugin.postcss = true
