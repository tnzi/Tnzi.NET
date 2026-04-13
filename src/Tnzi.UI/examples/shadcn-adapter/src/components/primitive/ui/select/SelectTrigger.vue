<script setup lang="ts">
import type { SelectTriggerProps } from "reka-ui"
import type { HTMLAttributes } from "vue"
import { reactiveOmit } from "@vueuse/core"
import { ChevronDown } from "lucide-vue-next"
import { SelectIcon, SelectTrigger, useForwardProps } from "reka-ui"
import { cn } from "@/lib/utils"

const props = withDefaults(
  defineProps<SelectTriggerProps & { class?: HTMLAttributes["class"], size?: "sm" | "default" }>(),
  { size: "default" },
)

const delegatedProps = reactiveOmit(props, "class", "size")
const forwardedProps = useForwardProps(delegatedProps)
</script>

<template>
  <SelectTrigger
    data-slot="select-trigger"
    :data-size="size"
    v-bind="forwardedProps"
    :class="cn(
      'flex w-full items-center justify-between border border-input bg-transparent rounded-md px-3 text-sm text-foreground data-[placeholder]:text-placeholder [&_svg:not([class*=\'text-\'])]:text-muted-foreground transition-[color,border-color,box-shadow] duration-300 ease-[cubic-bezier(.4,0,.2,1)] outline-none hover:border-primary-hover focus-visible:border-primary-hover focus-visible:shadow-[0_0_0_2px_hsl(var(--ring)/0.2)] disabled:cursor-not-allowed disabled:opacity-50 disabled:bg-muted/50 data-[size=default]:h-[34px] data-[size=sm]:h-[28px] *:data-[slot=select-value]:flex *:data-[slot=select-value]:flex-1 *:data-[slot=select-value]:min-w-0 *:data-[slot=select-value]:items-center *:data-[slot=select-value]:gap-2 *:data-[slot=select-value]:truncate [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*=\'size-\'])]:size-4',
      props.class,
    )"
  >
    <slot />
    <SelectIcon as-child>
      <ChevronDown class="size-4 opacity-50" />
    </SelectIcon>
  </SelectTrigger>
</template>
