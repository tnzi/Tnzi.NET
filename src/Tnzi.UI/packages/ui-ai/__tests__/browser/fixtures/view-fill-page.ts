/**
 * The page of `viewFill.browser.test.ts`: the built `TChatApp` with two pages of its own, a short
 * ordinary one (`list`) and one that fills the view area with a frame scrolling inside itself
 * (`frame`, which calls `useChatViewFill`). The test switches between them through `window.shell`.
 */
import { createApp, defineComponent, h, ref } from 'vue'
import { disableRemoteIcons } from '../../../dist/controls.js'
import { TChatApp, useChatViewFill } from '../../../dist/chat.js'

// The shell's icons would be fetched from the network, which a test does not wait for.
disableRemoteIcons()

const FramePage = defineComponent({
  setup() {
    useChatViewFill()
    return () =>
      h('iframe', {
        class: 'frame-page',
        title: 'App',
        srcdoc: '<div style="height:3000px">An app that scrolls itself</div>',
        style: 'display:block;width:100%;height:100%;border:0',
      })
  },
})
const ListPage = defineComponent({ setup: () => () => h('div', { class: 'list-page', style: 'padding:16px' }, 'A short page') })

const view = ref('list')
createApp(
  defineComponent({
    setup: () => () =>
      h(
        TChatApp,
        { sidebarStorageKey: null, autoApplyTheme: false, view: view.value, 'onUpdate:view': (next: string) => (view.value = next) },
        { view: ({ view: current }: { view: string }) => (current === 'frame' ? h(FramePage) : h(ListPage)) },
      ),
  }),
).mount('#app')

;(window as unknown as { shell: unknown }).shell = { show: (next: string) => (view.value = next) }
