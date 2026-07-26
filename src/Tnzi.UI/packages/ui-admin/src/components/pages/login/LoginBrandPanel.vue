<script setup lang="ts">
/**
 * `LoginBrandPanel` - brand narrative panel for the `split` login layout
 * (2026-06-11 redesign, 方案 B). A deep primary gradient base carrying a
 * careercompass-style flowing aurora: layered morphing gradient blobs (screen
 * blend), a fine grain to break banding, a slow diagonal light sheen, a
 * rising particle field, plus drifting outline rings. Foreground: brand logo
 * + name on top, the four animated characters as the centerpiece, tagline +
 * sub copy at the bottom, and a copyright line.
 *
 * All colors derive from the `--tnzi-primary-*` palette tokens so the panel
 * follows the consumer's theme. Below 768px the panel collapses to a brand
 * strip (sub copy / characters / copyright hidden, compact paddings) - the
 * parent sizes it to a fixed height there.
 *
 * Decorative layers are `aria-hidden`; all animation stops under
 * `prefers-reduced-motion`.
 */
import { TSvgIcon } from '@tnzi/ui'
import LoginCharacters from './LoginCharacters.vue'
import type { LoginSceneState } from '../../../pages/login/useLoginContext'

interface Props {
  /** Brand title (e.g. "Tnzi Admin"). */
  brand: string
  /** Iconify icon for the logo box. Falls back to the brand's first letter. */
  brandIcon?: string
  /** Headline (tagline) shown at the bottom of the panel. */
  tagline: string
  /** Supporting line under the tagline. Hidden in strip mode. */
  taglineSub?: string
  /** Copyright line at the very bottom. Hidden in strip mode. */
  copyright?: string
  /** Live form signals driving the characters (typing / password state). */
  scene?: LoginSceneState
}

const props = withDefaults(defineProps<Props>(), {
  brandIcon: undefined,
  taglineSub: undefined,
  copyright: undefined,
  scene: undefined,
})

const brandInitial = props.brand.trim().charAt(0).toUpperCase() || 'T'

/** Deterministic rising-particle field (no Math.random → stable tests/SSR). */
function buildSparks(count: number): Array<Record<string, string>> {
  let seed = 21
  const rnd = (): number => {
    seed = (seed * 1103515245 + 12345) & 0x7fffffff
    return seed / 0x7fffffff
  }
  return Array.from({ length: count }, () => {
    const size = (2 + rnd() * 3.5).toFixed(1)
    return {
      left: `${(rnd() * 100).toFixed(2)}%`,
      bottom: `${(-10 + rnd() * 30).toFixed(2)}%`,
      width: `${size}px`,
      height: `${size}px`,
      opacity: (0.3 + rnd() * 0.5).toFixed(2),
      animationDuration: `${(7 + rnd() * 9).toFixed(1)}s`,
      animationDelay: `${(-rnd() * 12).toFixed(1)}s`,
    }
  })
}
const sparks = buildSparks(22)

const RINGS = [
  { size: 210, left: '58%', top: '16%', opacity: 0.22, duration: '14s', delay: '0s' },
  { size: 120, left: '12%', top: '56%', opacity: 0.26, duration: '11s', delay: '-4s' },
  { size: 70, left: '66%', top: '64%', opacity: 0.3, duration: '9s', delay: '-2s' },
] as const
</script>

