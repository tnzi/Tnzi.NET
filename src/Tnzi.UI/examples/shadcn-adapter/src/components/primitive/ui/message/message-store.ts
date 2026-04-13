import { reactive } from 'vue';
import type { MessageType, MessageOptions, MessageReactive, MessageApi, MessagePlacement } from './types';

let _idCounter = 0;

export interface MessageEntry {
  key: string;
  content: string;
  type: MessageType;
  duration: number;
  closable: boolean;
  keepAliveOnHover: boolean;
  showIcon: boolean;
  icon?: () => any;
  onClose?: () => void;
  onAfterLeave?: () => void;
}

interface MessageState {
  messages: MessageEntry[];
  placement: MessagePlacement;
  max: number;
  defaultDuration: number;
}

const state = reactive<MessageState>({
  messages: [],
  placement: 'top',
  max: 0,
  defaultDuration: 3000,
});

function createMessage(type: MessageType, content: string, options: MessageOptions = {}): MessageReactive {
  const key = `msg_${++_idCounter}`;
  const entry: MessageEntry = {
    key,
    content,
    type,
    duration: options.duration ?? state.defaultDuration,
    closable: options.closable ?? false,
    keepAliveOnHover: options.keepAliveOnHover ?? false,
    showIcon: options.showIcon ?? true,
    icon: options.icon,
    onClose: options.onClose,
    onAfterLeave: options.onAfterLeave,
  };

  state.messages.push(entry);

  // Enforce max limit
  if (state.max > 0 && state.messages.length > state.max) {
    state.messages.shift();
  }

  const reactiveMsg: MessageReactive = {
    key,
    get content() { return entry.content; },
    set content(v: string) { entry.content = v; },
    get type() { return entry.type; },
    set type(v: MessageType) { entry.type = v; },
    destroy: () => removeMessage(key),
  };

  return reactiveMsg;
}

function removeMessage(key: string) {
  const idx = state.messages.findIndex(m => m.key === key);
  if (idx !== -1) state.messages.splice(idx, 1);
}

function destroyAll() {
  state.messages.splice(0);
}

function configure(options: { placement?: MessagePlacement; max?: number; defaultDuration?: number }) {
  if (options.placement) state.placement = options.placement;
  if (options.max !== undefined) state.max = options.max;
  if (options.defaultDuration !== undefined) state.defaultDuration = options.defaultDuration;
}

export const messageApi: MessageApi = {
  info: (content, options) => createMessage('info', content, options),
  success: (content, options) => createMessage('success', content, options),
  warning: (content, options) => createMessage('warning', content, options),
  error: (content, options) => createMessage('error', content, options),
  loading: (content, options) => createMessage('loading', content, options),
  destroyAll,
};

export { state as messageState, removeMessage, configure as configureMessage };
