<template>
  <!-- The overlay provider (renderless): resets the naive theme to the GLOBAL
       mode - NModal teleports to <body> but naive forwards the content area's
       inner "Card / List" theme through provide/inject across the Teleport, so
       without it an overlay opened from a dark-card page would render dark
       under global light mode - and defaults every button and form control in
       the slots to `small` (naive's own dialogs render their buttons small,
       and a body that scrolls at 65vh has no height to spend on medium
       padding). One component for both, shared with TDrawerShell and with
       hand-rolled overlays, so the three cannot drift. See TOverlayTheme. -->
  <TOverlayTheme>
    <NModal
      :show="show"
      preset="card"
      :size="size"
      :mask-closable="maskClosable"
      :auto-focus="autoFocus"
      :title="$slots.header ? undefined : title"
      :class="[
        't-modal-shell',
        { 't-modal-shell--fullscreen': isFullscreen, 't-modal-shell--top': isTopAnchored },
      ]"
      :style="modalStyle"
      @update:show="(v: boolean) => emit('update:show', v)"
    >
    <!-- Rich header (entity name + status tag / subtitle): a `#header` slot
         overrides the plain `title` prop for callers that need more than a
         string. Omit it and the `title` prop drives the header as before.
         The override is enforced above by withholding `title` from NModal
         whenever the slot is present - naive's Card resolves its header as
         `title ? [title] : slots.header` (Card.mjs), i.e. the PROP wins and the
         slot is dropped without a warning or a DOM node. Forwarding both would
         silently render the plain title. Withholding it also matches
         TDrawerShell, where naive's DrawerContent already lets the slot win. -->
    <template v-if="$slots.header" #header>
      <slot name="header" />
    </template>
    <!-- Body scrolls inside the card so long content never pushes the header /
         footer off the viewport. `contentMaxHeightVh` is this box's preferred
         ceiling; the card's own `max-height` (see `modalStyle`) is the one that
         binds, and the body gives height back to it. Short content keeps its
         natural height (native overflow only kicks in past max-height). Plain
         `overflow:auto` (not NScrollbar) so the global polish.css macOS-style
         scrollbar applies - NScrollbar renders an overlay thumb that floats
         over (and occludes) the rightmost widgets. -->
    <div
      class="t-modal-shell__scroll"
      :class="{
        't-modal-shell__scroll--loading': loading,
        't-modal-shell__scroll--settled': heightSettles && measuredHeight !== null,
      }"
      :style="scrollStyle"
    >
      <!-- Measured wrapper. `flow-root` keeps it a block formatting context, so
           a first child's top margin counts toward this box's height instead of
           collapsing through it - otherwise the measurement comes up short and
           the modal settles a few pixels smaller than its content.
           `minContentHeight` floors THIS box rather than the scroll box around
           it: the floor is a statement about how tall an empty body should
           look, and a floor on the scroll box is also a refusal to shrink -
           which, once the card is bounded, spills the body over the footer
           instead (measured at `minContentHeight: 600` on a 720px viewport:
           body bottom 721.6 against a footer starting at 658). Floored here,
           the scroll box gives up whatever height the card needs back and the
           content scrolls, which is the same picture for every body that fits
           and the only correct one for a body that does not. -->
      <div ref="bodyRef" class="t-modal-shell__body" :style="bodyStyle">
        <NSpin :show="loading">
          <slot />
        </NSpin>
      </div>
    </div>
    <template v-if="$slots.footer" #footer>
      <!-- Chrome-level action layout: right-aligned with a uniform gap, so
           pages can drop bare buttons into #footer without them touching. -->
      <div class="t-modal-shell__footer">
        <slot name="footer" />
      </div>
    </template>
    </NModal>
  </TOverlayTheme>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch, type CSSProperties } from 'vue'
import { NModal, NSpin } from 'naive-ui'
import { useBreakpoint } from '../../headless/theme/useBreakpoints'
import TOverlayTheme from './TOverlayTheme.vue'

