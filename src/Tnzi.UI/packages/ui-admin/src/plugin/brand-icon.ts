/**
 * `registerBrandIcon` - register a custom monochrome brand glyph so it can be
 * referenced by `login.brandIcon` and therefore render identically across the
 * admin sidebar header, the login page brand panel, and anywhere `TSvgIcon`
 * resolves the name.
 *
 * The glyph SHOULD fill with `currentColor` so it adapts to each surface:
 * primary-tinted on the white sidebar, white on the login panel's saturated
 * gradient. (A single fixed-color logo can't contrast with both a white
 * sidebar and a deep gradient - hence currentColor + a separate colored
 * `favicon.svg` for the browser tab.)
 *
 * `@tnzi/ui-admin` resolves the *same* `@iconify/vue` instance as `@tnzi/ui`'s
 * `TSvgIcon`, so an icon registered here is visible to every `<Icon>` the UI
 * renders - consumers don't need `@iconify/vue` as a direct dependency.
 *
 * ```ts
 * registerBrandIcon('acme:logo', { body: '<path d="…" fill="currentColor"/>' })
 * defineAdminApp({
 *   login: { brandIcon: 'acme:logo', brand: 'Acme', brandSubtitle: 'Admin Console' },
 * })
 * ```
 */
import { addIcon } from '@iconify/vue'

export interface BrandIconData {
  /** Inner SVG markup (paths / rects). Use `fill="currentColor"` to tint per surface. */
  body: string
  /** viewBox width. Defaults to 24. */
  width?: number
  /** viewBox height. Defaults to 24. */
  height?: number
}

export function registerBrandIcon(name: string, icon: BrandIconData): void {
  addIcon(name, {
    width: icon.width ?? 24,
    height: icon.height ?? 24,
    body: icon.body,
  })
}
