<script setup lang="ts">
import type { HTMLAttributes } from "vue"
import { useVModel } from "@vueuse/core"
import { cn } from "@/lib/utils"

const props = defineProps<{
  class?: HTMLAttributes["class"]
  defaultValue?: string | number
  modelValue?: string | number
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
  <textarea
    v-model="modelValue"
    data-slot="textarea"
    :class="cn('border-input placeholder:text-placeholder flex field-sizing-content min-h-16 w-full rounded-md border bg-transparent px-3 py-2 text-sm leading-[1.5] text-foreground caret-primary outline-none transition-[color,border-color,box-shadow,background-color] duration-300 ease-[cubic-bezier(.4,0,.2,1)] hover:border-primary-hover focus:border-primary-hover focus:shadow-[0_0_0_2px_hsl(var(--ring)/0.2)] disabled:cursor-not-allowed disabled:opacity-50 disabled:bg-muted/50 aria-invalid:border-destructive aria-invalid:caret-destructive aria-invalid:focus:shadow-[0_0_0_2px_hsl(var(--destructive)/0.2)]', props.class)"
  />
</template>