<template>
  <div class="t-login-brand" data-test="t-login-page-brand-panel">
    <!-- flowing aurora background -->
    <div class="t-login-brand__aurora" aria-hidden="true">
      <span class="t-login-brand__blob t-login-brand__blob--a" />
      <span class="t-login-brand__blob t-login-brand__blob--b" />
      <span class="t-login-brand__blob t-login-brand__blob--c" />
      <span class="t-login-brand__blob t-login-brand__blob--d" />
      <span class="t-login-brand__grain" />
      <span class="t-login-brand__sheen" />
      <span
        v-for="(spark, i) in sparks"
        :key="i"
        class="t-login-brand__spark"
        :style="spark"
      />
      <span
        v-for="(ring, i) in RINGS"
        :key="`ring-${i}`"
        class="t-login-brand__ring"
        :style="{
          width: `${ring.size}px`,
          height: `${ring.size}px`,
          left: ring.left,
          top: ring.top,
          borderColor: `rgba(255, 255, 255, ${ring.opacity})`,
          animationDuration: ring.duration,
          animationDelay: ring.delay,
        }"
      />
    </div>

    <header class="t-login-brand__head t-login-stagger">
      <span class="t-login-brand__logo">
        <TSvgIcon v-if="brandIcon" :icon="brandIcon" :size="24" />
        <template v-else>{{ brandInitial }}</template>
      </span>
      <span data-test="t-login-page-brand" class="t-login-brand__name">{{ brand }}</span>
    </header>

    <!-- four playful characters (cursor-tracking eyes, leaning bodies, blinking,
         and form-aware reactions: leaning in while typing, looking away from a
         visible password, sneaking the occasional peek) -->
    <LoginCharacters
      class="t-login-brand__chars"
      :typing="scene?.typing ?? false"
      :password-visible="scene?.passwordVisible ?? false"
      :password-length="scene?.passwordLength ?? 0"
    />

    <div v-if="tagline || taglineSub" class="t-login-brand__body t-login-stagger">
      <h1 v-if="tagline" class="t-login-brand__tagline">{{ tagline }}</h1>
      <p v-if="taglineSub" class="t-login-brand__sub">{{ taglineSub }}</p>
    </div>

    <p v-if="copyright" class="t-login-brand__copyright">{{ copyright }}</p>
  </div>
</template>

