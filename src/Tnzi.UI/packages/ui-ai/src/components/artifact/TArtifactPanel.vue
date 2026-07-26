<script setup lang="ts">
/**
 * @experimental
 * TArtifactPanel - Manus-inspired side artifact panel.
 *
 * Supports two modes:
 *
 * **Single-file mode** - pass `artifact: { title, content }`. The panel
 * renders a single Shiki-highlighted code view with a title row.
 *
 * **Project mode** - pass `files: ArtifactFile[]`. The panel grows a
 * file list sidebar on the left, a breadcrumb path at the top, and lets
 * the user pick which file to view via `v-model:active-file`.
 *
 * ## Other features
 *
 * - **Tab row**: Preview / Code / History (`v-model:view`)
 * - **Resizable width** via a left-edge drag handle (`v-model:width`,
 *   bounded by `minWidth` / `maxWidth`)
 * - **Built-in Shiki highlighting** via the `useCodeHighlight` composable
 * - Shiki is dynamically imported only when the panel mounts
 * - **Toolbar** with copy / download / open-external + custom slot
 * - `#preview` / `#history` slots for tab content overrides
 *
 * Theme defaults to `github-light` (matches Manus's read-only code view).
 * Different from the lightweight `Artifact` container - this one owns
 * tabs, resize, file selection and Shiki rendering.
 */
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { Icon } from '@iconify/vue'
import { useCodeHighlight, detectLangFromFilename } from '@/composables/useCodeHighlight'

export type ArtifactView = 'preview' | 'code' | 'history'

/** Shape used by single-file mode (title + content). */
export interface ArtifactPanelItem {
  title: string
  content: string
}

/** Shape used by project (multi-file) mode. */
export interface ArtifactFile {
  /** Unique identifier and breadcrumb path (e.g. "src/App.tsx"). */
  path: string
  /** File contents as plain text. */
  content: string
  /** Optional Shiki lang id; auto-detected from the path extension. */
  lang?: string
  /** Optional display name override; defaults to basename of path. */
  name?: string
}

const props = withDefaults(
  defineProps<{
    /**
     * Single-file artifact. Use this OR `files`. When both are passed,
     * `files` takes precedence.
     */
    artifact?: ArtifactPanelItem | null
    /** Multi-file project mode. Renders the file list sidebar. */
    files?: readonly ArtifactFile[]
    /** Currently active file path in project mode. v-model:active-file. */
    activeFile?: string
    /** Active tab. v-model:view. */
    view?: ArtifactView
    /** Manual lang override (single-file mode). */
    lang?: string
    /** Shiki theme. Defaults to `github-light` (matches Manus). */
    theme?: string
    /** Hide the toolbar button cluster (for embedded variants). */
    hideToolbar?: boolean
    /**
     * Live preview URL. When set, the Preview tab renders a mini-browser
     * chrome (device toggle / URL input / refresh / external / Edit) +
     * an iframe loading this URL. When omitted, Preview shows the
     * placeholder empty state instead.
     */
    previewUrl?: string
    /** Device mode for the preview iframe. v-model:preview-device. */
    previewDevice?: 'desktop' | 'tablet' | 'mobile'
    /** Hide the desktop/tablet/mobile toggle in the preview chrome. */
    hidePreviewDeviceToggle?: boolean
    /**
     * Iframe `sandbox` attribute for the preview pane.
     *
     * Defaults to `allow-scripts allow-forms allow-popups` and deliberately
     * omits `allow-same-origin`: this panel exists to preview untrusted,
     * model-generated HTML, and `allow-scripts` + `allow-same-origin`
     * together void the sandbox (the framed document can reach
     * `parent.document`, read/write the host's storage, and even strip its
     * own `sandbox` attribute).
     *
     * Only opt back in when the previewed content is first-party and
     * genuinely needs same-origin access:
     * `:iframe-sandbox="'allow-scripts allow-same-origin'"`.
     *
     * An empty string renders `sandbox=""`, which is the strictest possible
     * setting (every restriction applied). To remove the attribute entirely
     * and run the frame unsandboxed, pass `null` explicitly.
     */
    iframeSandbox?: string | null
    /** Current panel width in pixels. v-model:width. */
    width?: number
    /** Minimum width allowed by the resize handle. */
    minWidth?: number
    /** Maximum width allowed by the resize handle. */
    maxWidth?: number
    /** Disable the resize handle entirely. */
    resizable?: boolean
  }>(),
  {
    artifact: null,
    files: () => [],
    activeFile: '',
    view: 'code',
    lang: '',
    theme: 'github-light',
    hideToolbar: false,
    previewUrl: '',
    previewDevice: 'desktop',
    hidePreviewDeviceToggle: false,
    iframeSandbox: 'allow-scripts allow-forms allow-popups',
    width: 520,
    minWidth: 320,
    maxWidth: 900,
    resizable: true,
  },
)

