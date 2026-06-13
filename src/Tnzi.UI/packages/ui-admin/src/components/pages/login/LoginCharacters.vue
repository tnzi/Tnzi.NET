<script setup lang="ts">
/**
 * `LoginCharacters` — four playful geometric characters for the `split`
 * login layout's brand panel. Full port of
 * `careercompass-main/src/components/ui/animated-characters.tsx`
 * (AnimatedCharacters + EyeBall + Pupil), 2026-06-11.
 *
 * Idle behaviour (cursor-driven):
 *   - Faces (eye row + mouth) shift toward the cursor: `faceX = dx/20`
 *     clamped ±15, `faceY = dy/30` clamped ±10, measured from each
 *     character's own rect (center-x, top third).
 *   - Bodies lean toward the cursor: `skewX(clamp(-dx/120, ±6°))`,
 *     transform-origin bottom.
 *   - Pupils track the cursor inside each eye (polar `atan2` math, radius
 *     capped at 4-5px, 0.1s ease-out).
 *   - The two eyeball characters blink randomly every 3-7s (150ms squash).
 *
 * Form-driven behaviour (props fed from `PwdLogin` via the login context):
 *   - `typing` (username focused) or password-being-typed-while-hidden →
 *     the tall one stretches its neck (400→440px), leans 12° and slides
 *     40px toward the form to peek; the slim one leans 1.5× harder.
 *   - The first 800ms of `typing` → the tall and slim characters glance at
 *     each other (eye rows jump to face one another, forced pupil look).
 *   - Password visible → everyone snaps upright (skew 0) and turns their
 *     faces away (forced look up-left) — politely not watching…
 *   - …except the tall one, which sneaks a peek every 2-5s for 800ms
 *     (pupils flick toward the form, then away again).
 *
 * The 550×400 design-coordinate stage scales via ResizeObserver to fit the
 * panel. Decorative only: `aria-hidden`, `pointer-events: none`; under
 * `prefers-reduced-motion` no listeners/timers are attached and the scene
 * stays in its neutral pose. The tall character follows the theme
 * (`--tnzi-primary-300`); the rest keep the reference palette.
 */
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

interface Props {
  /** The username field has focus — characters lean in / glance at each other. */
  typing?: boolean
  /** The password is shown in clear text — characters look away (mostly). */
  passwordVisible?: boolean
  /** Characters of password typed so far. */
  passwordLength?: number
}

const props = withDefaults(defineProps<Props>(), {
  typing: false,
  passwordVisible: false,
  passwordLength: 0,
})

const STAGE_W = 550
const STAGE_H = 400
const INK = '#2d2d2d'

interface CharacterDef {
  key: 'tall' | 'slim' | 'round' | 'bean'
  left: number
  width: number
  height: number
  color: string
  radius: number
  z: number
  /** Eye row base offset + geometry (design px). */
  eyes: { left: number; top: number; gap: number; size: number; pupil?: number; maxDistance: number }
  mouth?: { left: number; top: number; width: number; height: number }
}

const CHARS: CharacterDef[] = [
  {
    key: 'tall',
    left: 70,
    width: 180,
    height: 400,
    color: 'var(--tnzi-primary-300, #6c3ff5)',
    radius: 10,
    z: 1,
    eyes: { left: 45, top: 40, gap: 32, size: 18, pupil: 7, maxDistance: 5 },
  },
  {
    key: 'slim',
    left: 240,
    width: 120,
    height: 310,
    color: INK,
    radius: 8,
    z: 2,
    eyes: { left: 26, top: 32, gap: 24, size: 16, pupil: 6, maxDistance: 4 },
  },
  {
    key: 'round',
    left: 0,
    width: 240,
    height: 200,
    color: '#ff9b6b',
    radius: 120,
    z: 3,
    eyes: { left: 82, top: 90, gap: 32, size: 12, maxDistance: 5 },
  },
  {
    key: 'bean',
    left: 310,
    width: 140,
    height: 230,
    color: '#e8d754',
    radius: 70,
    z: 4,
    eyes: { left: 52, top: 40, gap: 24, size: 12, maxDistance: 5 },
    mouth: { left: 40, top: 88, width: 80, height: 4 },
  },
]

interface CharRuntime {
  faceX: number
  faceY: number
  skew: number
  pupils: Array<{ x: number; y: number }>
}

