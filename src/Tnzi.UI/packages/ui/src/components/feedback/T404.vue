<template>
  <TErrorPage code="404" :title="title" :description="description">
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
  title: 'Page Not Found',
  description: 'The page you are looking for does not exist or has been moved.',
})
</script>
