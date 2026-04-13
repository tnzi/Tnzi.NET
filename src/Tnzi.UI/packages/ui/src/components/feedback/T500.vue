<template>
  <TErrorPage code="500" :title="title" :description="description">
    <template v-if="$slots.action" #action>
      <slot name="action" />
    </template>
  </TErrorPage>
</template>

<script setup lang="ts">
import TErrorPage from './TErrorPage.vue'

interface Props {
  title?: string
  description?: string
}

withDefaults(defineProps<Props>(), {
  title: 'Server Error',
  description: 'Something went wrong on our end. Please try again later.',
})
</script>