const wrapEl = ref<HTMLElement | null>(null)
const stageEl = ref<HTMLElement | null>(null)
const charEls = ref<Array<HTMLElement | null>>(CHARS.map(() => null))
const eyeEls = ref<Array<Array<HTMLElement | null>>>(CHARS.map(() => [null, null]))
const scale = ref(1)
const motion = ref(false) // true once mounted, unless prefers-reduced-motion
const rt = ref<CharRuntime[]>(
  CHARS.map(() => ({ faceX: 0, faceY: 0, skew: 0, pupils: [{ x: 0, y: 0 }, { x: 0, y: 0 }] })),
)
const blinks = ref<boolean[]>(CHARS.map(() => false))
const looking = ref(false) // tall + slim glance at each other (first 800ms of typing)
const peeking = ref(false) // tall sneaks a peek at the visible password

// ---- scene flags (reference: AnimatedCharacters) ----
const pwdVisible = computed(() => motion.value && props.passwordLength > 0 && props.passwordVisible)
const pwdHidden = computed(() => motion.value && props.passwordLength > 0 && !props.passwordVisible)
/** The tall one stretches + leans toward the form. */
const leanIn = computed(() => motion.value && (props.typing || pwdHidden.value))

const stageStyle = computed(() => ({ transform: `scale(${scale.value})` }))

function setCharEl(i: number) {
  return (el: unknown) => {
    charEls.value[i] = (el as HTMLElement | null) ?? null
  }
}
function setEyeEl(i: number, e: number) {
  return (el: unknown) => {
    eyeEls.value[i]![e] = (el as HTMLElement | null) ?? null
  }
}

// ---- per-character style state machine ----

function bodyStyle(c: CharacterDef, i: number): Record<string, string> {
  const skew = rt.value[i]!.skew
  let transform = `skewX(${skew}deg)`
  if (pwdVisible.value) {
    transform = 'skewX(0deg)'
  } else if (c.key === 'tall' && leanIn.value) {
    transform = `skewX(${skew - 12}deg) translateX(40px)`
  } else if (c.key === 'slim') {
    if (looking.value) transform = `skewX(${skew * 1.5 + 10}deg) translateX(20px)`
    else if (props.typing || pwdHidden.value) transform = `skewX(${skew * 1.5}deg)`
  }
  const height = c.key === 'tall' && leanIn.value ? 440 : c.height
  return {
    left: `${c.left}px`,
    width: `${c.width}px`,
    height: `${height}px`,
    backgroundColor: c.color,
    borderRadius: `${c.radius}px ${c.radius}px 0 0`,
    zIndex: String(c.z),
    transform,
  }
}

function facePos(c: CharacterDef, i: number): { x: number; y: number } {
  const r = rt.value[i]!
  if (pwdVisible.value) {
    if (c.key === 'tall') return { x: 20, y: 35 }
    if (c.key === 'slim') return { x: 10, y: 28 }
    if (c.key === 'round') return { x: 50, y: 85 }
    return { x: 20, y: 35 }
  }
  if (looking.value) {
    if (c.key === 'tall') return { x: 55, y: 65 }
    if (c.key === 'slim') return { x: 32, y: 12 }
  }
  return { x: c.eyes.left + r.faceX, y: c.eyes.top + r.faceY }
}

function eyesStyle(c: CharacterDef, i: number): Record<string, string> {
  const pos = facePos(c, i)
  return { left: `${pos.x}px`, top: `${pos.y}px`, gap: `${c.eyes.gap}px` }
}

function eyeStyle(c: CharacterDef, i: number): Record<string, string> {
  // Eyeball characters get a white sclera that squashes when blinking;
  // dot-eye characters are a bare pupil (the dot itself translates).
  return {
    width: `${c.eyes.size}px`,
    height: blinks.value[i] ? '2px' : `${c.eyes.size}px`,
    backgroundColor: c.eyes.pupil ? '#fff' : 'transparent',
    overflow: c.eyes.pupil ? 'hidden' : 'visible',
  }
}

/** Forced pupil direction for scripted moments — null falls back to cursor tracking. */
function forcedLook(c: CharacterDef): { x: number; y: number } | null {
  if (pwdVisible.value) {
    if (c.key === 'tall') return peeking.value ? { x: 4, y: 5 } : { x: -4, y: -4 }
    if (c.key === 'slim') return { x: -4, y: -4 }
    return { x: -5, y: -4 }
  }
  if (looking.value) {
    if (c.key === 'tall') return { x: 3, y: 4 }
    if (c.key === 'slim') return { x: 0, y: -4 }
  }
  return null
}

