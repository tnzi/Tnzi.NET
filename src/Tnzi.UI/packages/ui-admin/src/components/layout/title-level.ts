import type { InjectionKey } from 'vue'

/**
 * Which tier a content-page title renders at.
 *
 *  - `'page'`    - the identity of the surface you navigated to.
 *  - `'section'` - one region INSIDE such a surface.
 *
 * The two sizes live in `styles/variables.css`
 * (`--tnzi-admin-title-{page,section}-{size,weight,tracking}`), so the tier is
 * stated once and both steps down at the same breakpoint.
 */
export type TTitleLevel = 'page' | 'section'

/**
 * The tier the surrounding container renders its titles at.
 *
 * `TDetailLayout` provides `'section'` for its panel/body, because everything
 * dropped in there is one region of the page whose identity the layout's own
 * header already carries. `TPageHeader` injects it, so a `TListShell` (or a
 * `TContentPage`, or a bare `TPageHeader`) used as a detail section renders its
 * title at the SAME tier as a `TDetailSection` beside it in the same menu -
 * with no prop at the call site.
 *
 * Why this and not "just make the two components agree": they legitimately are
 * two tiers. Every `side`/`tabs` detail page renders BOTH at once - the page
 * header naming the record above a panel naming the part of it you are looking
 * at. Flattening them would delete that. What was missing is not a shared size,
 * it is a way for a page-tier component to be told it is standing in a section
 * slot; the only escape that existed was `TListShell :show-header="false"`,
 * which deletes the title outright and is wrong when the panel's title is
 * per-section.
 *
 * An explicit `level` prop always wins - which is how `TDetailLayout` keeps its
 * OWN header at `'page'` despite providing `'section'` to everything below it.
 *
 * A consumer with a hand-rolled panel gets the same behaviour with one line:
 * `provide(TITLE_LEVEL, 'section')`.
 */
export const TITLE_LEVEL: InjectionKey<TTitleLevel> = Symbol('tnzi-title-level')
