<script setup lang="ts">
/**
 * @experimental
 * TResourceCard - one entry in a resource grid (an agent, a skill, a saved
 * artifact, a scheduled task).
 *
 * Icon or thumbnail, name, one line of description, an optional status tag and
 * an overflow menu slot. Pair with the `.t-resource-grid` class exported from
 * the same barrel for the responsive column behaviour.
 */
import { Icon } from '@iconify/vue'

withDefaults(
  defineProps<{
    title: string
    description?: string
    /** Iconify name shown in the leading chip. Ignored when `thumbnail` is set. */
    icon?: string
    /** Image URL for a preview-style card (saved artifacts, generated pages). */
    thumbnail?: string
    /** Short status tag rendered next to the title. */
    tag?: string
    /** Renders the card as pressed/selected. */
    active?: boolean
    /** Makes the whole card a button. Leave false for cards whose only
     *  affordances are in the actions slot. */
    clickable?: boolean
  }>(),
  {
    description: '',
    icon: '',
    thumbnail: '',
    tag: '',
    active: false,
    clickable: true,
  },
)

const emit = defineEmits<{
  select: []
}>()
</script>

<template>
  <component
    :is="clickable ? 'button' : 'div'"
    :type="clickable ? 'button' : undefined"
    class="t-resource-card"
    :class="{ 'is-active': active, 'is-clickable': clickable }"
    @click="clickable && emit('select')"
  >
    <img v-if="thumbnail" :src="thumbnail" :alt="title" class="t-resource-card__thumb" />

    <div class="t-resource-card__head">
      <span v-if="icon && !thumbnail" class="t-resource-card__icon" aria-hidden="true">
        <Icon :icon="icon" />
      </span>
      <span class="t-resource-card__title">{{ title }}</span>
      <span v-if="tag" class="t-resource-card__tag">{{ tag }}</span>
      <span v-if="$slots.actions" class="t-resource-card__actions" @click.stop>
        <slot name="actions" />
      </span>
    </div>

    <p v-if="description" class="t-resource-card__desc">{{ description }}</p>

    <slot />
  </component>
</template>

<style scoped>
.t-resource-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 16px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  border-radius: 14px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 14px;
  text-align: left;
  width: 100%;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              box-shadow var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-resource-card.is-clickable { cursor: pointer; }
.t-resource-card.is-clickable:hover {
  border-color: var(--tnzi-ai-border-strong);
  box-shadow: 0 2px 10px rgba(0, 0, 0, 0.04);
}
.t-resource-card.is-active {
  border-color: var(--tnzi-ai-accent);
  background: var(--tnzi-ai-accent-soft);
}
.t-resource-card__thumb {
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  border-radius: 10px;
  background: var(--tnzi-ai-hover);
}
.t-resource-card__head {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}
.t-resource-card__icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border-radius: 8px;
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text-secondary);
  font-size: 16px;
  flex-shrink: 0;
}
.t-resource-card__title {
  flex: 1;
  min-width: 0;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-resource-card__tag {
  flex-shrink: 0;
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text-secondary);
}
.t-resource-card__actions {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 4px;
}
.t-resource-card__desc {
  margin: 0;
  font-size: 13px;
  line-height: 1.55;
  color: var(--tnzi-ai-text-secondary);
  /* Two lines then ellipsis: cards in a grid must stay the same height or the
     grid turns ragged as soon as one description runs long. */
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