const emit = defineEmits<{
  'update:view': [view: ArtifactView]
  'update:activeFile': [path: string]
  'update:width': [width: number]
  'update:previewDevice': [device: 'desktop' | 'tablet' | 'mobile']
  copy: [content: string]
  download: [file: ArtifactPanelItem | ArtifactFile]
  'open-external': [file: ArtifactPanelItem | ArtifactFile]
  /** Fired when the URL input commits (Enter or blur). */
  'preview:navigate': [url: string]
  /** Fired when the refresh button is clicked. */
  'preview:refresh': []
  /** Fired when the home button is clicked. */
  'preview:home': []
  /** Fired when the Edit button is clicked. */
  'preview:edit': []
}>()

/* --- Mode detection ----------------------------------------------------
   `isProject` is true when the consumer passes a non-empty files array;
   then the file list sidebar + breadcrumb appear and `activeFile` drives
   which file is currently rendered. Otherwise we fall back to single-file
   mode using the legacy `artifact` prop. */
const isProject = computed<boolean>(() => props.files.length > 0)

/* Currently active file in project mode. Falls back to the first file if
   the consumer-provided `activeFile` is empty or doesn't match any path. */
const internalActiveFile = ref<string>('')
const activeFilePath = computed<string>({
  get() {
    if (props.activeFile) return props.activeFile
    if (internalActiveFile.value) return internalActiveFile.value
    return props.files[0]?.path ?? ''
  },
  set(v) {
    internalActiveFile.value = v
    emit('update:activeFile', v)
  },
})

const activeFile = computed<ArtifactFile | null>(() => {
  if (!isProject.value) return null
  return props.files.find((f) => f.path === activeFilePath.value) ?? props.files[0] ?? null
})

/* Unified "thing being shown" abstraction so the rest of the template
   doesn't have to branch on single-file vs project mode at every step. */
const currentItem = computed<{ title: string; content: string; lang?: string } | null>(() => {
  if (isProject.value) {
    const f = activeFile.value
    if (!f) return null
    return {
      title: f.name || basename(f.path),
      content: f.content,
      lang: f.lang,
    }
  }
  if (props.artifact) {
    return {
      title: props.artifact.title,
      content: props.artifact.content,
    }
  }
  return null
})

function basename(path: string): string {
  const i = path.lastIndexOf('/')
  return i >= 0 ? path.slice(i + 1) : path
}

/* --- File tree construction --------------------------------------------
   Convert the flat `files` array (each with a `path` like "src/App.tsx")
   into a recursive tree so the file list can render nested folders with
   collapse/expand. Folders are inferred from path segments - there's no
   concept of an empty folder. Files inside the same folder are grouped
   under their common parent folder node. */
interface TreeNode {
  /** Display label (folder name or file basename). */
  label: string
  /** Full path of this node; for folders this is the joined prefix. */
  path: string
  /** Folder children. */
  children: TreeNode[]
  /** True when this node represents a leaf file. */
  isFile: boolean
  /** Reference back to the original ArtifactFile for files. */
  file: ArtifactFile | null
  /** Depth from root (0 = top level), used for indent. */
  depth: number
}

