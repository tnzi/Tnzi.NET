<template>
  <div class="usb">
    <div class="usb__block">
      <TTwoFactorPanel ref="twoFactor" mode="admin" :user-id="userId" />
    </div>
    <div class="usb__block">
      <TSignInPolicyPanel ref="policy" :user-id="userId" />
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * Sign-in security of one account, as blocks and nothing else: the second
 * factor (`TTwoFactorPanel` in admin mode) over the sign-in IP allow-list
 * (`TSignInPolicyPanel`), one rule between them, no section chrome.
 *
 * This is the shape a consumer's own record page needs when sign-in security
 * belongs to a section that already exists there (an "Access" section with
 * the login window and the password reset, say): the blocks go in under the
 * host's own, inside the host's `TDetailSection`. `UserSecuritySection` is
 * this plus that chrome, for the identity user page and for hosts that want a
 * section of its own.
 *
 * The rule between the two blocks is drawn here, so the pair reads the same
 * wherever it is mounted; the host draws whatever separates its own blocks
 * from the first of these. Both panels read `user.security` themselves and
 * resolve their own strings, so a host passes nothing but the `userId`.
 */
import { ref } from 'vue'
import TTwoFactorPanel from '../../../components/auth/TTwoFactorPanel.vue'
import TSignInPolicyPanel from '../../../components/auth/TSignInPolicyPanel.vue'

defineProps<{
  /** The account whose sign-in security is shown. */
  userId: string
}>()

defineOptions({ name: 'UserSecurityBlocks' })

const twoFactor = ref<InstanceType<typeof TTwoFactorPanel> | null>(null)
const policy = ref<InstanceType<typeof TSignInPolicyPanel> | null>(null)

defineExpose({
  /** Re-fetch both halves; a failing one never hides the other. */
  reload: async (): Promise<void> => {
    await Promise.allSettled([twoFactor.value?.reload(), policy.value?.reload()])
  },
})
</script>

<style scoped>
.usb__block + .usb__block {
  margin-top: 28px;
  padding-top: 24px;
  border-top: 1px solid var(--tnzi-border);
}
</style>
