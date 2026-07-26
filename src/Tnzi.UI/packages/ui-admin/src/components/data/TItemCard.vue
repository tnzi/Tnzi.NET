<template>
  <div
    class="t-item-card"
    :class="{
      't-item-card--clickable': clickable,
      't-item-card--selected': selected,
      't-item-card--muted': muted,
    }"
    :role="clickable ? 'button' : undefined"
    :tabindex="clickable ? 0 : undefined"
    :aria-pressed="clickable && selected ? 'true' : undefined"
    @click="onActivate"
    @keydown="onKeydown"
  >
    <!-- Selection checkbox: its own click must not open the card. -->
    <div v-if="selectable" class="t-item-card__check" @click.stop>
      <NCheckbox :checked="checked" @update:checked="(v: boolean) => emit('update:checked', v)" />
    </div>

    <slot name="leading">
      <div v-if="avatar || icon" class="t-item-card__glyph" :class="`t-item-card__glyph--${iconTone}`">
        <img v-if="avatar" :src="avatar" :alt="title" class="t-item-card__avatar" />
        <TSvgIcon v-else-if="icon" :icon="icon" :size="20" />
      </div>
    </slot>

    <div class="t-item-card__main">
      <div class="t-item-card__top">
        <slot name="title">
          <span class="t-item-card__title" :title="title">{{ title }}</span>
        </slot>
        <slot name="tags">
          <NTag
            v-for="(tag, i) in tags"
            :key="i"
            size="small"
            round
            :bordered="false"
            :type="tag.type ?? 'default'"
          >
            {{ tag.label }}
          </NTag>
        </slot>
      </div>

      <slot name="meta">
        <div v-if="subtitle || meta.length" class="t-item-card__meta">
          <span v-if="subtitle" class="t-item-card__desc" :title="subtitle">{{ subtitle }}</span>
          <span v-for="(m, i) in meta" :key="i" :title="m.title ?? m.text">
            <TSvgIcon v-if="m.icon" :icon="m.icon" :size="13" />{{ m.text }}
          </span>
        </div>
      </slot>

      <slot />
    </div>

    <slot name="trailing">
      <div v-if="amount !== undefined" class="t-item-card__amount">
        <span class="t-item-card__amount-value">{{ amount }}</span>
        <span v-if="amountLabel" class="t-item-card__amount-label">{{ amountLabel }}</span>
      </div>
    </slot>

    <div v-if="$slots.actions" class="t-item-card__ops" @click.stop @keydown.enter.stop>
      <slot name="actions" />
    </div>

    <TSvgIcon v-else-if="clickable" icon="mdi:chevron-right" :size="18" class="t-item-card__chev" />
  </div>
</template>

<script setup lang="ts">
/**
 * TItemCard - one record as a horizontal card instead of a table row.
 *
 * The shape a document list actually wants: a strong title with its status
 * chips beside it, a muted meta line underneath (date · party · reference), the
 * figure that matters right-aligned, and the row's operations at the end. A
 * table can only give every field the same weight in a fixed-width column; this
 * gives the record a hierarchy, and it survives narrow widths by refolding
 * instead of scrolling sideways.
 *
 * Use it for records a human reads as a DOCUMENT (invoices, payments, files,
 * templates, messages). Keep a table for records a human reads as a GRID -
 * dense numeric registers, ledgers, anything compared column-by-column.
 *
 * Every visual part is also a slot, so a page can keep the chrome (click
 * target, selection, hover, keyboard access, refold rules) and replace any
 * single region without re-implementing the card.
 *
 * The doc comment lives here rather than above the root element on purpose: a
 * comment node at the top of `<template>` makes the component multi-root, which
 * silently breaks class/attribute fallthrough onto the card.
 */
import { NCheckbox, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

export type ItemCardTone = 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error'

export interface ItemCardTag {
  label: string
  type?: 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error'
}

export interface ItemCardMeta {
  /** Iconify name rendered before the text. */
  icon?: string
  text: string
  /** `title` attribute when the text is truncated. */
  title?: string
}

interface Props {
  /** The one thing the reader scans for. */
  title: string
  /** Free description line; truncates with an ellipsis rather than wrapping. */
  subtitle?: string
  /** Iconify glyph in the leading tile. Ignored when `avatar` is set. */
  icon?: string
  /** Image URL for the leading tile (wins over `icon`). */
  avatar?: string
  /** Tint of the leading tile. */
  iconTone?: ItemCardTone
  /** Muted `icon · text` chips under the title. */
  meta?: ItemCardMeta[]
  /** Chips beside the title (status, kind, reference). */
  tags?: ItemCardTag[]
  /** Pre-formatted figure shown right-aligned (money, count, size). */
  amount?: string
  /** Small muted line under the figure (e.g. "balance due"). */
  amountLabel?: string
  /** Whole card opens the record: pointer, hover lift, Enter/Space, `click`. */
  clickable?: boolean
  /** Render a selection checkbox at the head of the card. */
  selectable?: boolean
  /** Checkbox state (`v-model:checked`). */
  checked?: boolean
  /** Selected styling (tinted background + primary border). */
  selected?: boolean
  /** Retired record (voided / disabled): dimmed, figure struck through. */
  muted?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  subtitle: undefined,
  icon: undefined,
  avatar: undefined,
  iconTone: 'default',
  meta: () => [],
  tags: () => [],
  amount: undefined,
  amountLabel: undefined,
  clickable: false,
  selectable: false,
  checked: false,
  selected: false,
  muted: false,
})