const fileTree = computed<TreeNode[]>(() => {
  if (!isProject.value) return []
  const root: TreeNode[] = []
  const folderIndex = new Map<string, TreeNode>()
  for (const f of props.files) {
    const segments = f.path.split('/').filter(Boolean)
    let parent: TreeNode[] = root
    let prefix = ''
    for (let i = 0; i < segments.length; i++) {
      const seg = segments[i]!
      const isLast = i === segments.length - 1
      prefix = prefix ? `${prefix}/${seg}` : seg
      if (isLast) {
        parent.push({
          label: f.name || seg,
          path: f.path,
          children: [],
          isFile: true,
          file: f,
          depth: i,
        })
      } else {
        let folder = folderIndex.get(prefix)
        if (!folder) {
          folder = {
            label: seg,
            path: prefix,
            children: [],
            isFile: false,
            file: null,
            depth: i,
          }
          folderIndex.set(prefix, folder)
          parent.push(folder)
        }
        parent = folder.children
      }
    }
  }
  /* Sort each level: folders first, then files, alphabetic within each
     bucket. Matches IDE conventions and Manus's behavior. */
  const sortNodes = (nodes: TreeNode[]): void => {
    nodes.sort((a, b) => {
      if (a.isFile !== b.isFile) return a.isFile ? 1 : -1
      return a.label.localeCompare(b.label)
    })
    for (const n of nodes) {
      if (!n.isFile) sortNodes(n.children)
    }
  }
  sortNodes(root)
  return root
})

/* Folder open/closed state - defaults to all expanded so the user sees
   the full project structure on first open. Toggled per-path via the
   chevron click on each folder row. */
const collapsedFolders = ref<Set<string>>(new Set())
function toggleFolder(path: string): void {
  const next = new Set(collapsedFolders.value)
  if (next.has(path)) next.delete(path)
  else next.add(path)
  collapsedFolders.value = next
}
function isFolderOpen(path: string): boolean {
  return !collapsedFolders.value.has(path)
}

/* Flatten the visible tree into a single render list. Each row knows
   its depth and whether it's a folder so the template can render a
   single v-for instead of recursive components - simpler + avoids
   recursive component registration. */
interface FlatRow {
  node: TreeNode
  visible: boolean
}
const flatRows = computed<FlatRow[]>(() => {
  const rows: FlatRow[] = []
  const walk = (nodes: TreeNode[], parentVisible: boolean): void => {
    for (const n of nodes) {
      rows.push({ node: n, visible: parentVisible })
      if (!n.isFile) {
        const childrenVisible = parentVisible && isFolderOpen(n.path)
        walk(n.children, childrenVisible)
      }
    }
  }
  walk(fileTree.value, true)
  return rows
})

/* Breadcrumb segments split on `/`. The last segment is the file name. */
const breadcrumbSegments = computed<string[]>(() => {
  if (!isProject.value || !activeFile.value) return []
  return activeFile.value.path.split('/').filter(Boolean)
})

/* --- Code highlighting -------------------------------------------------
   Pulls (content, lang) from the unified currentItem and runs Shiki via
   the shared composable. `theme` is reactive too in case the consumer
   swaps github-light ↔ github-dark on the fly. */
const itemCode = computed<string>(() => currentItem.value?.content ?? '')
const itemLang = computed<string>(() => {
  const item = currentItem.value
  if (!item) return 'text'
  return props.lang || item.lang || detectLangFromFilename(item.title)
})
const { html: highlightedCode } = useCodeHighlight(itemCode, itemLang, {
  theme: () => props.theme,
})

/* --- Width resize handle -----------------------------------------------
   Listens for mousedown on the left-edge handle, then captures mousemove
   on the document so the drag continues even when the cursor leaves the
   panel. Width is clamped to [minWidth, maxWidth] and emitted via
   `update:width` so consumers can persist the value. */
const dragStartX = ref(0)
const dragStartWidth = ref(0)
const isDragging = ref(false)