interface Props {
  /** Open state (controlled). */
  show: boolean
  /**
   * Plain-string header. Ignored when a `#header` slot is supplied - the slot
   * wins, and the modal renders no plain title alongside it.
   */
  title?: string
  /** Desktop width (px); capped at 95vw so a too-large value still shows a mask strip. */
  width?: number
  /**
   * Card padding scale (forwarded to naive's card preset). Default `small`
   * (12/16/12px) - the admin-compact chrome. naive's own default is `medium`
   * (19/24/20px), which reads as too roomy for dense admin dialogs. Bump to
   * `medium`/`large` for content-heavy modals.
   *
   * This is the CARD's padding only. The buttons and form controls inside the
   * slots have their own default (`small`, see `useOverlayComponentOptions`),
   * which this prop does not touch: a `size="large"` card still gets small
   * controls unless its NForm / controls say otherwise.
   */
  size?: 'small' | 'medium' | 'large' | 'huge'
  /**
   * Force fullscreen. When unset, auto-switches to fullscreen on viewports
   * narrower than `max(width + 32, 640)`.
   */
  fullscreen?: boolean
  /**
   * Max viewport height (vh) the inner scroll area may occupy. Default 65.
   *
   * A preference, not a guarantee: the card is separately bounded to the
   * viewport (see `modalStyle`), and where the two disagree the viewport wins
   * and the body scrolls that much sooner. A caller cannot do the arithmetic
   * that would keep it under the bound anyway - the top offset, the header and
   * the footer all belong to the shell and are invisible from the outside.
   */
  contentMaxHeightVh?: number
  /** Allow closing by clicking the mask. Default false (forms shouldn't lose input on a stray click). */
  maskClosable?: boolean
  /**
   * Show a spinner over the body while its data loads. The body keeps a
   * minimum height during the load so the modal doesn't collapse when the
   * content renders empty (`v-if` on the record).
   */
  loading?: boolean
  /**
   * Where the modal sits vertically.
   *   - `'top'` (default) - above centre: a third of the free space above the
   *     card and two thirds below (the optical centre), never less than an
   *     even split once the card reaches its viewport bound.
   *   - `'center'` - naive's own behaviour (centred by `margin: auto`).
   *
   * Top is the default because a modal's height is rarely known when it opens:
   * a form behind `v-if="record"` renders empty, then grows by hundreds of
   * pixels once the data lands, and the placement follows the height. Centred,
   * that growth moves the header UP by half the delta - measured at 254px on a
   * 10-field form - so the title and the close button jump out from under the
   * pointer. Biased upward, the header moves by a third of it (the same form:
   * 124px), and not at all once the card is close to its bound.
   */
  align?: 'top' | 'center'
  /**
   * Floor for the scrolling body, in px (default 120).
   *
   * A modal that opens before its data arrives has an empty body, and an empty
   * body collapses the card to a ~96px sliver of header and footer that reads
   * as a broken dialog. The floor keeps it dialog-shaped for the load - which
   * is why a consumer no longer has to be told to size the modal by hand.
   */
  minContentHeight?: number
  /**
   * Ease the body between heights as its content changes (default true).
   *
   * With the floor and the top anchor, a modal still SNAPS from the loading
   * size to the loaded one. Easing that step is what turns "the window suddenly
   * got bigger" into the dialog settling. `false` restores the plain
   * auto-height box; ignored in fullscreen, where the body is sized by the flex
   * column rather than by its content.
   */
  animateHeight?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  width: 560,
  size: 'small',
  fullscreen: undefined,
  contentMaxHeightVh: 65,
  maskClosable: false,
  loading: false,
  align: 'top',
  minContentHeight: 120,
  animateHeight: true,
})

const emit = defineEmits<{ 'update:show': [value: boolean] }>()

const bp = useBreakpoint()

// Suppress naive's default first-focusable auto-focus on phones: it would grab
// the first input/search box on open and pop the soft keyboard, covering half
// the screen before the user has read anything. Desktop keeps auto-focus (users
// expect to start typing in a create/edit form immediately). `trap-focus` stays
// on either way (a11y).
const autoFocus = computed<boolean>(() => !bp.isSm.value)

