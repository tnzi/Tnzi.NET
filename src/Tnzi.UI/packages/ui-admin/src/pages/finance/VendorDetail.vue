<template>
  <PartyWorkSurface :id="id" :party-type="FinancePartyType.Vendor" />
</template>

<script setup lang="ts">
/**
 * Vendor work surface. The shape is shared with the customer page - see
 * `components/PartyWorkSurface.vue`; only the direction of the money differs.
 */
import PartyWorkSurface from './components/PartyWorkSurface.vue'
import { FinancePartyType } from '../../services/bridges/finance-bridge'

defineProps<{ id: string }>()
</script>
