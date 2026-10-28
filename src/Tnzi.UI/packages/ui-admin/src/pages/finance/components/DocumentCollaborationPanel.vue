<template>
  <div v-if="canViewAttachments || canViewComments" class="fin-collab">
    <TAttachmentPanel
      v-if="canViewAttachments"
      :items="attachments"
      :loading="loadingAttachments"
      :can-add="canAttach"
      :can-remove="canRemoveAttachment"
      :upload="upload"
      :attach="attach"
      :remove="removeAttachment"
      :translate="t"
      @changed="loadAttachments"
    />

    <TCommentThread
      v-if="canViewComments"
      :items="comments"
      :loading="loadingComments"
      :can-post="canComment"
      :post="postComment"
      :remove="removeComment"
      :translate="t"
      @changed="loadComments"
    />
  </div>
</template>

<script setup lang="ts">
/**
 * Binds the two generic collaboration primitives to a finance document.
 *
 * The upload path is two steps on purpose: the file goes to Storage, then only
 * its id is linked to the document. Finance itself holds no reference to
 * Storage - that separation is why a pure-accounting deployment does not drag
 * the whole file stack in behind it.
 */
import { computed, ref, watch } from 'vue'
import TAttachmentPanel from '../../../components/data/TAttachmentPanel.vue'
import TCommentThread from '../../../components/data/TCommentThread.vue'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import { makePageTranslator } from '../../_shared/translate'
import { useSafeMessage } from '../../_shared/safe-message'
import { useAdminClient } from '../../../plugin/client'
import { createStorageBridge } from '../../../services/bridges/storage-bridge'
import {
  createFinanceBridge,
  type DocumentAttachmentDto,
  type DocumentCommentDto,
} from '../../../services/bridges/finance-bridge'

const props = defineProps<{
  /** Source token (see FINANCE_SOURCE_TYPES); a consumer app's own token works too. */
  docType: string
  docId: string
}>()

const client = useAdminClient()
const finance = createFinanceBridge({ client })
const storage = createStorageBridge({ client })
const { can } = usePermissionGuard()
const t = makePageTranslator('finance.docs')
const message = useSafeMessage()

const attachments = ref<DocumentAttachmentDto[]>([])
const comments = ref<DocumentCommentDto[]>([])
const loadingAttachments = ref(false)
const loadingComments = ref(false)

// Each half has its own read code: seeing a document does not mean seeing its
// internal discussion. Without the code the read is a certain 403, so it is not
// sent and that half is not shown.
const canViewAttachments = computed(() => can('finance.attachment.view'))
const canViewComments = computed(() => can('finance.comment.view'))
const canAttach = computed(() => can('finance.attachment.create'))
const canRemoveAttachment = computed(() => can('finance.attachment.delete'))
const canComment = computed(() => can('finance.comment.create'))

// A failed read is said out loud: the empty list it leaves behind would
// otherwise read as "this document has no attachments / no comments".
async function loadAttachments() {
  if (!props.docId || !canViewAttachments.value) return
  loadingAttachments.value = true
  try {
    attachments.value = await finance.collaboration.attachments(props.docType, props.docId)
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    loadingAttachments.value = false
  }
}

async function loadComments() {
  if (!props.docId || !canViewComments.value) return
  loadingComments.value = true
  try {
    comments.value = await finance.collaboration.comments(props.docType, props.docId)
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    loadingComments.value = false
  }
}

async function upload(file: File) {
  const stored = await storage.files.upload(file)
  return {
    // `originalName` is what the person picked; `fileName` is what Storage
    // stored it as. The list should show the former.
    fileId: stored.id,
    fileName: stored.originalName || stored.fileName || file.name,
    contentType: stored.contentType || file.type,
    fileSize: stored.size ?? file.size,
  }
}

async function attach(linked: { fileId: string; fileName: string; contentType?: string | null; fileSize: number }) {
  await finance.collaboration.attach(props.docType, props.docId, linked)
}

async function removeAttachment(item: { id: string }) {
  await finance.collaboration.removeAttachment(item.id)
}

async function postComment(body: string) {
  await finance.collaboration.postComment(props.docType, props.docId, body)
}

async function removeComment(item: { id: string }) {
  await finance.collaboration.deleteComment(item.id)
}

watch(
  () => [props.docType, props.docId] as const,
  () => {
    attachments.value = []
    comments.value = []
    void loadAttachments()
    void loadComments()
  },
  { immediate: true },
)
</script>

<style scoped>
.fin-collab {
  display: flex;
  flex-direction: column;
  gap: 18px;
  margin-top: 14px;
  padding-top: 14px;
  border-top: 1px solid var(--tnzi-border);
}
</style>