// Auto-fullscreen when the viewport can't fit the configured width with room:
//   - width + 32 → never crop at the sides
//   - 640 (sm)  → catch large phones in portrait where even a 560px modal
//                 would visually compete with the whole screen
const isFullscreen = computed<boolean>(() => {
  if (typeof props.fullscreen === 'boolean') return props.fullscreen
  const threshold = Math.max(props.width + 32, 640)
  return bp.width.value > 0 && bp.width.value < threshold
})

/**
 * Headroom the card's bound reserves above it when `align="top"`. Together
 * with `VIEWPORT_GUTTER` it fixes the slack a card at its bound has left
 * (`anchor + gutter`), which the stylesheet then splits evenly - so it decides
 * how much mask shows around the tallest dialogs, not where a short one sits
 * (that is the 1:2 split in the stylesheet). It is also the top margin a
 * browser without `:has()` falls back to. `min(10vh, 88px)` keeps that
 * headroom proportionate on a short screen.
 */
const TOP_ANCHOR = 'min(10vh, 88px)'
/** Breathing room kept clear between the card and the viewport edge below it. */
const VIEWPORT_GUTTER = '16px'

/**
 * Whether the card takes the `align="top"` placement. Fullscreen is excluded
 * because there the card IS the viewport, so there is nothing to place - and
 * the marker class this drives puts the shell's placement rules on naive's
 * layout container, which fullscreen takes the card out of (`position: fixed`).
 */
const isTopAnchored = computed<boolean>(() => props.align === 'top' && !isFullscreen.value)

const modalStyle = computed<CSSProperties>(() => {
  if (isFullscreen.value) return { width: '100vw', maxWidth: '100vw' }
  // Cap at 95vw so a too-large `width` still leaves a visible mask strip.
  const style: CSSProperties = { width: `min(${props.width}px, 95vw)` }
  // The CARD is bounded to the viewport, not just its scrolling body. Capping
  // only the body (`contentMaxHeightVh`) left the top offset, the header and
  // the footer to stack on top of that cap, so the card ran past the bottom
  // edge and took the Save / Cancel row with it - measured in Chromium at
  // `contentMaxHeightVh: 88` on an 820px viewport: card bottom 911, i.e. 91px
  // of it, footer included, below the fold. `dvh` so a mobile URL bar or soft
  // keyboard shrinks the bound rather than hiding the footer behind itself.
  if (isTopAnchored.value) {
    // These two margins are the FALLBACK placement. The stylesheet's spacer
    // pair below normally owns it and zeroes them; they are what a browser
    // without `:has()` gets, and they are also the placement this component
    // shipped with before the spacers existed.
    //
    // naive centres the card with `margin: auto`; overriding only the BLOCK
    // margins leaves the inline `auto` in place, so it stays horizontally
    // centred. The bottom one is `auto`, not a fixed value: naive centres by
    // letting BOTH block margins resolve to auto inside a flex row, so
    // replacing only the top one leaves the box centred in what is left;
    // letting the bottom one keep absorbing the slack is what pins the top.
    style.marginTop = TOP_ANCHOR
    style.marginBottom = 'auto'
    style.maxHeight = `calc(100dvh - ${TOP_ANCHOR} - ${VIEWPORT_GUTTER})`
  } else {
    // Centred: naive's own `margin: auto` splits the slack, so one gutter at
    // each end.
    style.maxHeight = `calc(100dvh - ${VIEWPORT_GUTTER} * 2)`
  }
  return style
})

const contentMaxHeight = computed(() =>
  // Fullscreen leaves room for header (~56) + footer (~64) + safe area (~24).
  // `dvh` so the soft keyboard / mobile address bar shrinks the height instead
  // of the footer buttons ending up below the fold and unreachable.
  isFullscreen.value ? 'calc(100dvh - 144px)' : `${props.contentMaxHeightVh}vh`,
)

// -- Settling the body height ------------------------------------------------
// The body is measured and its height written back, because `height: auto` is
// the one value CSS cannot transition - and the whole point here is to make the
// step from "loading" to "loaded" a movement rather than a jump.
const bodyRef = ref<HTMLElement>()
const measuredHeight = ref<number | null>(null)
let observer: ResizeObserver | null = null

const heightSettles = computed(() => props.animateHeight && !isFullscreen.value)

