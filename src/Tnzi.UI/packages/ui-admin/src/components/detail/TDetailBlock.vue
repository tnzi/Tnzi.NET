<template>
  <div class="t-detail-block">
    <div class="t-detail-block__head" :class="{ 't-detail-block__head--bare': !inlineHint }">
      <div class="t-detail-block__title">
        <TSvgIcon v-if="icon" :icon="icon" :size="16" />
        <span>{{ title }}</span>
        <!-- `hintMode: 'popover'`: the hint behind a ⓘ beside the title, as in TDetailSection. -->
        <THint v-if="popoverHint" type="info" :size="15" :content="popoverHint" placement="bottom-start" />
        <slot name="titleExtra" />
      </div>
      <div v-if="$slots.actions" class="t-detail-block__actions">
        <slot name="actions" />
      </div>
    </div>
    <p v-if="inlineHint" class="t-detail-block__hint">{{ inlineHint }}</p>
    <slot />
  </div>
</template>

<script setup lang="ts">
/**
 * TDetailBlock - one titled block INSIDE a section: an icon and a bold title
 * with room for a status chip beside it, the block's own actions on the right,
 * a muted hint line, then the body.
 *
 * A section (`TDetailSection`, the User Center's section shell) answers one
 * navigation entry; a block is one topic within it - the second factor, the
 * sign-in allow-list, the password, the passkeys - each with its own state and
 * its own buttons. Sections used to draw this header three different ways
 * (bold + icon, uppercase muted label with a help popover, scoped per file),
 * so the same page read as three designs. Owning the markup and the CSS here
 * gives every block the same chrome for the same reason `TDetailSection`
 * exists: scoped styles do not cross a component boundary, so shared class
 * names alone leave a sibling component unstyled.
 *
 * Spacing BETWEEN blocks is the host's: a section decides whether its blocks
 * are separated by a rule, a divider or nothing.
 *
 * (Doc comment in the script, not above the root element: a leading comment
 * node in `<template>` makes the component multi-root and breaks fallthrough.)
 */
import { computed } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import THint from '../display/THint.vue'
import type { TDetailSectionHintMode } from './TDetailSection.vue'

/** Where a block's `hint` goes; the same two placements as a section's. */
export type TDetailBlockHintMode = TDetailSectionHintMode

interface Props {
  title: string
  /** Iconify glyph before the title. */
  icon?: string
  /** One or two sentences about the block. Under the title by default. */
  hint?: string
  /**
   * `'inline'` (default): a muted line under the title, read for free; right
   * for one short sentence. `'popover'`: a ⓘ beside the title with the text on
   * hover / focus; right for copy that would wrap into a paragraph and outweigh
   * the block it explains.
   */
  hintMode?: TDetailBlockHintMode
}

const props = withDefaults(defineProps<Props>(), {
  icon: undefined,
  hint: undefined,
  hintMode: 'inline',
})

// One hint, two mutually exclusive placements, as in TDetailSection.
const inlineHint = computed(() => (props.hintMode === 'inline' ? props.hint : undefined))
const popoverHint = computed(() => (props.hintMode === 'popover' ? props.hint : undefined))

defineOptions({ name: 'TDetailBlock' })

defineSlots<{
  /** The block body. */
  default?: () => unknown
  /** Beside the title: a status chip, a counter. */
  titleExtra?: () => unknown
  /** The block's own operations, right-aligned; wraps under the title on narrow widths. */
  actions?: () => unknown
}>()
</script>

<style scoped>
.t-detail-block__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.t-detail-block__title {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-detail-block__actions {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.t-detail-block__hint {
  margin: 6px 0 14px;
  font-size: 12.5px;
  line-height: 1.55;
  color: var(--tnzi-base-text-muted);
}
/* No hint: the body still needs the same breathing room under the title. */
.t-detail-block__head--bare {
  margin-bottom: 14px;
}
</style>
