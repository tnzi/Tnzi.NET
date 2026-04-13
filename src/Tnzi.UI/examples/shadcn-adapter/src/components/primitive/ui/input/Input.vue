<script setup lang="ts">
/**
 * Input — naive-ui style
 *
 * Height: 34px (medium), border: 1px solid rgb(224,224,230)
 * Hover: border → primaryHover
 * Focus: border → primaryHover + box-shadow 0 0 0 2px rgba(primary, 0.2)
 * Caret: primary color
 * Transition: .3s cubic-bezier(.4,0,.2,1)
 * Disabled: opacity 50%, bg actionColor
 */
import type { HTMLAttributes } from "vue"
import { useVModel } from "@vueuse/core"
import { cn } from "@/lib/utils"

const props = defineProps<{
  defaultValue?: string | number
  modelValue?: string | number
  class?: HTMLAttributes["class"]
}>()

const emits = defineEmits<{
  (e: "update:modelValue", payload: string | number): void
}>()

const modelValue = useVModel(props, "modelValue", emits, {
  passive: true,
  defaultValue: props.defaultValue,
})
</script>

<template>
  <input
    v-model="modelValue"
    data-slot="input"
    :class="cn(
      'h-[34px] w-full min-w-0 rounded-md border border-input bg-transparent px-[12px] py-0 text-sm leading-[1.5] text-foreground caret-primary outline-none transition-[color,border-color,box-shadow,background-color,caret-color] duration-300 ease-[cubic-bezier(.4,0,.2,1)]',
      'placeholder:text-[hsl(var(--placeholder))]',
      'file:text-foreground file:inline-flex file:h-7 file:border-0 file:bg-transparent file:text-sm file:font-medium',
      'selection:bg-primary selection:text-primary-foreground',
      'hover:border-primary-hover',
      'focus:border-primary-hover focus:shadow-[0_0_0_2px_hsl(var(--ring)/0.2)]',
      'disabled:cursor-not-allowed disabled:opacity-50 disabled:bg-muted/50',
      'aria-invalid:border-destructive aria-invalid:caret-destructive aria-invalid:focus:shadow-[0_0_0_2px_hsl(var(--destructive)/0.2)]',
      props.class,
    )"
  >
</template>