const scrollStyle = computed<CSSProperties>(() => {
  const style: CSSProperties = { maxHeight: contentMaxHeight.value }
  if (heightSettles.value && measuredHeight.value !== null) {
    style.height = `${measuredHeight.value}px`
  }
  return style
})

/** The floor, on the body rather than on the scroll box around it - see template. */
const bodyStyle = computed<CSSProperties>(() => ({ minHeight: `${props.minContentHeight}px` }))

/** The body's ceiling in px - `contentMaxHeightVh` resolved against the viewport. */
function maxBodyHeight(): number {
  const viewport = typeof window === 'undefined' ? 0 : window.innerHeight
  return viewport > 0 ? (viewport * props.contentMaxHeightVh) / 100 : Number.POSITIVE_INFINITY
}

function measure(): void {
  const el = bodyRef.value
  if (!el || !heightSettles.value) return
  // `offsetHeight` - the LAYOUT box - and deliberately not
  // `getBoundingClientRect().height`, which is the box AFTER transforms.
  //
  // The first measurement runs on the nextTick after `show` flips, which lands
  // in the MIDDLE of naive's scale-in: an ancestor is still under
  // `transform: scale(...)`, so a rect reports a fraction of the real box.
  // Measured in Chromium on a 193px body - rect 96.6 at 29ms, 117.9 at 87ms,
  // 193.2 only once the transition ended, while `offsetHeight` read 193 from
  // the very first frame. Reading the rect therefore latched the opening size
  // off a half-drawn frame, and a modal whose content was fully laid out before
  // it opened settled at half its height (or at the floor below that), the rest
  // clipped behind an inner scrollbar.
  //
  // Nothing corrected it afterwards, and nothing could have: the correction is
  // a ResizeObserver, and an observer reports the untransformed LAYOUT box,
  // which was 193 from the first frame to the last - it never changed, so no
  // callback ever fired. A number the observer would never produce is a number
  // the observer can never revise. Measuring the layout box here is what makes
  // that whole class of miss impossible rather than merely fixed: the opening
  // read and every later read are now literally the same quantity.
  //
  // `offsetHeight` is the border box rounded to a whole pixel. The <=0.5px it
  // gives up costs nothing: verified in Chromium across the fractional range
  // that a 193.48px body sized to `height: 193px` still reports no overflow and
  // grows no scrollbar (scrollable overflow is snapped the same way).
  const next = Math.min(Math.max(el.offsetHeight, props.minContentHeight), maxBodyHeight())
  // The first measurement IS the opening size and must land without a
  // transition - otherwise the modal unfolds from the floor every single time
  // it opens, which is a worse version of the jump this exists to remove. The
  // `--settled` class (and with it the transition) only goes on afterwards.
  if (measuredHeight.value === null) {
    measuredHeight.value = next
    return
  }
  // Sub-pixel churn (a scrollbar appearing, a webfont settling) must not feed
  // itself back through the observer.
  if (Math.abs(next - measuredHeight.value) > 1) measuredHeight.value = next
}

function startObserving(): void {
  stopObserving()
  if (!heightSettles.value || typeof ResizeObserver === 'undefined') return
  void nextTick(() => {
    const el = bodyRef.value
    if (!el) return
    measure()
    observer = new ResizeObserver(() => measure())
    observer.observe(el)
  })
}

function stopObserving(): void {
  observer?.disconnect()
  observer = null
}

watch(
  () => props.show,
  (open) => {
    if (open) {
      startObserving()
    } else {
      stopObserving()
      // Reset so the NEXT open measures from scratch instead of animating down
      // from whatever size the previous record happened to need.
      measuredHeight.value = null
    }
  },
  { immediate: true },
)

// Fullscreen hands sizing to the flex column; a leftover fixed height fights it.
watch(isFullscreen, (full) => {
  if (full) {
    stopObserving()
    measuredHeight.value = null
  } else if (props.show) {
    startObserving()
  }
})

onBeforeUnmount(stopObserving)
</script>

