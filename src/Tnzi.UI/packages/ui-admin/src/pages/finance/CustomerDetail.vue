<template>
  <PartyWorkSurface :id="id" :party-type="FinancePartyType.Customer" />
</template>

<script setup lang="ts">
/**
 * Customer work surface. The shape is shared with the vendor page - see
 * `components/PartyWorkSurface.vue`; only the direction of the money differs.
 */
import PartyWorkSurface from './components/PartyWorkSurface.vue'
import { FinancePartyType } from '../../services/bridges/finance-bridge'

defineProps<{ id: string }>()
</script>
