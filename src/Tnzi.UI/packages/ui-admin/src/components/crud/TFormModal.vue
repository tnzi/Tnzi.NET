<template>
  <TModalShell
    :show="show"
    :title="title"
    :width="width"
    :content-max-height-vh="contentMaxHeightVh"
    :fullscreen="fullscreen"
    @update:show="onUpdateShow"
  >
    <slot :form-data="rendered.formData.value" :mode="rendered.mode.value" />
    <template #footer>
      <slot name="footer">
        <div class="t-form-modal__footer">
          <!-- The label toggles the switch too: it is the bigger target. -->
          <div v-if="shownSwitch" class="t-form-modal__switch">
            <span class="t-form-modal__switch-label" @click="setSwitch(!switchOn)">{{ t(shownSwitch.label) }}</span>
            <THint v-if="shownSwitch.hint" :content="t(shownSwitch.hint)" :size="14" />
            <NSwitch :value="switchOn" :aria-label="t(shownSwitch.label)" @update:value="setSwitch" />
          </div>
          <NButton class="t-form-modal__cancel" @click="onCancel">
            {{ t('admin.common.cancel') }}
          </NButton>
          <NButton
            v-if="rendered.mode.value !== 'view'"
            type="primary"
            class="t-form-modal__confirm"
            :loading="busy"
            @click="onConfirm"
          >
            {{ t('admin.common.confirm') }}
          </NButton>
        </div>
      </slot>
    </template>
  </TModalShell>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { NButton, NSwitch } from 'naive-ui'
import { THint, TModalShell } from '@tnzi/ui'
import { provideFormHost } from '@tnzi/ui/headless'
import { useFormModal, useRetainedFormState, type FormFooterSwitch } from '../../headless/useFormModal'

type FormState = ReturnType<typeof useFormModal<unknown>>

interface Props {
  state: FormState
  title: string
  width?: number
  /**
   * Max viewport height (vh) the inner scroll area is allowed to occupy.
   * Forwarded to {@link TModalShell}. Default 65.
   */
  contentMaxHeightVh?: number
  /**
   * Force fullscreen layout regardless of viewport size. When unset, the modal
   * auto-switches to fullscreen on viewports narrower than the configured
   * `width` (or `<640px`, whichever is wider). Forwarded to {@link TModalShell}.
   */
  fullscreen?: boolean
  /**
   * Suppress this modal for the `view` action - the host renders the read-only
   * detail in a drawer (its `#detail` slot) instead. Create/edit still open the
   * modal. Off by default, so a page with no `#detail` slot keeps view-in-modal.
   */
  skipViewMode?: boolean
  /** Opt-in switch at the footer's left (a record flag such as "Active"); see {@link FormFooterSwitch}. */
  footerSwitch?: FormFooterSwitch
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  width: 560,
  contentMaxHeightVh: 65,
  fullscreen: undefined,
  skipViewMode: false,
  footerSwitch: undefined,
  translate: undefined,
})

// What the chrome RENDERS - the last (mode, formData) pair the state held while
// open, kept until the leave transition has taken the body off screen. Identical
// to the live refs whenever the modal is open; see `useRetainedFormState`.
const rendered = useRetainedFormState(props.state)

// The form modal yields to a sibling view-drawer when the host opted in
// (`skipViewMode`) and the open-state is a read-only `view`. One open-state,
// chrome chosen by action: create/edit → this modal, view → the drawer.
const show = computed(
  () => props.state.visible.value && !(props.skipViewMode && rendered.mode.value === 'view'),
)

const emit = defineEmits<{
  submit: []
}>()

// The modal owns the Confirm button, so it owns the form host: every
// `TSchemaForm` rendered in its slot registers here, and Confirm validates
// them before `submit` leaves. Without this a `required` rule drew its message
// under the field while the request still went out and the backend's 400
// toast came back on top of it.
const formHost = provideFormHost()

/** The footer switch when this mode shows it: never in view mode, edit only by default. */
const shownSwitch = computed(() => {
  const sw = props.footerSwitch
  const mode = rendered.mode.value
  return sw && mode && mode !== 'view' && (sw.modes ?? ['edit']).includes(mode) ? sw : null
})

const switchOn = computed(() => {
  const data = rendered.formData.value as Record<string, unknown> | null | undefined
  return !!shownSwitch.value && data?.[shownSwitch.value.key] === true
})

function setSwitch(value: boolean): void {
  const data = rendered.formData.value as Record<string, unknown> | null | undefined
  if (shownSwitch.value && data) data[shownSwitch.value.key] = value
}

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

function onUpdateShow(value: boolean): void {
  if (!value) props.state.close()
}

function onCancel(): void {
  props.state.close()
}

// One save per Confirm. `validating` covers the click-to-emit window (the
// validation is async, so a second click would otherwise pass it too);
// `state.submitting` covers the request itself, raised by the engine that
// owns the write. A hand-rolled state without the flag still gets the first.
const validating = ref(false)
const busy = computed(() => validating.value || props.state.submitting?.value === true)

async function onConfirm(): Promise<void> {
  if (busy.value) return
  validating.value = true
  let valid: boolean
  try {
    valid = await formHost.validate()
  } finally {
    validating.value = false
  }
  if (!valid) return
  emit('submit')
}
</script>

<style scoped>
/*
 * `flex-grow` fills the modal shell's footer row, which is itself `flex-end`: without it this box shrink-wraps
 * its children and the switch's `margin-right: auto` has no free space to take, so the switch lands beside
 * Cancel. Without a switch the buttons still sit at the right as before. On phones the shell's footer is a
 * column, where growing is vertical and there is nothing to grow into.
 */
.t-form-modal__footer {
  display: flex;
  flex-grow: 1;
  justify-content: flex-end;
  align-items: center;
  gap: var(--tnzi-spacing-sm, 8px);
}
/* The switch takes the free space, so it sits at the left opposite the buttons. */
.t-form-modal__switch {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-right: auto;
}
/* Phone: the shell stretches the buttons full width, so the switch gets its own line above them. */
@media (max-width: 767px) {
  .t-form-modal__footer:has(.t-form-modal__switch) {
    flex-wrap: wrap;
  }
  .t-form-modal__footer:has(.t-form-modal__switch) .t-form-modal__switch {
    flex-basis: 100%;
  }
}
.t-form-modal__switch-label {
  cursor: pointer;
  user-select: none;
}
</style>