function pupilStyle(c: CharacterDef, i: number, e: number): Record<string, string> {
  const look = forcedLook(c) ?? rt.value[i]!.pupils[e]!
  const size = c.eyes.pupil ?? c.eyes.size
  return {
    width: `${size}px`,
    height: `${size}px`,
    transform: `translate(${look.x}px, ${look.y}px)`,
  }
}

function mouthStyle(c: CharacterDef, i: number): Record<string, string> {
  const m = c.mouth!
  const pos = pwdVisible.value
    ? { x: 10, y: 88 }
    : { x: m.left + rt.value[i]!.faceX, y: m.top + rt.value[i]!.faceY }
  return { left: `${pos.x}px`, top: `${pos.y}px`, width: `${m.width}px`, height: `${m.height}px` }
}

// ---- cursor tracking (faces, body lean, pupils) ----

const clamp = (v: number, min: number, max: number): number => Math.max(min, Math.min(max, v))

let raf = 0
let cleanups: Array<() => void> = []

function onMouseMove(event: MouseEvent): void {
  if (raf) return
  raf = requestAnimationFrame(() => {
    raf = 0
    const next: CharRuntime[] = []
    for (let i = 0; i < CHARS.length; i++) {
      const c = CHARS[i]!
      const prev = rt.value[i]!
      const el = charEls.value[i]
      if (!el) {
        next.push(prev)
        continue
      }
      // Reference math: center-x, top third of the character's own rect.
      const rect = el.getBoundingClientRect()
      const dx = event.clientX - (rect.left + rect.width / 2)
      const dy = event.clientY - (rect.top + rect.height / 3)
      const faceX = clamp(dx / 20, -15, 15)
      const faceY = clamp(dy / 30, -10, 10)
      const skew = clamp(-dx / 120, -6, 6)
      // Pupils: polar offset toward the cursor, capped at maxDistance.
      const pupils = [0, 1].map((e) => {
        const eye = eyeEls.value[i]![e]
        if (!eye) return prev.pupils[e]!
        const eyeRect = eye.getBoundingClientRect()
        const ex = event.clientX - (eyeRect.left + eyeRect.width / 2)
        const ey = event.clientY - (eyeRect.top + eyeRect.height / 2)
        const distance = Math.min(Math.hypot(ex, ey), c.eyes.maxDistance)
        const angle = Math.atan2(ey, ex)
        return { x: Math.cos(angle) * distance, y: Math.sin(angle) * distance }
      })
      next.push({ faceX, faceY, skew, pupils })
    }
    rt.value = next
  })
}

// ---- scripted moments ----

function scheduleBlink(i: number): void {
  const delay = 3000 + Math.random() * 4000
  const open = window.setTimeout(() => {
    blinks.value[i] = true
    blinks.value = [...blinks.value]
    const close = window.setTimeout(() => {
      blinks.value[i] = false
      blinks.value = [...blinks.value]
      scheduleBlink(i)
    }, 150)
    cleanups.push(() => window.clearTimeout(close))
  }, delay)
  cleanups.push(() => window.clearTimeout(open))
}

// Glance at each other for the first 800ms of typing.
let lookTimer = 0
watch(
  () => motion.value && props.typing,
  (typing) => {
    window.clearTimeout(lookTimer)
    if (typing) {
      looking.value = true
      lookTimer = window.setTimeout(() => {
        looking.value = false
      }, 800)
    } else {
      looking.value = false
    }
  },
)

// Sneaky peek loop while the password is visible.
let peekTimer = 0
let peekEndTimer = 0
function schedulePeek(): void {
  peekTimer = window.setTimeout(() => {
    peeking.value = true
    peekEndTimer = window.setTimeout(() => {
      peeking.value = false
      schedulePeek()
    }, 800)
  }, 2000 + Math.random() * 3000)
}
watch(pwdVisible, (visible) => {
  window.clearTimeout(peekTimer)
  window.clearTimeout(peekEndTimer)
  peeking.value = false
  if (visible) schedulePeek()
})

onMounted(() => {
  if (typeof ResizeObserver !== 'undefined' && wrapEl.value) {
    const observer = new ResizeObserver((entries) => {
      const box = entries[0]?.contentRect
      if (!box) return
      scale.value = Math.min(box.width / STAGE_W, box.height / STAGE_H, 1)
    })
    observer.observe(wrapEl.value)
    cleanups.push(() => observer.disconnect())
  }
  const reducedMotion =
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  if (reducedMotion) return
  motion.value = true
  window.addEventListener('mousemove', onMouseMove, { passive: true })
  cleanups.push(() => window.removeEventListener('mousemove', onMouseMove))
  // Only the two eyeball characters blink (reference behaviour).
  scheduleBlink(0)
  scheduleBlink(1)
})

