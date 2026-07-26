<script setup lang="ts">
/**
 * `TAvatar` - flexible identity avatar primitive.
 *
 * Renders, in priority order:
 *   1. the `src` image (cover-cropped), degrading on a load error to
 *   2. the `name` initial(s) on a deterministic colour, or
 *   3. a fallback `icon` (when `prefer-icon` is set, or there is no name).
 *
 * One component for every avatar scenario in the ecosystem - chat bubbles, the
 * admin header, user cards, AI persona / role glyphs - driven entirely by props
 * plus two slots:
 *   - `#badge` - an overlay anchored to the bottom-right corner (presence
 *                   dots, unread counters, edit pencils, …).
 *   - `#fallback` - fully custom non-image content (replaces the initial/icon).
 *
 * The component is stateless about *where* the image URL comes from: callers
 * resolve the URL (storage preview, external link, …) and pass `src`. It owns
 * only the presentation + the load-error → initial/icon degradation.
 */
import { computed, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { avatarColor, avatarInitial } from '../../utils/avatar'

type AvatarShape = 'circle' | 'rounded' | 'square'
type AvatarObjectFit = 'cover' | 'contain' | 'fill' | 'none' | 'scale-down'

const props = withDefaults(
  defineProps<{
    /** Resolved image URL. When present (and it loads) the picture is shown. */
    src?: string | null
    /** Display name - drives the initial fallback, the default colour seed and `alt`. */
    name?: string | null
    /** Explicit colour seed (e.g. a stable user id). Defaults to `name`. */
    seed?: string | null
    /** Pixel size (width = height). Default 40. */
    size?: number
    /** Outline shape. Default `circle`. `rounded` uses a soft corner radius. */
    shape?: AvatarShape
    /** Corner radius (px) for the `rounded` shape. Defaults to `size * 0.18`. */
    radius?: number
    /** Iconify icon used as the last-resort fallback (no image, no name). */
    icon?: string
    /** Number of initial characters (1 or 2). Default 1. */
    maxInitials?: 1 | 2
    /** Background colour override - skips the deterministic palette. Any CSS colour / `var()`. */
    color?: string
    /** Foreground (text / icon) colour override. Default white. */
    textColor?: string
    /** Prefer the `icon` over the name initial (for icon-only / role-glyph avatars). */
    preferIcon?: boolean
    /** `object-fit` for the image. Default `cover`. */
    objectFit?: AvatarObjectFit
    /** Alt text for the image. Defaults to `name`. */
    alt?: string
    /** Draw a subtle 1px ring (useful for white-on-white surfaces). */
    bordered?: boolean
    /**
     * Presence status - renders a colored dot in the bottom-right corner when `showStatus`
     * is set (and no `#badge` slot is provided). Accepts the `UserPresenceStatus` member
     * names (`'Online' | 'Away' | 'Busy' | 'Invisible' | 'Offline'`) or their lowercase form.
     * Lets any avatar (chat, header, user cards, member grids) show online status directly.
     */
    status?: string | null
    /** Show the presence dot for `status`. Default false. */
    showStatus?: boolean
    /** Presence dot diameter in px. Defaults to ~28% of `size` (min 8). */
    statusSize?: number
  }>(),
  {
    src: null,
    name: null,
    seed: null,
    size: 40,
    shape: 'circle',
    radius: undefined,
    icon: '',
    maxInitials: 1,
    color: undefined,
    textColor: undefined,
    preferIcon: false,
    objectFit: 'cover',
    alt: undefined,
    bordered: false,
    status: null,
    showStatus: false,
    statusSize: undefined,
  },
)

const slots = defineSlots<{
  badge?: () => unknown
  fallback?: () => unknown
}>()

// Presence dot: online/away/busy resolve to a colour; invisible = hollow ring;
// anything else (including offline / unknown) = grey. Explicit #badge always wins.
const statusKind = computed(() => {
  switch ((props.status ?? '').toLowerCase()) {
    case 'online': return 'online'
    case 'away': return 'away'
    case 'busy': return 'busy'
    case 'invisible': return 'invisible'
    default: return 'offline'
  }
})
const showStatusDot = computed(() => props.showStatus && props.status != null && !slots.badge)
const statusDotSize = computed(() => `${props.statusSize ?? Math.max(8, Math.round(props.size * 0.28))}px`)

// Track image load failures so a broken/expired URL degrades to the initial /
// icon instead of a broken-image glyph. Reset whenever the source changes
// (keyed lists reuse instances; a fresh upload deserves a fresh attempt).
const imgError = ref(false)
watch(
  () => props.src,
  () => {
    imgError.value = false
  },
)

const hasName = computed(() => !!(props.name && props.name.trim()))
const showImg = computed(() => !!props.src && !imgError.value)
const showIcon = computed(
  () => !showImg.value && !!props.icon && (props.preferIcon || !hasName.value),
)

const initial = computed(() => avatarInitial(props.name, props.maxInitials))

const px = computed(() => `${props.size}px`)

const borderRadius = computed(() => {
  if (props.shape === 'square') return '0'
  if (props.shape === 'rounded') return `${props.radius ?? Math.max(4, Math.round(props.size * 0.18))}px`
  return '50%'
})

const background = computed(() => {
  if (showImg.value) return props.color ?? 'var(--tnzi-container-bg, #fff)'
  if (props.color) return props.color
  return avatarColor(props.seed ?? props.name)
})

const initialFontSize = computed(() => {
  const ratio = initial.value.length >= 2 ? 0.36 : 0.42
  return `${Math.round(props.size * ratio)}px`
})
const iconSize = computed(() => Math.round(props.size * 0.56))

const boxStyle = computed(() => ({
  width: px.value,
  height: px.value,
  borderRadius: borderRadius.value,
  background: background.value,
  color: props.textColor ?? '#fff',
}))

const altText = computed(() => props.alt ?? props.name ?? '')
</script>

<template>
  <span class="t-avatar" :style="{ width: px, height: px }">
    <span class="t-avatar__box" :class="{ 't-avatar__box--bordered': bordered }" :style="boxStyle">
      <img
        v-if="showImg"
        :src="src ?? ''"
        :alt="altText"
        class="t-avatar__img"
        :style="{ objectFit }"
        @error="imgError = true"
      />
      <slot v-else name="fallback">
        <Icon
          v-if="showIcon"
          :icon="icon"
          :width="iconSize"
          :height="iconSize"
          class="t-avatar__icon"
        />
        <span v-else class="t-avatar__initial" :style="{ fontSize: initialFontSize }">{{ initial }}</span>
      </slot>
    </span>
    <span v-if="$slots.badge" class="t-avatar__badge"><slot name="badge" /></span>
    <span
      v-else-if="showStatusDot"
      class="t-avatar__status"
      :class="`t-avatar__status--${statusKind}`"
      :style="{ width: statusDotSize, height: statusDotSize }"
    />
  </span>
</template>

<style scoped>
.t-avatar {
  position: relative;
  display: inline-flex;
  flex-shrink: 0;
  vertical-align: middle;
}
.t-avatar__box {
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  user-select: none;
}
.t-avatar__box--bordered {
  box-shadow: 0 0 0 1px var(--tnzi-border, rgb(0 0 0 / 0.08));
}
.t-avatar__img {
  width: 100%;
  height: 100%;
  display: block;
}
.t-avatar__initial {
  font-weight: 600;
  line-height: 1;
  letter-spacing: 0.01em;
}
.t-avatar__icon {
  line-height: 0;
}
.t-avatar__badge {
  position: absolute;
  right: 0;
  bottom: 0;
  line-height: 0;
}
/* Built-in presence dot (bottom-right); an explicit #badge slot overrides it. The ring
   uses the container background so the dot reads on any surface. */
.t-avatar__status {
  position: absolute;
  right: 0;
  bottom: 0;
  box-sizing: border-box;
  border-radius: 50%;
  border: 2px solid var(--tnzi-container-bg, #fff);
}
.t-avatar__status--online { background: #1aad19; }
.t-avatar__status--away { background: #f5a623; }
.t-avatar__status--busy { background: #e64340; }
.t-avatar__status--offline { background: #bcbcbc; }
.t-avatar__status--invisible { background: transparent; border-color: #bcbcbc; }
</style>