<style scoped>
.t-modal-shell__scroll {
  overflow-y: auto;
  overflow-x: hidden;
}
/* Applied only once an opening height is in place - see `measure`. */
.t-modal-shell__scroll--settled {
  transition: height 0.22s cubic-bezier(0.4, 0, 0.2, 1);
}
@media (prefers-reduced-motion: reduce) {
  .t-modal-shell__scroll--settled {
    transition: none;
  }
}
.t-modal-shell__body {
  display: flow-root;
}
/* While loading, guarantee the spin container a working height: an empty body
   (content behind `v-if`) would otherwise collapse to ~0 and squash the
   spinner. :deep because NSpin's wrapper is a child component element. */
.t-modal-shell__scroll--loading :deep(.n-spin-container) {
  min-height: 120px;
}
.t-modal-shell__footer {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  flex-wrap: wrap;
}
/* Phone: stack the action buttons full-width (thumb-friendly). `column` keeps
   DOM order (Cancel above, primary below at thumb reach). naive buttons are
   inline-flex so `align-items: stretch` alone won't widen them - set an explicit
   full width. `:deep` because the buttons are the consumer's, not this scope. */
@media (max-width: 767px) {
  .t-modal-shell__footer {
    flex-direction: column;
    align-items: stretch;
  }
  .t-modal-shell__footer :deep(.n-button) {
    width: 100%;
  }
}
</style>

<!-- These target the teleported modal root, so they can't be scoped. NModal
     merges our class onto the card element itself (`.n-card.n-modal`). -->
<style>
/* Where the card sits: a third of the slack above it, two thirds below - the
   optical centre - but never less than half the slack its bound leaves, so a
   card AT the bound splits that evenly.
   Two spacers make this `max(slack / 3, (anchor + gutter) / 2)` without
   measuring anything: the top one claims one share of whatever the card leaves
   over, the bottom one two, and the top one has a floor of half the minimum
   slack. Flexbox clamps a spacer that would fall under its floor and hands the
   rest to the other one. Being pure layout, it re-balances on a window resize,
   which a number computed at open would not.
   Measured in Chromium (card / above / below):
     221.6px card, 1205px viewport   327.8 / 655.6   (a third; was 88 / 895)
     306.6px card, 1000 / 768 / 600  231 / 154 / 98 above, always a third
     card at its bound, 1000 / 720   52 / 52 and 44 / 44 (unchanged)
     861.6px card, 1000px            52 / 86.4   (floor binding, see below)
   This replaced a fixed anchor of min(10vh, 88px), under which a short dialog
   on a tall screen hugged the top with most of the screen empty beneath it -
   88 above against 895 below on the first row above - which consumers read as
   "every small dialog sits too high".
   The cost is header movement while a dialog grows after opening (its data
   landing behind `v-if`). Any placement that is a function of the card height
   moves the card when the height changes; the fixed anchor did not move it at
   all, and centring moved it by half the growth - measured at 254px on a
   10-field form, the reason `align="top"` exists. This rule moves the header
   up by a THIRD of the growth, until the card is within 1.5x the minimum slack
   of its bound (slack under 156px at 1000px), after which the floor binds and
   the header holds still. Measured: the same 10-field form (221.6 -> 593.6 on
   894px) moves its header 124px, where centring would move it 186; the floor
   to the bound on 1000px moves it 207; a small step (224.6 -> 306.6) moves it
   27; growth entirely inside the floor's range moves it 0. That is the
   trade-off the ratio buys: a third of the growth for a third of the slack.
   `:has()` scopes the rules to OUR modal - the container belongs to naive and
   is shared with every other modal in the app. Where `:has()` is missing the
   whole block drops out and the inline margins place the card at the anchor,
   which is the placement this component shipped with before the spacers. */
/* naive's container is a flex ROW, where spacers would sit beside the card
   rather than above and below it. The column switch is also what keeps the
   1:2 split: with the margins cleared below, naive's own `align-self: center`
   takes over in a row and centres the card - measured at 1000px, a 429.6px
   card goes to 285 above / 285 below instead of 190 / 380. So losing this line
   turns `align="top"` into `align="center"`, silently. */