onBeforeUnmount(() => {
  if (raf) cancelAnimationFrame(raf)
  window.clearTimeout(lookTimer)
  window.clearTimeout(peekTimer)
  window.clearTimeout(peekEndTimer)
  cleanups.forEach((fn) => fn())
  cleanups = []
})
</script>

<template>
  <div ref="wrapEl" class="t-login-chars" aria-hidden="true" data-test="t-login-page-characters">
    <div ref="stageEl" class="t-login-chars__stage" :style="stageStyle">
      <!-- soft ground: a wide group shadow + a contact shadow per character,
           so the flat bottoms read as "standing on a floor" instead of being
           cut off by a hard line -->
      <span class="t-login-chars__ground" />
      <span
        v-for="c in CHARS"
        :key="`foot-${c.key}`"
        class="t-login-chars__foot"
        :style="{ left: `${c.left + 6}px`, width: `${c.width - 12}px` }"
      />
      <div
        v-for="(c, i) in CHARS"
        :key="c.key"
        :ref="setCharEl(i)"
        class="t-login-chars__char"
        :style="bodyStyle(c, i)"
      >
        <div
          class="t-login-chars__eyes"
          :class="c.eyes.pupil ? 't-login-chars__eyes--slow' : 't-login-chars__eyes--fast'"
          :style="eyesStyle(c, i)"
        >
          <span
            v-for="e in 2"
            :key="e"
            :ref="setEyeEl(i, e - 1)"
            class="t-login-chars__eye"
            :style="eyeStyle(c, i)"
          >
            <span
              v-if="!blinks[i] || !c.eyes.pupil"
              class="t-login-chars__pupil"
              :class="{ 't-login-chars__pupil--bare': !c.eyes.pupil }"
              :style="pupilStyle(c, i, e - 1)"
            />
          </span>
        </div>
        <span v-if="c.mouth" class="t-login-chars__mouth" :style="mouthStyle(c, i)" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-login-chars {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  /* Vertically centered in the panel's flexible middle area. */
  align-items: center;
  justify-content: center;
  pointer-events: none;
}
.t-login-chars__stage {
  position: relative;
  width: 550px;
  height: 400px;
  flex: none;
  /* Scale around the center so the group stays vertically centered. */
  transform-origin: center center;
}
.t-login-chars__char {
  position: absolute;
  bottom: 0;
  transform-origin: center bottom;
  transition: all 0.7s ease-in-out;
}
/* ---- soft ground (group glow + per-character contact shadows) ---- */
.t-login-chars__ground {
  position: absolute;
  left: 50%;
  bottom: -26px;
  width: 560px;
  height: 64px;
  transform: translateX(-50%);
  border-radius: 50%;
  background: radial-gradient(ellipse at center, rgba(3, 28, 38, 0.4), transparent 70%);
  filter: blur(12px);
}
.t-login-chars__foot {
  position: absolute;
  bottom: -7px;
  height: 14px;
  border-radius: 50%;
  background: radial-gradient(ellipse at center, rgba(3, 28, 38, 0.45), transparent 72%);
  filter: blur(4px);
}
.t-login-chars__eyes {
  position: absolute;
  display: flex;
}
.t-login-chars__eyes--slow {
  transition: all 0.7s ease-in-out;
}
.t-login-chars__eyes--fast {
  transition: all 0.2s ease-out;
}
.t-login-chars__eye {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 999px;
  overflow: hidden;
  transition: height 0.15s ease-out;
}
.t-login-chars__pupil {
  border-radius: 50%;
  background-color: #2d2d2d;
  transition: transform 0.1s ease-out;
  flex: none;
}
.t-login-chars__mouth {
  position: absolute;
  border-radius: 999px;
  background-color: #2d2d2d;
  transition: all 0.2s ease-out;
}
@media (prefers-reduced-motion: reduce) {
  .t-login-chars__char,
  .t-login-chars__eyes--slow,
  .t-login-chars__eyes--fast,
  .t-login-chars__eye,
  .t-login-chars__pupil,
  .t-login-chars__mouth {
    transition: none;
  }
}
</style>