<style scoped>
.t-login-brand {
  position: relative;
  width: 100%;
  height: 100%;
  overflow: hidden;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  padding: 44px 48px;
  color: #fff;
  background: linear-gradient(
    160deg,
    var(--tnzi-primary-900, #312e81) 0%,
    var(--tnzi-primary-600, #4f46e5) 45%,
    var(--tnzi-primary-700, #6d28d9) 100%
  );
}

/* ---- aurora layers ---- */
.t-login-brand__aurora {
  position: absolute;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
}
.t-login-brand__blob {
  position: absolute;
  border-radius: 50%;
  mix-blend-mode: screen;
}
.t-login-brand__blob--a {
  width: 70%;
  height: 60%;
  left: -12%;
  top: -8%;
  background: radial-gradient(circle at 50% 50%, var(--tnzi-primary-400, #818cf8), transparent 68%);
  filter: blur(46px);
  animation: t-login-aur-a 18s ease-in-out infinite;
}
.t-login-brand__blob--b {
  width: 78%;
  height: 70%;
  right: -18%;
  bottom: -12%;
  background: radial-gradient(circle at 50% 50%, var(--tnzi-primary-300, #a855f7), transparent 66%);
  filter: blur(52px);
  opacity: 0.85;
  animation: t-login-aur-b 22s ease-in-out infinite;
}
.t-login-brand__blob--c {
  width: 60%;
  height: 58%;
  left: 24%;
  top: 30%;
  background: radial-gradient(circle at 50% 50%, var(--tnzi-primary-200, #38bdf8), transparent 64%);
  filter: blur(56px);
  opacity: 0.55;
  animation: t-login-aur-c 16s ease-in-out infinite;
}
.t-login-brand__blob--d {
  width: 52%;
  height: 52%;
  right: 6%;
  top: -6%;
  background: radial-gradient(circle at 50% 50%, var(--tnzi-primary-500, #4f46e5), transparent 60%);
  filter: blur(50px);
  animation: t-login-aur-a 18s ease-in-out infinite;
  animation-delay: -7s;
}
.t-login-brand__grain {
  position: absolute;
  inset: 0;
  opacity: 0.5;
  background-image: radial-gradient(rgba(255, 255, 255, 0.5) 0.5px, transparent 0.6px);
  background-size: 4px 4px;
  mix-blend-mode: overlay;
}
.t-login-brand__sheen {
  position: absolute;
  top: -20%;
  left: 0;
  width: 40%;
  height: 140%;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.22), transparent);
  filter: blur(8px);
  animation: t-login-sheen 9s ease-in-out infinite;
}
.t-login-brand__spark {
  position: absolute;
  border-radius: 50%;
  background: #fff;
  box-shadow: 0 0 6px rgba(255, 255, 255, 0.7);
  animation-name: t-login-rise;
  animation-timing-function: linear;
  animation-iteration-count: infinite;
}
.t-login-brand__ring {
  position: absolute;
  border-radius: 50%;
  border: 1.5px solid;
  animation: t-login-drift 13s ease-in-out infinite;
}

/* ---- foreground ---- */
.t-login-brand__chars {
  position: relative;
}
.t-login-brand__head {
  position: relative;
  display: flex;
  align-items: center;
  gap: 12px;
}
.t-login-brand__logo {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 42px;
  height: 42px;
  flex: none;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.16);
  font-size: 21px;
  font-weight: 800;
}
.t-login-brand__name {
  font-size: 20px;
  font-weight: 600;
  letter-spacing: -0.3px;
}
.t-login-brand__body {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-bottom: 16px;
}
/* Understated by design - the characters are the centerpiece; the copy is a
   quiet sign-off. Consumers replace it via login.tagline/taglineSub, or pass
   an empty string to hide it entirely. */
.t-login-brand__tagline {
  margin: 0;
  font-size: 24px;
  line-height: 1.35;
  font-weight: 600;
  letter-spacing: -0.4px;
  text-wrap: pretty;
}
.t-login-brand__sub {
  margin: 0;
  font-size: 13.5px;
  line-height: 1.6;
  color: rgba(255, 255, 255, 0.65);
}
.t-login-brand__copyright {
  position: relative;
  margin: 0;
  font-size: 12.5px;
  color: rgba(255, 255, 255, 0.55);
}

/* ---- strip mode (< 768px): compact, auto-height brand header ---- */
@media (max-width: 767px) {
  .t-login-brand {
    justify-content: flex-start;
    gap: 12px;
    padding: 16px 20px 18px;
  }
  .t-login-brand__head {
    /* Leave room for the shell's theme/lang toolbar in the top-right. */
    padding-right: 84px;
  }
  .t-login-brand__logo {
    width: 32px;
    height: 32px;
    border-radius: 9px;
    font-size: 16px;
  }
  .t-login-brand__name {
    font-size: 16px;
  }
  .t-login-brand__tagline {
    font-size: 17px;
    letter-spacing: -0.3px;
  }
  .t-login-brand__sub {
    font-size: 12px;
  }
  .t-login-brand__body {
    gap: 4px;
    padding-bottom: 0;
  }
  .t-login-brand__copyright,
  .t-login-brand__ring,
  .t-login-brand__chars {
    display: none;
  }
}

/* ---- keyframes ---- */
@keyframes t-login-aur-a {
  0%,
  100% {
    transform: translate(0, 0) scale(1);
  }
  33% {
    transform: translate(14%, 18%) scale(1.18);
  }
  66% {
    transform: translate(-10%, 8%) scale(0.92);
  }
}
@keyframes t-login-aur-b {
  0%,
  100% {
    transform: translate(0, 0) scale(1);
  }
  33% {
    transform: translate(-16%, -12%) scale(1.12);
  }
  66% {
    transform: translate(12%, -18%) scale(1.22);
  }
}
@keyframes t-login-aur-c {
  0%,
  100% {
    transform: translate(0, 0) scale(1.05);
  }
  50% {
    transform: translate(10%, -14%) scale(0.9);
  }
}
@keyframes t-login-sheen {
  0% {
    transform: translateX(-30%) rotate(8deg);
    opacity: 0;
  }
  50% {
    opacity: 0.5;
  }
  100% {
    transform: translateX(130%) rotate(8deg);
    opacity: 0;
  }
}
@keyframes t-login-rise {
  0% {
    transform: translateY(0);
    opacity: 0;
  }
  12% {
    opacity: 0.9;
  }
  88% {
    opacity: 0.9;
  }
  100% {
    transform: translateY(-340px);
    opacity: 0;
  }
}
@keyframes t-login-drift {
  0%,
  100% {
    transform: translate(0, 0) scale(1);
  }
  50% {
    transform: translate(26px, -20px) scale(1.07);
  }
}
@media (prefers-reduced-motion: reduce) {
  .t-login-brand__blob,
  .t-login-brand__sheen,
  .t-login-brand__spark,
  .t-login-brand__ring {
    animation: none;
  }
  .t-login-brand__spark {
    opacity: 0.7 !important;
  }
}
</style>
