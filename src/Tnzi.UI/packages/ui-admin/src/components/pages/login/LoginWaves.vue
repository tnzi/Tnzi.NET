<script setup lang="ts">
/**
 * `LoginWaves` — animated dual-layer bottom waves for the `wave` login layout
 * (2026-06-11 redesign, 方案 A). Two seamless SVG strips scroll horizontally
 * at different speeds so the waves appear to drift. Each strip is 200% wide
 * (the same path repeated twice) translating from 0 to -50% in a loop.
 *
 * Purely decorative — `aria-hidden` + `pointer-events: none`; animation is
 * disabled under `prefers-reduced-motion`.
 */
interface Props {
  /** Wave fill color — typically the theme primary (or a dark palette step). */
  color: string
  /** Overall opacity of the wave block (dark mode dims it). */
  opacity?: number
}

withDefaults(defineProps<Props>(), { opacity: 1 })

const PATH_SLOW =
  'M0,64 C160,16 320,16 480,56 C640,96 800,104 960,72 C1120,40 1280,32 1440,64 L1440,160 L0,160 Z'
const PATH_FAST =
  'M0,96 C180,56 360,48 540,80 C720,112 900,120 1080,92 C1260,64 1380,60 1440,80 L1440,160 L0,160 Z'
</script>

<template>
  <div class="t-login-waves" :style="{ opacity }" aria-hidden="true">
    <div class="t-login-waves__layer t-login-waves__layer--slow">
      <svg
        v-for="i in 2"
        :key="i"
        class="t-login-waves__svg t-login-waves__svg--slow"
        :style="{ left: `${(i - 1) * 50}%` }"
        viewBox="0 0 1440 160"
        preserveAspectRatio="none"
      >
        <path :d="PATH_SLOW" :fill="color" fill-opacity="0.35" />
      </svg>
    </div>
    <div class="t-login-waves__layer t-login-waves__layer--fast">
      <svg
        v-for="i in 2"
        :key="i"
        class="t-login-waves__svg t-login-waves__svg--fast"
        :style="{ left: `${(i - 1) * 50}%` }"
        viewBox="0 0 1440 160"
        preserveAspectRatio="none"
      >
        <path :d="PATH_FAST" :fill="color" fill-opacity="0.55" />
      </svg>
    </div>
  </div>
</template>

<style scoped>
.t-login-waves {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  height: 190px;
  overflow: hidden;
  pointer-events: none;
}
.t-login-waves__layer {
  position: absolute;
  bottom: 0;
  width: 200%;
  height: 100%;
}
.t-login-waves__layer--slow {
  animation: t-login-wave-scroll 19s linear infinite;
}
.t-login-waves__layer--fast {
  animation: t-login-wave-scroll 12s linear infinite;
}
.t-login-waves__svg {
  position: absolute;
  bottom: 0;
  width: 50%;
}
.t-login-waves__svg--slow {
  height: 78%;
}
.t-login-waves__svg--fast {
  height: 60%;
}
@keyframes t-login-wave-scroll {
  from {
    transform: translateX(0);
  }
  to {
    transform: translateX(-50%);
  }
}
@media (prefers-reduced-motion: reduce) {
  .t-login-waves__layer {
    animation: none;
  }
}
</style>
