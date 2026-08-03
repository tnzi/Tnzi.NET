<script setup lang="ts">
/**
 * @experimental
 * TSettingGroup - a titled block of settings inside a pane.
 *
 * Pairs with `TSettingRow`. Together they cover the shape that every settings
 * pane repeats: a small section heading, a stack of label/description/control
 * rows, and a rule separating one section from the next.
 *
 * The separator is drawn on the group's *top* edge and suppressed for the
 * first group, so a pane can render N groups without the consumer tracking
 * which one is last.
 */
withDefaults(
  defineProps<{
    /** Section heading. Ignored when the `title` slot is used. */
    title?: string
    /** Draw the top separator. Set false to butt a group against the previous
     *  one (e.g. two groups that read as a single block). */
    separator?: boolean
  }>(),
  {
    title: '',
    separator: true,
  },
)
</script>

<template>
  <section class="t-setting-group" :class="{ 't-setting-group--no-separator': !separator }">
    <h3 v-if="title || $slots.title" class="t-setting-group__title">
      <slot name="title">{{ title }}</slot>
    </h3>
    <div class="t-setting-group__body">
      <slot />
    </div>
  </section>
</template>

<style scoped>
.t-setting-group {
  padding-top: 0;
  margin-top: 0;
  border-top: none;
}
/* Sibling selector, not `:first-child`: a pane almost always renders its own
   heading element before the first group, so the first group is the parent's
   SECOND child and `:first-child` never matched it. Measured result was two
   parallel rules 24px apart - the pane heading's border-bottom and the
   group's border-top. Keying off "follows another group" makes the rule
   independent of whatever else the pane puts above. */
.t-setting-group + .t-setting-group {
  padding-top: 24px;
  margin-top: 24px;
  border-top: 1px solid var(--tnzi-ai-divider);
}
.t-setting-group--no-separator {
  padding-top: 0;
  margin-top: 0;
  border-top: none;
}
.t-setting-group__title {
  margin: 0 0 4px;
  font-size: 15px;
  font-weight: 600;
  letter-spacing: -0.01em;
  color: var(--tnzi-ai-text);
}
.t-setting-group__body {
  display: flex;
  flex-direction: column;
}
</style>
