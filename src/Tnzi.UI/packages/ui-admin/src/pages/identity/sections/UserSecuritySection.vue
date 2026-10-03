<template>
  <TDetailSection :title="t('detail.sections.security')" :hint="t('detail.security.hint')" max-width="none">
    <template #actions>
      <NButton size="small" tertiary :loading="reloading" @click="reload">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="15" /></template>
        {{ t('admin.common.reload') }}
      </NButton>
    </template>

    <UserSecurityBlocks ref="blocks" :user-id="userId" />
  </TDetailSection>
</template>

<script setup lang="ts">
/**
 * Sign-in security of one account, administered by someone else, as a section
 * of its own.
 *
 * Two things live here because they answer the same question ("what, beyond the
 * password, stands between this account and a token"): the second factor and
 * the sign-in IP allow-list. Both used to be rebuilt per consumer app on top of
 * whatever staff table that app kept; they belong to the account, not to the
 * employee record behind it. `UserSecurityBlocks` is the two of them without
 * chrome; this file adds the section bar (title, hint, Reload).
 *
 * Every write here rides `user.security`; the section is readable with plain
 * `user.view` and renders read-only when the write grant is missing. The
 * blocks read that grant themselves.
 *
 * Hostable: exported from the package root so a consumer's own persona page
 * (staff record, officer file, customer account) can mount it next to its
 * domain fields with nothing but a `userId`. The account behind such a record
 * is where sign-in security lives, and the alternative - deep-linking to the
 * framework Users page - drags in that page's other sections and the grants
 * they need. Hence the section owns everything a host would otherwise have to
 * know: it resolves its strings from the `identity.users` namespace of the
 * admin locale bundle (consumer overrides via `extendLocaleMessages` still
 * win) and reads the write grant itself, so no host can gate the writes on
 * the wrong code. A host whose record page already has the section these
 * blocks belong in mounts `UserSecurityBlocks` inside it instead.
 */
import { ref } from 'vue'
import { NButton } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TDetailSection from '../../../components/detail/TDetailSection.vue'
import UserSecurityBlocks from './UserSecurityBlocks.vue'
import { makePageTranslator } from '../../_shared/translate'

defineProps<{
  /** The account whose sign-in security is shown. */
  userId: string
}>()

const t = makePageTranslator('identity.users')

const blocks = ref<InstanceType<typeof UserSecurityBlocks> | null>(null)
const reloading = ref(false)

async function reload(): Promise<void> {
  reloading.value = true
  try {
    await blocks.value?.reload()
  } finally {
    reloading.value = false
  }
}
</script>