const emit = defineEmits<{
  click: [event: Event]
  'update:checked': [value: boolean]
}>()

defineSlots<{
  /** Extra content under the meta line (progress bars, inline previews). */
  default?: () => unknown
  /** Replaces the avatar/icon tile. */
  leading?: () => unknown
  /** Replaces the title text (keeps the tag row). */
  title?: () => unknown
  /** Replaces the tag row. */
  tags?: () => unknown
  /** Replaces the meta line. */
  meta?: () => unknown
  /** Replaces the right-aligned figure block. */
  trailing?: () => unknown
  /** Row operations; clicks are stopped from opening the card. */
  actions?: () => unknown
}>()

function onActivate(event: MouseEvent): void {
  if (!props.clickable) return
  emit('click', event)
}

function onKeydown(event: KeyboardEvent): void {
  if (!props.clickable) return
  if (event.key === 'Enter' || event.key === ' ') {
    // Space would otherwise scroll the page; both keys "activate" the card.
    event.preventDefault()
    emit('click', event)
  }
}
</script>

<style scoped>
.t-item-card {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 14px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg, #fff));
  transition: border-color 0.15s, box-shadow 0.15s;
}
.t-item-card--clickable {
  cursor: pointer;
}
.t-item-card--clickable:hover {
  border-color: var(--tnzi-primary);
  box-shadow: 0 2px 10px rgb(0 0 0 / 0.06);
}
.t-item-card--clickable:focus-visible {
  outline: 2px solid var(--tnzi-primary);
  outline-offset: 2px;
}
.t-item-card--selected {
  border-color: var(--tnzi-primary);
  background: rgb(var(--tnzi-primary-rgb) / 0.05);
}
/* A voided document stays listed and readable, but reads as retired rather
   than as money that moved. */
.t-item-card--muted {
  opacity: 0.62;
}
.t-item-card--muted .t-item-card__amount-value {
  text-decoration: line-through;
}

.t-item-card__check {
  flex-shrink: 0;
}
.t-item-card__glyph {
  width: 38px;
  height: 38px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  overflow: hidden;
  background: rgb(23 38 60 / 0.06);
  color: var(--tnzi-base-text-muted);
}
.t-item-card__glyph--primary { background: var(--tnzi-primary); color: #fff; }
.t-item-card__glyph--info { background: var(--tnzi-info); color: #fff; }
.t-item-card__glyph--success { background: var(--tnzi-success); color: #fff; }
.t-item-card__glyph--warning { background: var(--tnzi-warning); color: #fff; }
.t-item-card__glyph--error { background: var(--tnzi-error); color: #fff; }
.t-item-card__avatar {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.t-item-card__main {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 5px;
}
.t-item-card__top {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-item-card__title {
  font-size: 14.5px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-item-card__meta {
  display: flex;
  flex-wrap: nowrap;
  min-width: 0;
  gap: 4px 16px;
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted);
}
.t-item-card__meta > span {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  flex-shrink: 0;
}
/* `display: block`, not the meta inline-flex: text-overflow only ellipsises
   inline text - inside a flex container the text becomes an anonymous flex
   item and the overflow clips mid-word instead. */
.t-item-card__meta .t-item-card__desc {
  display: block;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex-shrink: 1 !important;
}

.t-item-card__amount {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 2px;
  flex: 0 0 auto;
  text-align: right;
}
.t-item-card__amount-value {
  font-size: 15px;
  font-weight: 700;
  color: var(--tnzi-base-text);
  font-variant-numeric: tabular-nums;
}
.t-item-card__amount-label {
  font-size: 11.5px;
  color: var(--tnzi-base-text-muted);
}

.t-item-card__ops {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}
.t-item-card__chev {
  flex-shrink: 0;
  color: var(--tnzi-base-text-muted);
}

/* Phones: the one-row card refolds into fixed bands so every card reads the
   same regardless of how long the title or tag row is:
     [✓] title (ellipsised)
         tags
         meta
     figure ················ actions / chevron                              */
@media (max-width: 660px) {
  .t-item-card {
    flex-wrap: wrap;
    align-items: flex-start;
    row-gap: 8px;
  }
  .t-item-card__check {
    margin-top: 3px;
  }
  /* Row 1 = checkbox/glyph + text block only; the basis claims the row minus
     the leading column so the figure and actions are pushed to their own row
     instead of crushing the title. */
  .t-item-card__main {
    flex: 1 1 calc(100% - 56px);
  }
  /* The title always owns a full line and the tags drop below it - with tags
     inline the fold point depended on title length, so no two cards folded
     the same way. */
  .t-item-card__top {
    flex-wrap: wrap;
  }
  .t-item-card__title {
    flex-basis: 100%;
  }
  .t-item-card__amount {
    align-items: flex-start;
    text-align: left;
  }
  .t-item-card__ops {
    margin-left: auto;
  }
  .t-item-card__chev {
    margin-left: auto;
  }
}
</style>
