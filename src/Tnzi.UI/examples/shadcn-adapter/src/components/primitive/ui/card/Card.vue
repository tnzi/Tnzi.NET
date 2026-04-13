<script setup lang="ts">
/**
 * Card — naive-ui style
 *
 * Border: 1px solid dividerColor
 * Border-radius: 3px
 * Shadow: boxShadow1 (3-layer)
 * Transition: .3s cubic-bezier(.4,0,.2,1) on color, bg, shadow, border
 */
import type { HTMLAttributes } from "vue"
import { cn } from "@/lib/utils"

const props = defineProps<{
  class?: HTMLAttributes["class"]
}>()
</script>

<template>
  <div
    data-slot="card"
    :class="
      cn(
        'bg-card text-card-foreground flex flex-col gap-6 rounded-md border py-6 shadow-sm transition-[color,background-color,box-shadow,border-color] duration-300 ease-[cubic-bezier(.4,0,.2,1)]',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