.n-modal-scroll-content:has(> .t-modal-shell--top) {
  flex-direction: column;
}
.n-modal-scroll-content:has(> .t-modal-shell--top)::before,
.n-modal-scroll-content:has(> .t-modal-shell--top)::after {
  content: '';
}
/* One share above, two below: the optical centre. The floor is half of the
   slack the card's bound leaves (`100dvh - anchor - gutter` of card means
   `anchor + gutter` of slack), so a card AT its bound splits that slack
   evenly, and the two terms have to be the script's `TOP_ANCHOR` and
   `VIEWPORT_GUTTER` verbatim (gated by a test): a custom property cannot carry
   them here, because it would have to travel from the card to the card's own
   sibling, and custom properties only inherit downward. */
.n-modal-scroll-content:has(> .t-modal-shell--top)::before {
  flex: 1 1 0%;
  min-height: calc((min(10vh, 88px) + 16px) / 2);
}
.n-modal-scroll-content:has(> .t-modal-shell--top)::after {
  flex: 2 1 0%;
}
/* The spacers own the placement now, so the fallback margins have to go.
   `margin-top` is the one that bites: a fixed margin is part of the card's
   outer size, so it is taken out of the free space BEFORE the spacers divide
   what is left, and it then adds to the spacer above - measured at 1000px, a
   card at its bound lands at 96 above / 8 below, i.e. lower than it sat before
   any of this. `margin-bottom: auto` measured inert (52 / 52 either way):
   flexbox resolves flexible lengths before it distributes free space to auto
   margins, so a growing spacer leaves an auto margin nothing to take. It is
   cleared anyway, because "inert" here is a property of the spacers' `flex`
   value rather than of the margin. `!important` because the fallback arrives
   as an inline style. */
.n-modal-scroll-content:has(> .t-modal-shell--top) > .t-modal-shell--top {
  margin-top: 0 !important;
  margin-bottom: 0 !important;
}

/* A `max-height` on the card only moves the problem unless the body can give
   the height back. naive's card is already a flex column, but its content area
   is a block: its automatic minimum is its own content, so it refuses to shrink
   and pushes the footer out through the bottom of a card that is itself the
   right height. Making it a shrinkable flex column hands the shortfall to the
   scroll region, which is the one box in here that knows how to lose height -
   it grows a scrollbar. `> ` so a card nested INSIDE a dialog body keeps its
   own layout, `:not(--fullscreen)` because fullscreen sizes the body by what
   the flex column has left over instead (rule below) and must win regardless of
   source order. */
.t-modal-shell:not(.t-modal-shell--fullscreen) > .n-card__content,
.t-modal-shell:not(.t-modal-shell--fullscreen) > .n-card-content {
  flex: 0 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-modal-shell--fullscreen {
  position: fixed !important;
  inset: 0 !important;
  height: 100vh !important;
  height: 100dvh !important; /* dvh: shrink with the mobile URL bar / keyboard */
  max-height: 100vh !important;
  max-height: 100dvh !important;
  border-radius: 0 !important;
  margin: 0 !important;
}
.t-modal-shell--fullscreen .n-card {
  border-radius: 0;
  height: 100vh;
  height: 100dvh;
  max-height: 100vh;
  max-height: 100dvh;
  display: flex;
  flex-direction: column;
}
.t-modal-shell--fullscreen .n-card__content,
.t-modal-shell--fullscreen .n-card-content {
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
/* Fullscreen: size the body by what is actually left over, not by a viewport
   guess. `contentMaxHeight` reserves ~64px for the footer, but on phones the
   footer stacks its buttons full-width in a column - a two-button footer is
   ~100px tall. The body's max-height then exceeded the space the card really
   had, and the `overflow: hidden` above clipped the last fields of the form
   with no way to scroll down to them. `!important` because `contentMaxHeight`
   arrives as an inline style. */
.t-modal-shell--fullscreen .t-modal-shell__scroll {
  flex: 1 1 auto;
  min-height: 0;
  max-height: none !important;
}
/* Keep the footer action row clear of the iOS home indicator when fullscreen. */
.t-modal-shell--fullscreen .n-card__footer {
  padding-bottom: max(var(--n-padding-bottom, 16px), env(safe-area-inset-bottom));
}
</style>
