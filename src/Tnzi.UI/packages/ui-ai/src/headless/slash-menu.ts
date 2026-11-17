/**
 * The composer's `/` menu, without its looks: when it opens, what it lists for what is typed, and
 * the keys that move through it. A CLI agent reads a command only at the very start of a message,
 * so the menu opens only while the box holds nothing but a slash and the name being typed; picking
 * writes the command the way the CLI takes it, followed by a space for its arguments.
 *
 * It reads the box's text and answers keys, nothing more: `TChatApp` wires it to its composer
 * (`slashCommands`), and `TSlashCommandMenu` draws it.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue';

/** What the menu needs of a command, and what it shows of it. */
export interface SlashMenuItem {
  /** What follows the slash, exactly as the CLI takes it (`team-shop:release`). */
  readonly command: string;
  /** The command's own name, without its plugin's (`release`). */
  readonly name: string;
  readonly description?: string | null;
  /** What the command takes after it (`<file>`), shown beside the name. */
  readonly argumentHint?: string | null;
  /** Where it comes from (a project, the user, built in), shown at the end of the row. */
  readonly sourceLabel?: string | null;
  /** Iconify name; default a terminal. */
  readonly icon?: string | null;
}

/** The name being typed after the slash, or null when the box is not a command being written. */
export function slashQuery(text: string): string | null {
  const match = /^\/(\S*)$/.exec(text);
  return match ? match[1]! : null;
}

/** How well an item matches; 0 is no match. A name or command that starts with the query ranks first. */
function score(item: SlashMenuItem, query: string): number {
  if (!query) return 1;
  const q = query.toLowerCase();
  const name = item.name.toLowerCase();
  const command = item.command.toLowerCase();
  // A command's parts: "team-shop:release" starts a word at "team", "shop" and "release".
  const words = command.split(/[:-]/);
  if (name.startsWith(q) || command.startsWith(q)) return 4;
  if (words.some((word) => word.startsWith(q))) return 3;
  if (name.includes(q) || command.includes(q)) return 2;
  if (item.description?.toLowerCase().includes(q)) return 1;
  return 0;
}

/** The items that match `query`, best first, keeping the given order among equals. */
export function filterSlashItems<T extends SlashMenuItem>(items: readonly T[], query: string): T[] {
  return items
    .map((item, index) => ({ item, index, score: score(item, query) }))
    .filter((entry) => entry.score > 0)
    .sort((a, b) => b.score - a.score || a.index - b.index)
    .map((entry) => entry.item);
}

/** The box's text once `item` is picked: the command, then a space for what follows it. */
export function textWithCommand(item: Pick<SlashMenuItem, 'command'>): string {
  return `/${item.command} `;
}

/** The parts of a key press the menu reads. */
export interface SlashMenuKey {
  readonly key: string;
  readonly shiftKey?: boolean;
  readonly isComposing?: boolean;
  readonly keyCode?: number;
}

export interface UseSlashMenuOptions<T extends SlashMenuItem> {
  /** The composer's text. */
  text: Ref<string>;
  /** The commands of the conversation on screen. */
  items: Ref<readonly T[]>;
  /** Called with the item picked; the caller writes `textWithCommand(item)` into the box. */
  onPick: (item: T) => void;
}

export interface UseSlashMenuReturn<T extends SlashMenuItem> {
  /** A command is being typed and the menu was not dismissed for this text. */
  open: ComputedRef<boolean>;
  /** The name being typed after the slash; null when no command is being typed. */
  query: ComputedRef<string | null>;
  results: ComputedRef<T[]>;
  highlighted: Ref<number>;
  pick: (index?: number) => boolean;
  dismiss: () => void;
  /**
   * A key pressed in the composer while the menu may be open; true when the menu took it (the
   * caller then keeps it from the composer, so Enter picks instead of sending). Enter with nothing
   * matching is left to the composer, which sends the text as it is. The Enter that commits an
   * IME candidate is not the menu's either.
   */
  handleKey: (event: SlashMenuKey) => boolean;
}

export function useSlashMenu<T extends SlashMenuItem>(options: UseSlashMenuOptions<T>): UseSlashMenuReturn<T> {
  const query = computed(() => slashQuery(options.text.value));
  const results = computed(() => (query.value === null ? [] : filterSlashItems(options.items.value, query.value)));
  const highlighted = ref(0);
  /** Escape (or a click away) closes the menu until the text changes. */
  const dismissedAt = ref<string | null>(null);
  const open = computed(() => query.value !== null && dismissedAt.value !== options.text.value);

  watch(query, () => {
    highlighted.value = 0;
  });
  watch(options.text, (text) => {
    if (dismissedAt.value !== null && dismissedAt.value !== text) dismissedAt.value = null;
  });

  function move(step: number): void {
    const count = results.value.length;
    if (count === 0) return;
    highlighted.value = (highlighted.value + step + count) % count;
  }

  function pick(index = highlighted.value): boolean {
    const item = results.value[index];
    if (!item) return false;
    options.onPick(item);
    return true;
  }

  function dismiss(): void {
    dismissedAt.value = options.text.value;
  }

  function handleKey(event: SlashMenuKey): boolean {
    if (!open.value || event.isComposing || event.keyCode === 229) return false;
    switch (event.key) {
      case 'ArrowDown':
        move(1);
        return results.value.length > 0;
      case 'ArrowUp':
        move(-1);
        return results.value.length > 0;
      case 'Enter':
      case 'Tab':
        return !event.shiftKey && pick();
      case 'Escape':
        dismiss();
        return true;
      default:
        return false;
    }
  }

  return { open, query, results, highlighted, pick, dismiss, handleKey };
}
