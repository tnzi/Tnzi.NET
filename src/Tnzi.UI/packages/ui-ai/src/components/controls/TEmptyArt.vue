<script setup lang="ts">
/**
 * TEmptyArt - the drawings an empty page leads with: one per kind of thing a
 * page holds, all in one hand. Each is a loose line drawing in the text colour
 * on a 96-unit square, its shapes filled with a soft tone set a little off the
 * lines (down and to the left, as if printed slightly out of register), so any
 * two pages' empty states read as one set. The fill follows the drawing's own
 * outline, never a fixed backdrop.
 *
 * A page whose kind of thing has no drawing here puts its own in
 * `TEmptyState`'s `#art` slot, drawn the same way: `.t-empty-art` on the
 * `<svg>` (96px, the text colour, 2.6 round strokes) and `.t-empty-art__fill`
 * on the shapes that take the tone.
 */
import type { EmptyArt } from './empty-art'

defineProps<{ name: EmptyArt }>()
</script>

<template>
  <svg class="t-empty-art" viewBox="0 0 96 96" fill="none" aria-hidden="true" :data-empty-art="name">
    <!-- A stopwatch: a crown on top, a face and two hands. -->
    <template v-if="name === 'stopwatch'">
      <circle class="t-empty-art__fill" cx="47" cy="56" r="33" />
      <path d="M40 9.5c5.2-.6 10.6-.5 16 .2" />
      <path d="M48 10v9" />
      <path d="M48.4 21c17.6.3 30.8 14.6 30.4 32.4-.4 17.9-14.9 31.5-32.2 31.1C29 84.2 15.9 70 16.3 52.5 16.8 35 30.6 20.7 48.4 21z" />
      <path d="M73.5 25.5l4.6-4.4" />
      <path d="M47.6 53.2V35.5" />
      <path d="M47.6 53.2l11.2 6.4" />
    </template>

    <!-- A clock face turning back: past runs. -->
    <template v-else-if="name === 'history'">
      <circle class="t-empty-art__fill" cx="49" cy="54" r="27" />
      <path d="M24.5 38.5C30 27.6 41.3 21.2 53.2 22.4c15.1 1.5 26.4 14.6 25.4 30-1 15.6-14.4 27.2-29.8 26.5-13.7-.6-24.8-11-26.5-24.3" />
      <path d="M23.6 27.4l.9 11.1 11-1.6" />
      <path d="M50 36.5v15.8l9.8 6.2" />
    </template>

    <!-- Three blocks set together and a fourth being put in place: apps made for you. -->
    <template v-else-if="name === 'blocks'">
      <g class="t-empty-art__fill">
        <path d="M21.2 37c6.2-.3 12.4-.2 18.6.2.2 6.2.3 12.4.2 18.6 6.6-.2 13.2-.1 19.8.2.2 6.6.1 13.1-.3 19.6-12.9.4-25.8.4-38.6-.1-.4-12.8-.3-25.6.3-38.5z" />
        <path d="M60.9 19.8l19.4 5.1-5.2 19.4-19.3-5.2z" />
      </g>
      <path d="M21.2 37c6.2-.3 12.4-.2 18.6.2.2 6.2.3 12.4.2 18.6 6.6-.2 13.2-.1 19.8.2.2 6.6.1 13.1-.3 19.6-12.9.4-25.8.4-38.6-.1-.4-12.8-.3-25.6.3-38.5z" />
      <path d="M21 56.4c6.4-.3 12.8-.3 19 0" />
      <path d="M40.2 56.2c.3 6.5.3 13 0 19.4" />
      <path d="M60.9 19.8l19.4 5.1-5.2 19.4-19.3-5.2z" />
    </template>

    <!-- Three tiles and room for a fourth: installed apps. -->
    <template v-else-if="name === 'apps'">
      <g class="t-empty-art__fill">
        <rect x="21" y="27" width="23" height="23" rx="5" />
        <rect x="51" y="28" width="22" height="22" rx="5" />
        <rect x="21.5" y="57" width="22" height="22" rx="5" />
      </g>
      <rect x="21" y="27" width="23" height="23" rx="5" />
      <rect x="51" y="28" width="22" height="22" rx="5" />
      <rect x="21.5" y="57" width="22" height="22" rx="5" />
      <path d="M62 59.5v17" />
      <path d="M53.5 68h17" />
    </template>

    <!-- A shop front under an awning: the app store. -->
    <template v-else-if="name === 'store'">
      <g class="t-empty-art__fill">
        <path d="M18 38l6.2-15.6c16-.6 32-.6 47.8 0L78 38c0 5-3.6 8-7.5 8h-45C21.6 46 18 43 18 38z" />
        <path d="M23 46v30.3c16.6.5 33.4.5 50 0V46z" />
      </g>
      <path d="M18 38l6.2-15.6c16-.6 32-.6 47.8 0L78 38" />
      <path d="M18 38c0 5 3.6 8 7.5 8S33 43 33 38c0 5 3.6 8 7.5 8S48 43 48 38c0 5 3.6 8 7.5 8S63 43 63 38c0 5 3.6 8 7.5 8S78 43 78 38" />
      <path d="M23 47.5v28.8c16.6.5 33.4.5 50 0V47.5" />
      <path d="M41 76V61.2c0-1.5 1.2-2.7 2.7-2.7h8.6c1.5 0 2.7 1.2 2.7 2.7V76" />
    </template>

    <!-- A folder with a branch in it: a Code project. -->
    <template v-else-if="name === 'project'">
      <path class="t-empty-art__fill" d="M16 30.5c0-2.5 2-4.5 4.5-4.5h15.2c1.6 0 3 .8 3.8 2.1l3.3 5.3c.8 1.3 2.3 2.1 3.8 2.1h27.9c2.5 0 4.5 2 4.5 4.5v31.5c0 2.5-2 4.5-4.5 4.5H20.5c-2.5 0-4.5-2-4.5-4.5z" />
      <path d="M16 30.5c0-2.5 2-4.5 4.5-4.5h15.2c1.6 0 3 .8 3.8 2.1l3.3 5.3c.8 1.3 2.3 2.1 3.8 2.1h27.9c2.5 0 4.5 2 4.5 4.5v31.5c0 2.5-2 4.5-4.5 4.5H20.5c-2.5 0-4.5-2-4.5-4.5z" />
      <circle cx="38" cy="46.5" r="4.5" />
      <circle cx="58" cy="51.5" r="4.5" />
      <path d="M38 51v17.5" />
      <path d="M58 56c-.2 6.4-4.6 10-11.4 10.6l-8.6.6" />
    </template>

    <!-- A screen on its stand: a device. -->
    <template v-else-if="name === 'device'">
      <path class="t-empty-art__fill" d="M17.5 26.5c0-2.2 1.8-4 4-4h53c2.2 0 4 1.8 4 4v34.3c0 2.2-1.8 4-4 4h-53c-2.2 0-4-1.8-4-4z" />
      <path d="M17.5 26.5c0-2.2 1.8-4 4-4h53c2.2 0 4 1.8 4 4v34.3c0 2.2-1.8 4-4 4h-53c-2.2 0-4-1.8-4-4z" />
      <path d="M63 30.5l8.5 8.5" />
      <path d="M48 65v11.5" />
      <path d="M35 77.5c8.6-.6 17.3-.6 26 0" />
    </template>

    <!-- An open folder: files. -->
    <template v-else-if="name === 'folder'">
      <g class="t-empty-art__fill">
        <path d="M18 70.8V27.5c0-2.2 1.8-4 4-4h14.5c1.4 0 2.7.7 3.4 1.9l3.2 5.2c.7 1.2 2 1.9 3.4 1.9H70c2.2 0 4 1.8 4 4v5L66 70.8z" />
        <path d="M18 70.8l9.4-27c.6-1.6 2.1-2.6 3.8-2.6h47.6c2.8 0 4.7 2.8 3.7 5.4l-8.9 21.8c-.6 1.5-2 2.4-3.6 2.4z" />
      </g>
      <path d="M18 70.8V27.5c0-2.2 1.8-4 4-4h14.5c1.4 0 2.7.7 3.4 1.9l3.2 5.2c.7 1.2 2 1.9 3.4 1.9H70c2.2 0 4 1.8 4 4v5" />
      <path d="M18 70.8l9.4-27c.6-1.6 2.1-2.6 3.8-2.6h47.6c2.8 0 4.7 2.8 3.7 5.4l-8.9 21.8c-.6 1.5-2 2.4-3.6 2.4H18" />
    </template>

    <!-- A bin with its lid on: the recycle bin. -->
    <template v-else-if="name === 'trash'">
      <path class="t-empty-art__fill" d="M25 31l3.6 42.4c.2 2.3 2.1 4 4.4 4h30c2.3 0 4.2-1.7 4.4-4L71 31z" />
      <path d="M20 30.5c18.6-1.2 37.4-1.2 56 0" />
      <path d="M38 30v-5.5c0-1.6 1.3-2.9 2.9-2.9h14.2c1.6 0 2.9 1.3 2.9 2.9V30" />
      <path d="M25 31l3.6 42.4c.2 2.3 2.1 4 4.4 4h30c2.3 0 4.2-1.7 4.4-4L71 31" />
      <path d="M41 42v24" />
      <path d="M55 42v24" />
    </template>

    <!-- Books on a shelf, one leaning: the library. -->
    <template v-else-if="name === 'library'">
      <g class="t-empty-art__fill">
        <rect x="20" y="24" width="14" height="52" rx="2.5" />
        <rect x="38" y="30" width="12" height="46" rx="2.5" />
        <path d="M55.5 33.2l11.4-3.6 13.5 43.4-11.4 3.6z" />
      </g>
      <rect x="20" y="24" width="14" height="52" rx="2.5" />
      <path d="M20 34h14" />
      <path d="M20 66h14" />
      <rect x="38" y="30" width="12" height="46" rx="2.5" />
      <path d="M55.5 33.2l11.4-3.6 13.5 43.4-11.4 3.6z" />
      <path d="M14 77.5c22.6-.8 45.4-.8 68 0" />
    </template>

    <!-- A sticky note with its corner turned up. -->
    <template v-else-if="name === 'note'">
      <path class="t-empty-art__fill" transform="rotate(-4 48 48)" d="M22 24.5c0-1.4 1.1-2.5 2.5-2.5h47c1.4 0 2.5 1.1 2.5 2.5V56L56 74H24.5c-1.4 0-2.5-1.1-2.5-2.5z" />
      <g transform="rotate(-4 48 48)">
        <path d="M22 24.5c0-1.4 1.1-2.5 2.5-2.5h47c1.4 0 2.5 1.1 2.5 2.5V56L56 74H24.5c-1.4 0-2.5-1.1-2.5-2.5z" />
        <path d="M74 56H59.5c-2 0-3.5 1.5-3.5 3.5V74" />
        <path d="M32 36h28" />
        <path d="M32 46h18" />
      </g>
    </template>

    <!-- A bell, ringing: reminders. -->
    <template v-else-if="name === 'bell'">
      <path class="t-empty-art__fill" d="M48 20c-12 0-20.5 9.3-20.5 21.5v12.3c0 3.5-1.4 6.9-3.8 9.4L21 66c-.9.9-.2 2.5 1.1 2.5h51.8c1.3 0 2-1.6 1.1-2.5l-2.7-2.8c-2.4-2.5-3.8-5.9-3.8-9.4V41.5C68.5 29.3 60 20 48 20z" />
      <path d="M48 14v6" />
      <path d="M48 20c-12 0-20.5 9.3-20.5 21.5v12.3c0 3.5-1.4 6.9-3.8 9.4L21 66c-.9.9-.2 2.5 1.1 2.5h51.8c1.3 0 2-1.6 1.1-2.5l-2.7-2.8c-2.4-2.5-3.8-5.9-3.8-9.4V41.5C68.5 29.3 60 20 48 20z" />
      <path d="M41.5 74.5c1.3 2.9 3.9 4.6 6.5 4.6s5.2-1.7 6.5-4.6" />
      <path d="M76.5 26.5c3 3 4.8 7 5.3 11.2" />
      <path d="M19.5 26.5c-3 3-4.8 7-5.3 11.2" />
    </template>

    <!-- An envelope, its flap folded down, with a letter peeking out: mail. -->
    <template v-else-if="name === 'mail'">
      <path class="t-empty-art__fill" d="M15.6 40.3c21.8-.6 43.6-.6 65.4.1.6 11.9.5 23.8-.2 35.7-21.6.6-43.2.6-64.8-.1-.8-11.9-.9-23.8-.4-35.7z" />
      <path d="M27.5 40.2V20.6c13.8-.5 27.6-.4 41.4.2v19.6" />
      <path d="M36 29.5c8.2-.3 16.4-.3 24.6.1" />
      <path d="M36 36c5.4-.2 10.8-.2 16.2 0" />
      <path d="M15.6 40.3c21.8-.6 43.6-.6 65.4.1.6 11.9.5 23.8-.2 35.7-21.6.6-43.2.6-64.8-.1-.8-11.9-.9-23.8-.4-35.7z" />
      <path d="M16 41c10.6 7.6 21.3 14.6 32.3 21.1 10.9-6.6 21.7-13.6 32.4-21" />
      <path d="M16.6 75.4c8.9-7.2 18-13.9 27.4-20.2" />
      <path d="M80.3 75.6c-8.8-7.3-17.8-14-27.2-20.4" />
    </template>
  </svg>
</template>