function onResizeStart(e: MouseEvent): void {
  if (!props.resizable) return
  e.preventDefault()
  dragStartX.value = e.clientX
  dragStartWidth.value = props.width
  isDragging.value = true
  document.addEventListener('mousemove', onResizeMove)
  document.addEventListener('mouseup', onResizeEnd)
  document.body.style.cursor = 'ew-resize'
  document.body.style.userSelect = 'none'
}

function onResizeMove(e: MouseEvent): void {
  if (!isDragging.value) return
  // Drag handle is on the LEFT edge; moving cursor left grows width.
  const delta = dragStartX.value - e.clientX
  const next = Math.max(
    props.minWidth,
    Math.min(props.maxWidth, dragStartWidth.value + delta),
  )
  emit('update:width', next)
}

/* Copy-confirmation reset, cleared on unmount so the callback cannot fire
   against a destroyed component. */
let copyResetTimer: ReturnType<typeof setTimeout> | null = null

function onResizeEnd(): void {
  isDragging.value = false
  document.removeEventListener('mousemove', onResizeMove)
  document.removeEventListener('mouseup', onResizeEnd)
  document.body.style.cursor = ''
  document.body.style.userSelect = ''
}

onBeforeUnmount(() => {
  if (isDragging.value) onResizeEnd()
  if (copyResetTimer != null) clearTimeout(copyResetTimer)
})

/* Reset active file when files array changes shape (e.g. switching
   scenarios). Otherwise stale activeFilePath from previous project would
   linger and cause an empty code view. */
watch(
  () => props.files,
  () => {
    internalActiveFile.value = ''
  },
)

/* --- Preview chrome state ---------------------------------------------
   Local URL input value lives in `previewUrlInput` so the user can edit
   freely; on Enter we emit `preview:navigate` for the consumer to handle
   (e.g. update an iframe src). The iframe ref is exposed for refresh - we reset its src to itself which causes a full reload. */
const previewUrlInput = ref<string>(props.previewUrl)
const iframeRef = ref<HTMLIFrameElement | null>(null)

watch(
  () => props.previewUrl,
  (v) => {
    previewUrlInput.value = v
  },
)

/* Device viewport widths matching common breakpoints:
   - mobile = 375px (iPhone 13 / Pixel 7 portrait)
   - tablet = 768px (iPad portrait, generic tablet)
   - desktop = full container (responsive) */
const previewIframeWidth = computed<string>(() => {
  if (props.previewDevice === 'mobile') return '375px'
  if (props.previewDevice === 'tablet') return '768px'
  return '100%'
})

function setPreviewDevice(d: 'desktop' | 'tablet' | 'mobile'): void {
  emit('update:previewDevice', d)
}

function commitNavigate(): void {
  emit('preview:navigate', previewUrlInput.value)
}

function refreshPreview(): void {
  if (iframeRef.value) {
    // Force iframe reload by re-setting src
    const src = iframeRef.value.src
    iframeRef.value.src = 'about:blank'
    requestAnimationFrame(() => {
      if (iframeRef.value) iframeRef.value.src = src
    })
  }
  emit('preview:refresh')
}

function homePreview(): void {
  previewUrlInput.value = '/'
  emit('preview:home')
  emit('preview:navigate', '/')
}

/* --- Header / toolbar interactions ----------------------------------- */
const copied = ref(false)

function setView(v: ArtifactView): void {
  emit('update:view', v)
}

async function handleCopy(): Promise<void> {
  const item = currentItem.value
  if (!item) return
  try {
    await navigator.clipboard.writeText(item.content)
    copied.value = true
    if (copyResetTimer != null) clearTimeout(copyResetTimer)
    copyResetTimer = setTimeout(() => {
      copyResetTimer = null
      copied.value = false
    }, 1500)
  } catch {
    /* clipboard permission denied - silent */
  }
  emit('copy', item.content)
}

function selectFile(path: string): void {
  activeFilePath.value = path
}
</script>

