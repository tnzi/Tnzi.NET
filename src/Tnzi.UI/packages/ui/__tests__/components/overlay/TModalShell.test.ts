import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, it, expect } from 'vitest'
import { nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import TModalShell from '../../../src/components/overlay/TModalShell.vue'

// Capture the props naive's NModal receives so we can assert the chrome
// defaults (size, mask-closable) the shell forwards.
const modalStub = {
  name: 'Modal',
  props: ['show', 'size', 'maskClosable', 'preset', 'title', 'style'],
  template: '<div class="n-modal-stub" :data-size="size" :data-preset="preset" v-if="show"><slot name="header" /><slot /><slot name="footer" /></div>',
}

const stubs = { Modal: modalStub }

/**
 * The component's own source. Some of what this shell guarantees is layout the
 * environment here does not compute (happy-dom does no layout, and CSS in a
 * `<style>` block never reaches it at all), so a few invariants are pinned
 * against the source itself.
 */
function readSource(): string {
  return readFileSync(
    join(__dirname, '..', '..', '..', 'src', 'components', 'overlay', 'TModalShell.vue'),
    'utf8',
  )
}

describe('TModalShell', () => {
  it('renders a card-preset NModal when shown', () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    const modal = w.find('.n-modal-stub')
    expect(modal.exists()).toBe(true)
    expect(modal.attributes('data-preset')).toBe('card')
  })

  it('defaults to the compact `small` card size (tighter padding)', () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    expect(w.find('.n-modal-stub').attributes('data-size')).toBe('small')
  })

  it('honours an explicit size override', () => {
    const w = mount(TModalShell, { props: { show: true, size: 'medium' }, global: { stubs } })
    expect(w.find('.n-modal-stub').attributes('data-size')).toBe('medium')
  })

  it('wraps the body in the scroll region', () => {
    const w = mount(TModalShell, {
      props: { show: true },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    expect(w.find('.t-modal-shell__scroll .body').exists()).toBe(true)
  })

  it('renders the footer region only when a footer slot is supplied', () => {
    const without = mount(TModalShell, { props: { show: true }, global: { stubs } })
    expect(without.find('.foot').exists()).toBe(false)
    const withFooter = mount(TModalShell, {
      props: { show: true },
      slots: { footer: '<div class="foot" />' },
      global: { stubs },
    })
    expect(withFooter.find('.foot').exists()).toBe(true)
  })

  it('emits update:show when the modal requests close', async () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    w.findComponent(modalStub).vm.$emit('update:show', false)
    await w.vm.$nextTick()
    expect(w.emitted('update:show')?.[0]).toEqual([false])
  })

  it('renders a #header slot for rich titles (entity name + tag)', () => {
    const w = mount(TModalShell, {
      props: { show: true },
      slots: { header: '<div class="rich-head">Invoice INV-001 <span class="tag">Posted</span></div>' },
      global: { stubs },
    })
    expect(w.find('.rich-head').exists()).toBe(true)
    expect(w.find('.rich-head .tag').text()).toBe('Posted')
  })

  it('forwards the plain title to NModal when no header slot is given', () => {
    const w = mount(TModalShell, { props: { show: true, title: 'Invoice' }, global: { stubs } })
    expect(w.findComponent(modalStub).props('title')).toBe('Invoice')
  })

  // naive's Card resolves its header as `title ? [title] : slots.header`, so a
  // forwarded title would win and the slot would vanish with no warning. The
  // shell withholds the prop to keep the documented precedence (slot wins).
  // Asserted on the forwarded prop, not on the DOM: the stub above can't
  // reproduce naive's own resolution.
  it('withholds the title from NModal when a header slot is given, so the slot wins', () => {
    const w = mount(TModalShell, {
      props: { show: true, title: 'Invoice' },
      slots: { header: '<div class="rich-head">Invoice INV-001</div>' },
      global: { stubs },
    })
    expect(w.findComponent(modalStub).props('title')).toBeUndefined()
    expect(w.find('.rich-head').exists()).toBe(true)
  })

  it('overlays the body with a spinner while loading', () => {
    const w = mount(TModalShell, {
      props: { show: true, loading: true },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    // Body stays mounted under the overlay; the scroll region gets the
    // min-height guard so an empty body doesn't collapse.
    expect(w.find('.t-modal-shell__scroll--loading').exists()).toBe(true)
    expect(w.find('.n-spin-body').exists()).toBe(true)
    expect(w.find('.t-modal-shell__scroll .body').exists()).toBe(true)
  })

  it('shows no spinner or min-height guard when not loading', () => {
    const w = mount(TModalShell, {
      props: { show: true },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    expect(w.find('.t-modal-shell__scroll--loading').exists()).toBe(false)
    expect(w.find('.n-spin-body').exists()).toBe(false)
    expect(w.find('.t-modal-shell__scroll .body').exists()).toBe(true)
  })
  // -- Height settling -------------------------------------------------------
  // A modal usually opens before its data arrives: the body renders empty, then
  // grows by hundreds of pixels. Centred, that growth moves the header up by
  // half the delta (measured: 254px on a 10-field form). These three guards are
  // what let a consumer drop a form in without hand-sizing the dialog.

  it('anchors the card near the top so growth only extends downward', () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    const style = w.findComponent(modalStub).props('style') as Record<string, string>
    expect(style.marginTop).toBe('min(10vh, 88px)')
    // `auto` is what actually pins the top edge - a fixed value leaves the card
    // centred within whatever space is left over.
    expect(style.marginBottom).toBe('auto')
  })

  it('leaves naive centring alone when align="center"', () => {
    const w = mount(TModalShell, { props: { show: true, align: 'center' }, global: { stubs } })
    const style = w.findComponent(modalStub).props('style') as Record<string, string>
    expect(style.marginTop).toBeUndefined()
    expect(style.marginBottom).toBeUndefined()
  })

  // -- Bounding the card to the viewport -------------------------------------
  // `contentMaxHeightVh` caps the inner scroll area only; the top offset, the
  // header and the footer stack on top of that cap, so a value much above ~78
  // ran the card - footer included - off the bottom of the screen. Measured in
  // Chromium before the bound, `contentMaxHeightVh: 88` on an 820px viewport:
  // card bottom 911, i.e. 91px of it below the fold, with the Save / Cancel row
  // in that 91px. After it, the card ends 16px clear of the bottom at every
  // viewport height from 600 to 1000 and the body scrolls that much sooner.
  // The caller cannot compute this bound: all three terms belong to the shell.

  it('bounds the card to the viewport, below the top anchor', () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    const style = w.findComponent(modalStub).props('style') as Record<string, string>
    // Same anchor expression as `marginTop`, so the two cannot drift apart.
    expect(style.maxHeight).toBe('calc(100dvh - min(10vh, 88px) - 16px)')
  })

  it('bounds the card with a gutter at each end when align="center"', () => {
    const w = mount(TModalShell, { props: { show: true, align: 'center' }, global: { stubs } })
    const style = w.findComponent(modalStub).props('style') as Record<string, string>
    expect(style.maxHeight).toBe('calc(100dvh - 16px * 2)')
  })

  it('bounds the card whatever contentMaxHeightVh the caller asks for', () => {
    const w = mount(TModalShell, { props: { show: true, contentMaxHeightVh: 95 }, global: { stubs } })
    const style = w.findComponent(modalStub).props('style') as Record<string, string>
    expect(style.maxHeight).toBe('calc(100dvh - min(10vh, 88px) - 16px)')
    // The preference still reaches the body - it just stops being the only cap.
    expect(w.find('.t-modal-shell__scroll').attributes('style')).toContain('max-height: 95vh')
  })

  // -- Placement once the card is at that bound ------------------------------
  // The bound made the anchor lopsided: a card at it sits under the full
  // min(10vh, 88px) with only the 16px gutter beneath, which reads as a dialog
  // that slipped down rather than one that was placed, and the taller the
  // dialog the worse it looks. The stylesheet turns the anchor into a MAXIMUM
  // with a pair of flex spacers around the card - the top one capped, both
  // claiming an equal share of the leftover - so the offset is
  // `min(anchor, slack / 2)`. Measured in Chromium at 1000px: a card at the
  // bound went from 88 above / 16 below to 52 / 52, while a 763.6px one (not at
  // the bound) stayed at 88 above to the pixel. That machinery is CSS and
  // invisible from here; what the component owns is the marker class it hangs
  // off, and the anchor value the two sides have to agree on.

  it('marks the card as top-anchored so the placement rules can reach it', () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    expect(w.find('.n-modal-stub').classes()).toContain('t-modal-shell--top')
  })

  it('drops the top-anchor marker for align="center"', () => {
    const w = mount(TModalShell, { props: { show: true, align: 'center' }, global: { stubs } })
    expect(w.find('.n-modal-stub').classes()).not.toContain('t-modal-shell--top')
  })

  // Fullscreen takes the card out of that container's flow entirely
  // (`position: fixed`), and there is no anchor to cap when the card IS the
  // viewport - so the marker must not survive `align="top"` plus fullscreen.
  it('drops the top-anchor marker in fullscreen', () => {
    const w = mount(TModalShell, {
      props: { show: true, align: 'top', fullscreen: true },
      global: { stubs },
    })
    expect(w.find('.n-modal-stub').classes()).not.toContain('t-modal-shell--top')
  })

  it('places the top-anchored card with a capped spacer pair', () => {
    const source = readSource()
    // The container is naive's, and it is a flex ROW. Spacers only stack above
    // and below the card once it is a column - and with the margins cleared,
    // a row hands the placement to naive's own `align-self: center`, i.e.
    // `align="top"` silently becomes `align="center"` (measured at 1000px: a
    // short dialog at 282 above / 282 below instead of 88 / 476).
    const container = /:has\(> \.t-modal-shell--top\)\s*\{([^}]*)\}/.exec(source)?.[1]
    expect(container).toMatch(/flex-direction:\s*column/)
    const pair
      = /:has\(> \.t-modal-shell--top\)::before,\s*[^{]*::after\s*\{([^}]*)\}/.exec(source)?.[1]
    // Equal shares of the leftover: that is what makes the two ends match once
    // the cap stops applying.
    expect(pair).toMatch(/flex:\s*1 1 0%/)
    // The spacers are added to the fallback margins, not substituted for them,
    // so those have to be cleared where the spacers apply. `margin-top` is the
    // half that bites (measured at 1000px: 96 above / 8 below, lower than the
    // lopsided placement this fixes); `margin-bottom: auto` measured inert,
    // because flexbox feeds the free space to flex-grow before auto margins get
    // a share, and is cleared for the same reason it is asserted here - that
    // inertness belongs to the spacers' `flex` value, not to the margin.
    const reset
      = /:has\(> \.t-modal-shell--top\) > \.t-modal-shell--top\s*\{([^}]*)\}/.exec(source)?.[1]
    expect(reset).toMatch(/margin-top:\s*0 !important/)
    expect(reset).toMatch(/margin-bottom:\s*0 !important/)
  })

  // The cap and the fallback margin are the same distance, but they live on
  // opposite sides of the SFC and cannot share a custom property: the spacer is
  // the card's sibling, and custom properties only inherit downward. Drift
  // between them would be silent - the card would simply settle a few pixels
  // off the anchor it advertises.
  it('caps the top spacer at exactly the anchor the script writes', () => {
    const source = readSource()
    const anchor = /const TOP_ANCHOR = '([^']+)'/.exec(source)?.[1]
    const cap = /::before\s*\{\s*max-height:\s*([^;]+);/.exec(source)?.[1]
    expect(anchor).toBe('min(10vh, 88px)')
    expect(cap).toBe(anchor)
  })

  it('leaves the height to the fullscreen rules when fullscreen', () => {
    const w = mount(TModalShell, { props: { show: true, fullscreen: true }, global: { stubs } })
    const style = w.findComponent(modalStub).props('style') as Record<string, string>
    expect(style.maxHeight).toBeUndefined()
    expect(w.find('.n-modal-stub').classes()).toContain('t-modal-shell--fullscreen')
  })

  // The bound is inline, but it only reaches the body through a stylesheet rule
  // - and that rule is invisible to every assertion above. naive's card content
  // area is a block whose automatic minimum is its own content: it refuses to
  // shrink, so without the rule a bounded card pushes its footer out through
  // its own bottom edge instead, which is the same unreachable footer by
  // another route. The marker class is what the rule hangs off.
  it('marks the card so the shell stylesheet can reach it', () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    expect(w.find('.n-modal-stub').classes()).toContain('t-modal-shell')
  })

  it('keeps the card content area shrinkable, or the bound only moves the overflow', () => {
    const source = readSource()
    const rule
      = /\.t-modal-shell:not\(\.t-modal-shell--fullscreen\)\s*>\s*\.n-card-content[^{]*\{([^}]*)\}/.exec(
        source,
      )?.[1]
    expect(rule).toBeDefined()
    expect(rule).toMatch(/min-height:\s*0/)
    expect(rule).toMatch(/flex-direction:\s*column/)
  })

  // The floor goes on the BODY, not on the scroll box around it. On the scroll
  // box it is also a refusal to shrink, and the card is bounded to the viewport
  // now: a floor taller than the space left over spills the body across the
  // footer (measured at `minContentHeight: 600` on a 720px viewport - body
  // bottom 721.6 against a footer starting at 658). On the body it says only
  // what it means, and the scroll box is free to give the height back.
  it('floors the body height so an empty body is not a sliver of chrome', () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    expect(w.find('.t-modal-shell__body').attributes('style')).toContain('min-height: 120px')
  })

  it('honours an explicit minContentHeight', () => {
    const w = mount(TModalShell, { props: { show: true, minContentHeight: 240 }, global: { stubs } })
    expect(w.find('.t-modal-shell__body').attributes('style')).toContain('min-height: 240px')
  })

  it('keeps the floor off the scroll box, which has to stay shrinkable', () => {
    const w = mount(TModalShell, { props: { show: true, minContentHeight: 600 }, global: { stubs } })
    expect(w.find('.t-modal-shell__scroll').attributes('style')).not.toContain('min-height')
  })

  it('writes no measured height when animateHeight is off', async () => {
    const w = mount(TModalShell, {
      props: { show: true, animateHeight: false },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    await nextTick()
    expect(w.find('.t-modal-shell__scroll--settled').exists()).toBe(false)
    // Anchored so `max-height` (which the ceiling always writes) is not
    // mistaken for the measured `height` this assertion is about.
    expect(w.find('.t-modal-shell__scroll').attributes('style')).not.toMatch(/(^|;)\s*height:/)
  })

  // -- The opening measurement -----------------------------------------------
  // The opening size is taken on the nextTick after `show` flips, i.e. partway
  // through naive's scale-in, while an ancestor of the body is still under
  // `transform: scale(...)`. `getBoundingClientRect()` reports the box AFTER
  // that transform; the ResizeObserver that is supposed to correct a stale size
  // reports the untransformed LAYOUT box. Reading the rect therefore latched a
  // height the observer would never produce and so could never revise - a modal
  // whose content was fully laid out before it opened stayed at half its height
  // (measured in Chromium: a 193px body read 96.6 at 29ms) or at the floor
  // below that, for as long as it was open.
  //
  // happy-dom does no layout and applies no transforms, so both sources read 0
  // here and mounting alone cannot tell them apart. They are faked below with
  // the numbers the browser actually produced, which is the point: these
  // assertions discriminate on WHICH source is read, not on a height the
  // environment computes.

  /** Give the body a layout box of `layout`px seen through a `scale` ancestor. */
  function fakeBox(el: HTMLElement, layout: number, scale = 0.5): void {
    Object.defineProperty(el, 'offsetHeight', { configurable: true, value: layout })
    el.getBoundingClientRect = () =>
      ({
        height: layout * scale,
        width: 0,
        top: 0,
        right: 0,
        bottom: 0,
        left: 0,
        x: 0,
        y: 0,
        toJSON: () => ({}),
      }) as DOMRect
  }

  /** The measured `height` alone - `min-height` must not be mistaken for it. */
  function measuredHeight(w: ReturnType<typeof mount>): string | undefined {
    const style = w.find('.t-modal-shell__scroll').attributes('style') ?? ''
    return /(?:^|;)\s*height:\s*([^;]+)/.exec(style)?.[1]?.trim()
  }

  /** Mount shown, fake the body's boxes, let the opening measurement run. */
  async function openWithBody(
    layout: number,
    props: Record<string, unknown> = {},
  ): Promise<ReturnType<typeof mount>> {
    const w = mount(TModalShell, {
      props: { show: true, ...props },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    fakeBox(w.find('.t-modal-shell__body').element as HTMLElement, layout)
    // Two ticks: the component queues its first measurement on the nextTick
    // after `show` flips, and the height it writes needs one more to render.
    await nextTick()
    await nextTick()
    return w
  }

  it('opens at the body layout height, not the half-scaled box the transition is drawing', async () => {
    const w = await openWithBody(193)
    expect(measuredHeight(w)).toBe('193px')
  })

  it('opens at the layout height even when the scaled box clears the floor', async () => {
    // 356 * 0.5 = 178, comfortably above `minContentHeight` - so this modal did
    // not bottom out at the floor, it settled at exactly half. The floor was
    // never what was wrong; the source was.
    const w = await openWithBody(356)
    expect(measuredHeight(w)).toBe('356px')
  })

  it('still clamps the opening height to the viewport ceiling', async () => {
    expect(window.innerHeight).toBeGreaterThan(0)
    const w = await openWithBody(4000, { contentMaxHeightVh: 50 })
    expect(measuredHeight(w)).toBe(`${(window.innerHeight * 50) / 100}px`)
  })

  it('still floors a body that has no height yet (opened before its data landed)', async () => {
    const w = await openWithBody(0)
    expect(measuredHeight(w)).toBe('120px')
  })

  // A behavioural test can only prove the source is right for the boxes it
  // fakes. The invariant is stronger and worth pinning directly: the opening
  // read and the observer's reads must be the SAME quantity, because a size
  // taken from a source the observer does not watch is a size nothing can ever
  // correct. Reading a transformed box back into the height would reintroduce
  // that whether or not an assertion above happened to cover the case.
  it('never sizes the body from a transformed box', () => {
    const source = readSource()
    // Comments stripped first, or this gate reads the paragraph that explains
    // the bug and calls it the bug. (Good enough for one known file: `//` is
    // only spared after a colon, so a `https://` inside prose survives.)
    const code = source
      .replace(/<!--[\s\S]*?-->/g, '')
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .replace(/(^|[^:])\/\/[^\n]*/g, '$1')
    expect(code).not.toMatch(/getBoundingClientRect/)
  })
})
