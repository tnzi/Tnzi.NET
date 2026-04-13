import { reactive, nextTick } from 'vue';

const state = reactive({
  loading: false,
  status: 'starting' as 'starting' | 'finishing' | 'error',
  maxWidth: 0,
});

let hideTimer: ReturnType<typeof setTimeout> | null = null;

function clearHideTimer() {
  if (hideTimer) {
    clearTimeout(hideTimer);
    hideTimer = null;
  }
}

function scheduleHide() {
  clearHideTimer();
  hideTimer = setTimeout(() => {
    state.loading = false;
    hideTimer = null;
  }, 800);
}

async function start() {
  clearHideTimer();
  state.loading = true;
  state.status = 'starting';
  state.maxWidth = 0;
  await nextTick();
  requestAnimationFrame(() => {
    state.maxWidth = 80;
  });
}

async function finish() {
  if (!state.loading) {
    // Not started yet — do a quick start→finish sequence
    await start();
    await nextTick();
  }
  state.status = 'finishing';
  state.maxWidth = 100;
  scheduleHide();
}

async function error() {
  if (!state.loading) {
    await start();
    await nextTick();
  }
  state.status = 'error';
  state.maxWidth = 100;
  scheduleHide();
}

export const loadingBarApi = { start, finish, error };
export const loadingBarState = state;