<template>
  <aside
    v-if="currentItem"
    class="t-artifact-panel"
    :style="{ width: `${width}px` }"
  >
    <!-- Left-edge resize handle. Sits 4px outside the panel so it's
         still hittable when the panel is flush against the chat column. -->
    <div
      v-if="resizable"
      class="t-artifact-panel__resize"
      :class="{ 'is-dragging': isDragging }"
      @mousedown="onResizeStart"
    >
      <div class="t-artifact-panel__resize-pill" />
    </div>

    <header class="t-artifact-panel__head">
      <div class="t-artifact-panel__tabs">
        <button
          type="button"
          class="t-artifact-panel__tab"
          :class="{ 'is-active': view === 'preview' }"
          :aria-pressed="view === 'preview'"
          @click="setView('preview')"
        >
          <Icon icon="lucide:play" />
          <span>Preview</span>
        </button>
        <button
          type="button"
          class="t-artifact-panel__tab"
          :class="{ 'is-active': view === 'code' }"
          :aria-pressed="view === 'code'"
          @click="setView('code')"
        >
          <Icon icon="lucide:code" />
          <span>Code</span>
        </button>
        <button
          type="button"
          class="t-artifact-panel__tab"
          :class="{ 'is-active': view === 'history' }"
          :aria-pressed="view === 'history'"
          @click="setView('history')"
        >
          <Icon icon="lucide:history" />
        </button>
      </div>
      <div class="t-artifact-panel__spacer" />
      <template v-if="!hideToolbar">
        <slot name="toolbar" />
        <button
          type="button"
          class="t-artifact-panel__tool"
          :aria-label="copied ? 'Copied' : 'Copy code'"
          @click="handleCopy"
        >
          <Icon :icon="copied ? 'lucide:check' : 'lucide:copy'" />
        </button>
        <button
          type="button"
          class="t-artifact-panel__tool"
          aria-label="Download"
          @click="emit('download', isProject ? activeFile! : artifact!)"
        >
          <Icon icon="lucide:download" />
        </button>
        <button
          type="button"
          class="t-artifact-panel__tool"
          aria-label="Open in new tab"
          @click="emit('open-external', isProject ? activeFile! : artifact!)"
        >
          <Icon icon="lucide:external-link" />
        </button>
      </template>
    </header>

    <!-- Body: in project mode renders [file list | code view]. In single
         file mode renders just the [code view]. -->
    <div class="t-artifact-panel__body">
      <!-- File tree sidebar (project mode only). Renders the flattened
           visible rows from `flatRows`. Folder rows have a chevron toggle
           that hides/shows their children; file rows fire `selectFile`. -->
      <nav v-if="isProject" class="t-artifact-panel__filelist">
        <template v-for="row in flatRows" :key="row.node.path">
          <button
            v-if="row.visible"
            type="button"
            class="t-artifact-panel__file"
            :class="{
              'is-active': row.node.isFile && row.node.path === activeFilePath,
              'is-folder': !row.node.isFile,
            }"
            :style="{ paddingLeft: `${8 + row.node.depth * 14}px` }"
            @click="row.node.isFile ? selectFile(row.node.path) : toggleFolder(row.node.path)"
          >
            <Icon
              v-if="!row.node.isFile"
              :icon="isFolderOpen(row.node.path) ? 'lucide:chevron-down' : 'lucide:chevron-right'"
              class="t-artifact-panel__file-chevron"
            />
            <Icon
              :icon="row.node.isFile ? 'lucide:file-code-2' : (isFolderOpen(row.node.path) ? 'lucide:folder-open' : 'lucide:folder')"
              class="t-artifact-panel__file-icon"
            />
            <span class="t-artifact-panel__file-name">{{ row.node.label }}</span>
          </button>
        </template>
      </nav>

      <!-- Main column: breadcrumb (project mode) + active view -->
      <div class="t-artifact-panel__main">
        <div v-if="isProject && activeFile" class="t-artifact-panel__breadcrumb">
          <template v-for="(seg, i) in breadcrumbSegments" :key="i">
            <span class="t-artifact-panel__breadcrumb-sep" v-if="i > 0">/</span>
            <span class="t-artifact-panel__breadcrumb-seg">{{ seg }}</span>
          </template>
        </div>
        <div v-else-if="!isProject" class="t-artifact-panel__title">
          <Icon icon="lucide:file-code-2" />
          <span>{{ currentItem.title }}</span>
        </div>

        <div v-if="view === 'preview'" class="t-artifact-panel__preview">
          <slot name="preview">
            <!-- Mini-browser chrome: device toggle | URL bar (home + input
                 + refresh + external) | Edit button cluster. Renders only
                 when previewUrl is provided; otherwise falls back to the
                 placeholder empty state. -->
            <template v-if="previewUrl">
              <div class="t-artifact-panel__chrome">
                <div v-if="!hidePreviewDeviceToggle" class="t-artifact-panel__chrome-devices">
                  <button
                    type="button"
                    class="t-artifact-panel__chrome-btn"
                    :class="{ 'is-active': previewDevice === 'desktop' }"
                    aria-label="Desktop view"
                    @click="setPreviewDevice('desktop')"
                  >
                    <Icon icon="lucide:monitor" />
                  </button>
                  <button
                    type="button"
                    class="t-artifact-panel__chrome-btn"
                    :class="{ 'is-active': previewDevice === 'tablet' }"
                    aria-label="Tablet view"
                    @click="setPreviewDevice('tablet')"
                  >
                    <Icon icon="lucide:tablet" />
                  </button>
                  <button
                    type="button"
                    class="t-artifact-panel__chrome-btn"
                    :class="{ 'is-active': previewDevice === 'mobile' }"
                    aria-label="Mobile view"
                    @click="setPreviewDevice('mobile')"
                  >
                    <Icon icon="lucide:smartphone" />
                  </button>
                </div>
                <div class="t-artifact-panel__chrome-urlbar">
                  <button
                    type="button"
                    class="t-artifact-panel__chrome-btn"
                    aria-label="Home"
                    @click="homePreview"
                  >
                    <Icon icon="lucide:house" />
                  </button>
                  <input
                    v-model="previewUrlInput"
                    type="text"
                    class="t-artifact-panel__chrome-url"
                    placeholder="/"
                    @keydown.enter="commitNavigate"
                    @blur="commitNavigate"
                  />
                  <button
                    type="button"
                    class="t-artifact-panel__chrome-btn"
                    aria-label="Open in new tab"
                    @click="emit('open-external', isProject ? activeFile! : artifact!)"
                  >
                    <Icon icon="lucide:external-link" />
                  </button>
                  <button
                    type="button"
                    class="t-artifact-panel__chrome-btn"
                    aria-label="Refresh"
                    @click="refreshPreview"
                  >
                    <Icon icon="lucide:refresh-cw" />
                  </button>
                </div>
                <button
                  type="button"
                  class="t-artifact-panel__chrome-edit"
                  aria-label="Edit"
                  @click="emit('preview:edit')"
                >
                  <Icon icon="lucide:square-pen" />
                  <span>Edit</span>
                </button>
              </div>
              <div class="t-artifact-panel__iframe-wrap">
                <iframe
                  ref="iframeRef"
                  class="t-artifact-panel__iframe"
                  :style="{ width: previewIframeWidth }"
                  :src="previewUrl"
                  :sandbox="iframeSandbox ?? undefined"
                  title="Artifact preview"
                  loading="lazy"
                />
              </div>
            </template>
            <div v-else class="t-artifact-panel__empty">
              <Icon icon="lucide:play" class="t-artifact-panel__empty-icon" />
              <div class="t-artifact-panel__empty-title">Live preview</div>
              <div class="t-artifact-panel__empty-sub">
                Runtime preview would render here. Switch to
                <strong>Code</strong> to inspect the source.
              </div>
            </div>
          </slot>
        </div>

        <div
          v-else-if="view === 'code'"
          class="t-artifact-panel__code"
          v-html="highlightedCode"
        />

        <div v-else class="t-artifact-panel__section">
          <slot name="history">
            <div class="t-artifact-panel__empty">
              <Icon icon="lucide:history" class="t-artifact-panel__empty-icon" />
              <div class="t-artifact-panel__empty-title">No history yet</div>
              <div class="t-artifact-panel__empty-sub">
                Iterations of this artifact will appear here once you edit it.
              </div>
            </div>
          </slot>
        </div>
      </div>
    </div>
  </aside>
