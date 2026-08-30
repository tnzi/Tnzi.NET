/**
 * The little round controls that sit on a picture - one family, three uses.
 *
 * `TAttachmentWall` draws a remove chip and a drag grip on each tile;
 * `TImageUpload` draws a remove chip on its frame. The two remove chips had
 * drifted into two different controls (a black, always-visible chip on the
 * gallery field; a white, hover-only chip on the image field, agreeing on
 * nothing but the 18px circle), and an editor filling one form met both.
 *
 * The convergence is `styles/media-chip.css`. These tests hold it: not "the
 * chips look the same today", which nothing can assert without a browser, but
 * "there is exactly one place that decides how they look, and no component has
 * started deciding again". Copying the material back into a scoped block is how
 * the first divergence happened, so that is the thing to fail on.
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const SRC = resolve(__dirname, '../../src')

const chipCss = readFileSync(resolve(SRC, 'styles/media-chip.css'), 'utf8')
const stylesIndex = readFileSync(resolve(SRC, 'styles/index.css'), 'utf8')
const wall = readFileSync(resolve(SRC, 'components/display/TAttachmentWall.vue'), 'utf8')
const upload = readFileSync(resolve(SRC, 'components/form/TImageUpload.vue'), 'utf8')

/** The `<style scoped>` block of an SFC - where a re-declaration would appear. */
function scopedStyle(sfc: string): string {
  const m = sfc.match(/<style scoped>([\s\S]*?)<\/style>/)
  expect(m, 'SFC has a scoped style block').toBeTruthy()
  return m![1]
}

/**
 * The body of an `@media` block, and the stylesheet with that block cut out.
 *
 * Brace-matched rather than lazily matched: a media block contains rules, so
 * the first `}` after it closes the first RULE, not the block. And anchored on
 * the opening brace rather than on the text, because this file names its own
 * media query in the header comment - both shortcuts return a fragment that
 * every assertion then passes against for the wrong reason.
 */
function mediaBlock(css: string, condition: string): { body: string; rest: string } {
  const escaped = condition.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const opener = new RegExp(`@media\\s*${escaped}\\s*\\{`).exec(css)
  if (!opener) return { body: '', rest: css }

  const open = opener.index + opener[0].length - 1
  let depth = 0
  let i = open
  for (; i < css.length; i++) {
    if (css[i] === '{') depth++
    else if (css[i] === '}' && --depth === 0) break
  }
  return {
    body: css.slice(open + 1, i),
    rest: css.slice(0, opener.index) + css.slice(i + 1),
  }
}

/** The rule body for one selector, from a flat (un-nested) stylesheet. */
function ruleBody(css: string, selector: string): string {
  const re = new RegExp(`(^|[},/*\\n])\\s*${selector.replace(/[.:()]/g, '\\$&')}\\s*\\{([^}]*)\\}`, 'm')
  const m = css.match(re)
  return m ? m[2] : ''
}

/** Every control in the family: the component that draws it and its own class. */
const CHIPS: Array<{ name: string; sfc: string; positionClass: string; modifier: string }> = [
  { name: 'TAttachmentWall remove', sfc: wall, positionClass: 't-attachment-wall__remove', modifier: 't-media-remove' },
  { name: 'TAttachmentWall grip', sfc: wall, positionClass: 't-attachment-wall__grip', modifier: 't-media-grip' },
  { name: 'TImageUpload remove', sfc: upload, positionClass: 't-image-upload__remove', modifier: 't-media-remove' },
]

describe('shared media chip', () => {
  it('ships its material with the package (the stylesheet is imported, not orphaned)', () => {
    // Without this line the whole file is dead weight and every chip renders as
    // an unstyled browser control - a failure mode no component test would see,
    // because Vue Test Utils never applies CSS.
    expect(stylesIndex).toContain("@import './media-chip.css'")
  })

  it('every chip carries the shared material class, on a component that hosts the hover', () => {
    for (const { name, sfc, positionClass, modifier } of CHIPS) {
      expect(sfc, `${name} carries the shared material class`).toContain(
        `${positionClass} t-media-chip ${modifier}`,
      )
      expect(sfc, `${name} lives on a hover host`).toContain('t-media-frame')
    }
  })

  it('no component re-declares the shared material in its own scoped block', () => {
    // Position is theirs - the wall's tile clips its overflow so its chips have
    // to sit inside it, the upload's does not. Everything else is not.
    const MATERIAL = ['background', 'color', 'opacity', 'box-shadow', 'transition', 'border-radius']
    for (const { name, sfc, positionClass } of CHIPS) {
      const body = ruleBody(scopedStyle(sfc), `.${positionClass}`)
      expect(body, `${name} still positions itself`).toMatch(/position:\s*absolute/)
      for (const prop of MATERIAL) {
        expect(body, `${name} must not re-declare ${prop}`).not.toContain(`${prop}:`)
      }
    }
  })

  it('hides the chips only where a pointer can hover, and reveals them for the keyboard', () => {
    // The trap this guards: hidden-until-hover on a touch screen is a control
    // that is permanently invisible AND still tappable, which is worse than one
    // that never hides. So the `opacity: 0` must live inside the media query.
    const { body, rest } = mediaBlock(chipCss, '(hover: hover)')
    expect(body, 'the reveal rule is gated on hover capability').not.toBe('')
    expect(body).toMatch(/opacity:\s*0/)
    expect(body).toContain(':focus-visible')
    // Gated on the family class, so a chip added later is covered by construction.
    expect(body).toContain('.t-media-frame .t-media-chip')

    // ...and nothing outside it may hide a chip, or the gate is decorative.
    expect(rest).not.toMatch(/opacity:\s*0/)
  })

  it('paints a dark scrim with a white glyph, so it reads over any photograph', () => {
    // The chip sits on a picture whose colours nobody controls. A chip in the
    // container colour is white in the light theme and vanishes into a light
    // photo - which is what `TImageUpload` used to do.
    const base = ruleBody(chipCss, '.t-media-chip')
    expect(base).toMatch(/background:\s*rgb\(0 0 0 \/ \d+%\)/)
    expect(base).toMatch(/color:\s*#fff/)
    expect(base).not.toContain('--tnzi-container-bg')
  })

  it('gives every remove chip the same destructive hover feedback', () => {
    expect(ruleBody(chipCss, '.t-media-remove:hover')).toContain('--tnzi-error')
  })

  it('says with the cursor that the grip is a gesture surface, not a button', () => {
    expect(ruleBody(chipCss, '.t-media-grip')).toMatch(/cursor:\s*grab/)
    // ...and that the gesture is under way for as long as Sortable says it is,
    // not only while the pointer happens to be over the grip.
    expect(ruleBody(chipCss, '.t-attachment-wall__tile--chosen .t-media-grip')).toMatch(/cursor:\s*grabbing/)
  })
})

describe('TImageUpload root box', () => {
  it('shrink-to-fits even when blockified by a flex parent', () => {
    // naive-ui's `.n-form-item-blank` is a flex container and a flex item
    // blockifies `inline-block`, so the root stretched across the whole form
    // column while the preview frame kept its own width - putting the chip,
    // which anchors to the root, hundreds of pixels from its picture.
    const root = ruleBody(scopedStyle(upload), '.t-image-upload')
    expect(root).toMatch(/width:\s*fit-content/)
  })
})
