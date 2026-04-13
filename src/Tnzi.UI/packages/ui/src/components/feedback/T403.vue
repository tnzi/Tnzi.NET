<template>
  <TErrorPage code="403" :title="title" :description="description">
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
  title: 'Forbidden',
  description: 'You do not have permission to access this resource.',
})
</script>