</template>

<style scoped>
.t-artifact-panel {
  position: relative;
  height: 100%;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  border-radius: 12px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  flex-shrink: 0;
}

/* --- Resize handle (left edge, 8px hit area, 4px visual pill) --- */
.t-artifact-panel__resize {
  position: absolute;
  left: -4px;
  top: 0;
  bottom: 0;
  width: 8px;
  z-index: 10;
  cursor: ew-resize;
  display: flex;
  align-items: stretch;
  justify-content: center;
  padding: 12px 0;
}
.t-artifact-panel__resize-pill {
  width: 4px;
  border-radius: 999px;
  background: transparent;
  transition: background 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-artifact-panel__resize:hover .t-artifact-panel__resize-pill,
.t-artifact-panel__resize.is-dragging .t-artifact-panel__resize-pill {
  background: var(--tnzi-ai-text-tertiary, #9a9a9a);
}

/* --- Header --- */
.t-artifact-panel__head {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 10px;
  border-bottom: 1px solid var(--tnzi-ai-divider, #ececec);
  flex-shrink: 0;
}
.t-artifact-panel__tabs {
  display: flex;
  gap: 2px;
}
.t-artifact-panel__tab {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 28px;
  padding: 0 10px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  border-radius: 6px;
  font-family: inherit;
  font-size: 12px;
  cursor: pointer;
}
.t-artifact-panel__tab:hover {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-artifact-panel__tab.is-active {
  background: var(--tnzi-ai-selected, rgba(55, 53, 47, 0.08));
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-artifact-panel__spacer {
  flex: 1;
}
.t-artifact-panel__tool {
  width: 28px;
  height: 28px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  border-radius: 6px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
}
.t-artifact-panel__tool:hover {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
  color: var(--tnzi-ai-text, #1a1a1a);
}

/* --- Body: file list + main column --- */
.t-artifact-panel__body {
  flex: 1;
  min-height: 0;
  display: flex;
  overflow: hidden;
}
.t-artifact-panel__filelist {
  width: 180px;
  flex-shrink: 0;
  border-right: 1px solid var(--tnzi-ai-divider, #ececec);
  overflow-y: auto;
  padding: 6px 4px;
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.t-artifact-panel__file {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  border: none;
  background: transparent;
  border-radius: 6px;
  cursor: pointer;
  font-family: inherit;
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  text-align: left;
  width: 100%;
}
.t-artifact-panel__file:hover {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-artifact-panel__file.is-active {
  background: var(--tnzi-ai-selected, rgba(55, 53, 47, 0.08));
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-artifact-panel__file-icon {
  font-size: 14px;
  flex-shrink: 0;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}
.t-artifact-panel__file.is-active .t-artifact-panel__file-icon {
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
.t-artifact-panel__file-chevron {
  font-size: 12px;
  flex-shrink: 0;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  margin-right: -4px;
}
.t-artifact-panel__file.is-folder {
  font-weight: 500;
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-artifact-panel__file.is-folder .t-artifact-panel__file-icon {
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
.t-artifact-panel__file-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}

.t-artifact-panel__main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.t-artifact-panel__breadcrumb {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 10px 16px 6px;
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  border-bottom: 1px solid var(--tnzi-ai-divider, #ececec);
  flex-shrink: 0;
}
.t-artifact-panel__breadcrumb-seg:last-child {
  color: var(--tnzi-ai-text, #1a1a1a);
  font-weight: 500;
}
.t-artifact-panel__breadcrumb-sep {
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}
.t-artifact-panel__title {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 16px 8px;
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-ai-text, #1a1a1a);
  flex-shrink: 0;
}
.t-artifact-panel__section {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 24px;
}

/* --- Preview tab: mini-browser chrome + iframe ---
   Layout matches Manus exactly: device toggle on the left, URL bar
   (home + input + external + refresh) in the middle taking flex-1, and
   a labeled Edit button on the right. The iframe sits below in a
   centered wrapper so mobile-mode (375px) can be horizontally centered. */
.t-artifact-panel__preview {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-artifact-panel__chrome {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  border-bottom: 1px solid var(--tnzi-ai-divider, #ececec);
  flex-shrink: 0;
}
.t-artifact-panel__chrome-devices {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  padding-right: 4px;
  border-right: 1px solid var(--tnzi-ai-divider, #ececec);
  margin-right: 2px;
}
.t-artifact-panel__chrome-urlbar {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 4px;
  height: 32px;
  padding: 0 6px;
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  border-radius: 8px;
  min-width: 0;
}
.t-artifact-panel__chrome-url {
  flex: 1;
  min-width: 0;
  background: transparent;
  border: none;
  outline: none;
  font-family: inherit;
  font-size: 13px;
  color: var(--tnzi-ai-text, #1a1a1a);
  padding: 0 6px;
}
.t-artifact-panel__chrome-url::placeholder {
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}
.t-artifact-panel__chrome-btn {
  width: 26px;
  height: 26px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  border-radius: 6px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  flex-shrink: 0;
}
.t-artifact-panel__chrome-btn:hover {
  background: var(--tnzi-ai-selected, rgba(55, 53, 47, 0.08));
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-artifact-panel__chrome-btn.is-active {
  background: var(--tnzi-ai-selected, rgba(55, 53, 47, 0.08));
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-artifact-panel__chrome-edit {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 28px;
  padding: 0 12px;
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  background: var(--tnzi-ai-surface, #ffffff);
  border-radius: 8px;
  font-family: inherit;
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-ai-text, #1a1a1a);
  cursor: pointer;
  flex-shrink: 0;
}
.t-artifact-panel__chrome-edit:hover {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
}
.t-artifact-panel__iframe-wrap {
  flex: 1;
  min-height: 0;
  display: flex;
  justify-content: center;
  background: var(--tnzi-ai-bg, #f8f8f7);
  overflow: auto;
}
.t-artifact-panel__iframe {
  height: 100%;
  border: none;
  /* The framed document paints its own background; this only fills the
     letterboxing around a narrower device width. */
  background: var(--tnzi-ai-surface);
  max-width: 100%;
}
.t-artifact-panel__empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  max-width: 320px;
  text-align: center;
}
.t-artifact-panel__empty-icon {
  font-size: 28px;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}
.t-artifact-panel__empty-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-artifact-panel__empty-sub {
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
.t-artifact-panel__empty-sub :deep(strong) {
  color: var(--tnzi-ai-text, #1a1a1a);
  font-weight: 500;
}

/* --- Shiki code view --- */
.t-artifact-panel__code {
  flex: 1;
  min-height: 0;
  overflow: auto;
  padding: 4px 0 16px;
}
.t-artifact-panel__code :deep(pre.shiki) {
  margin: 0;
  padding: 12px 16px;
  background: transparent !important;
  font-family: var(--tnzi-ai-font-mono, ui-monospace, SFMono-Regular, Menlo, Consolas, monospace);
  font-size: 12px;
  line-height: 1.65;
  white-space: pre;
  overflow-x: auto;
}
.t-artifact-panel__code :deep(pre.shiki code) {
  background: transparent;
  font-family: inherit;
  /* Shiki emits `<span class="line">…</span>\n` for each source line.
     The `\n` inside <pre> already creates the visual line break - making
     `.line` `display: block` would stack them as blocks AND keep the
     `\n` newline, producing a blank row between every line. Leave the
     spans inline so the natural newlines are the only separators. */
  display: block;
}
</style>
