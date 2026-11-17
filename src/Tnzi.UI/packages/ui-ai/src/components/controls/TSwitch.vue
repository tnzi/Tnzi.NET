<script setup lang="ts">
/**
 * TSwitch - an on/off switch (`role="switch"`, 38x22).
 *
 * It is a native button, so Space and Enter toggle it as they press any
 * button, and a `<label>` wrapping it or pointing at it toggles it too. While
 * `loading` the change is in flight: it shows the state being saved and takes
 * no clicks. Off, the track is the strong border colour with a white knob
 * (`--tnzi-ai-switch-knob`); on, the accent with what reads on the accent.
 */
withDefaults(
  defineProps<{
    modelValue: boolean
    /** Names the switch for screen readers when no visible label is attached to it. */
    label?: string
    disabled?: boolean
    loading?: boolean
  }>(),
  { label: '', disabled: false, loading: false },
)

const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()
</script>

<template>
  <button
    type="button"
    role="switch"
    class="t-switch"
    :class="{ 'is-on': modelValue, 'is-loading': loading }"
    :aria-checked="modelValue"
    :aria-label="label || undefined"
    :aria-busy="loading || undefined"
    :disabled="disabled || loading"
    @click="emit('update:modelValue', !modelValue)"
  >
    <span class="t-switch__knob" />
  </button>
</template>

<style scoped>
.t-switch {
  /* Its own text metrics, not the page reset's (which differ: 1.15 and the
     system font in most resets, `inherit` under a utility preflight). */
  font-family: inherit;
  font-size: 100%;
  line-height: 1.5;
  position: relative;
  flex-shrink: 0;
  width: 38px;
  height: 22px;
  padding: 0;
  border: none;
  border-radius: var(--tnzi-ai-radius-pill, 999px);
  background: var(--tnzi-ai-border-strong);
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-switch.is-on {
  background: var(--tnzi-ai-accent);
}
.t-switch__knob {
  position: absolute;
  top: 3px;
  left: 3px;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: var(--tnzi-ai-switch-knob);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
  transition:
    transform var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-switch.is-on .t-switch__knob {
  transform: translateX(16px);
  /* What goes on the accent: ink on a light one, white on a dark one. */
  background: var(--tnzi-ai-on-accent);
}
.t-switch:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-switch:disabled {
  opacity: 0.5;
  cursor: default;
}
.t-switch.is-loading {
  opacity: 0.7;
  cursor: progress;
}
@media (prefers-reduced-motion: reduce) {
  .t-switch,
  .t-switch__knob {
    transition: none;
  }
}
</style>
