// @vitest-environment node
/**
 * Convention gate: every row in the expanded sidebar shares one geometry, and
 * only the nav owns a row style.
 *
 * ## The bug this locks out
 *
 * The sidebar column is drawn by two components - `TSidebarNav` (nav entries)
 * and `TThreadList` (conversation history) - whose rows must line up because
 * they are stacked in the same 300px column. That agreement was maintained by
 * hand, and `TThreadList` says so in its own header comment ("Row geometry
 * deliberately matches TSidebarNav's items ... so every line in the expanded
 * sidebar aligns").
 *
 * There used to be a THIRD copy: `TChatApp` hand-wrote the "New chat" button
 * with `.t-chat-app__nav-item`, pixel-matched to `.t-sidebar-nav__item`. It had
 * drifted - the row started at x=0 instead of the nav's 8px column inset, so
 * its icon and label sat 8px left of every entry below it, and it carried a
 * `font-weight: 500` nothing else had. A user reported it as "New chat sticks
 * out; it isn't aligned". That copy is gone: New chat is now a `NavItem`
 * rendered by `TSidebarNav` like everything else.
 *
 * `TThreadList` had quietly drifted too, by 2px - a 10px column inset against
 * the nav's 8px, putting thread rows and the "All tasks" heading right of the
 * nav above them while the comment claimed they aligned.
 *
 * None of this could turn a test red: every component was internally
 * consistent, rendered fine, and had correct markup. Only stacking them in one
 * column and measuring shows it. So this file compares the declared values
 * across files instead, which is the part a unit test can actually see.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

const read = (rel: string) => readFileSync(resolve(pkgRoot, rel), 'utf8');

/* Comments are stripped before any parsing: a `/* ... *\/` sitting between two
   declarations otherwise hides the one after it from a "preceded by ; or start
   of block" match, and these rules are commented. (Found the hard way - the
   first version of this file failed on the very declaration it was added to
   protect, because the fix above it carried an explanation.) */
const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '');

/* Only the `<style>` block is CSS. Parsing the whole SFC also means the first
   rule in the block is preceded by markup rather than by the `}` the rule
   matcher anchors on. */
function styleBlock(sfc: string): string {
  const match = /<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc);
  if (!match) throw new Error('no <style> block');
  return stripComments(match[1]);
}

const NAV = 'src/components/layout/TSidebarNav.vue';
const THREADS = 'src/components/chat/TThreadList.vue';
const CHAT_APP = 'src/components/chat/TChatApp.vue';

/**
 * Body of the rule whose selector is exactly `selector`.
 *
 * Anchored on the end of the previous block (or the start of the stylesheet),
 * so `.t-thread-list__icon` matches its own rule and not the compound
 * `.t-thread-list__row.is-active .t-thread-list__icon` that appears earlier.
 */
function ruleBody(sfc: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(
    styleBlock(sfc),
  );
  if (!match) throw new Error(`no rule with selector exactly "${selector}"`);
  return match[1];
}

function declaration(css: string, selector: string, property: string): string {
  const match = new RegExp(`(?:^|;)\\s*${property}\\s*:\\s*([^;]+)`).exec(
    ruleBody(css, selector),
  );
  if (!match) throw new Error(`"${property}" not declared on "${selector}"`);
  return match[1].trim();
}

describe('sidebar row geometry', () => {
  const nav = read(NAV);
  const threads = read(THREADS);

  /* The metrics that make two stacked rows read as one list. Height and
     padding set where the text lands; gap and the icon box set where it lands
     horizontally; radius sets the shape of the hover/active pill. */
  const SHARED: ReadonlyArray<string> = ['height', 'padding', 'gap', 'border-radius'];

  it.each(SHARED)('nav rows and thread rows agree on %s', (property) => {
    expect(declaration(threads, '.t-thread-list__row', property)).toBe(
      declaration(nav, '.t-sidebar-nav__item', property),
    );
  });

  it('nav and thread columns share one inset, so their rows start at the same x', () => {
    /* Shorthand padding: the horizontal value is what insets the column.
       `0 8px` -> "8px"; `14px 8px 4px` -> "8px". */
    const horizontal = (padding: string): string => {
      const parts = padding.split(/\s+/);
      return parts.length === 1 ? parts[0] : parts[1];
    };

    expect(horizontal(declaration(threads, '.t-thread-list', 'padding'))).toBe(
      horizontal(declaration(nav, '.t-sidebar-nav__group', 'padding')),
    );
  });

  it('icon boxes match, so labels start at the same x', () => {
    for (const property of ['width', 'height'] as const) {
      expect(declaration(threads, '.t-thread-list__icon', property)).toBe(
        declaration(nav, '.t-sidebar-nav__item-icon', property),
      );
    }
  });

  it('TChatApp declares no row style of its own', () => {
    /* The removed copy was `.t-chat-app__nav-item` / `.t-chat-app__new-chat` /
       `.t-chat-app__nav-icon`. A shell that assembles regions should not also
       draw one; if a row style reappears here, a fourth copy is being born. */
    const offenders = [
      ...read(CHAT_APP).matchAll(/^\s*(\.t-chat-app__(?:nav|new-chat)[\w-]*)\s*[,{]/gm),
    ].map((m) => m[1]);

    expect(offenders).toEqual([]);
  });

  it('New chat is a nav item, not bespoke markup', () => {
    const chatApp = read(CHAT_APP);
    /* It must reach TSidebarNav as data. The rail gets it the same way, which
       is why neither surface needs a special case. */
    expect(chatApp).toMatch(/NEW_CHAT_NAV_ID/);
    expect(chatApp).not.toMatch(/class="t-chat-app__new-chat"/);
  });

  it('the primary nav is pinned outside the scroller', () => {
    /* New chat and the product's destinations must stay reachable however long
       the history gets. `TCollapsibleSidebar` renders `#nav` above the scroll
       area and `#content` inside it, so this only holds while TChatApp fills
       BOTH: dropping the `#nav` template puts the primary run back into the
       scroller, which looks fine until a user with a long history scrolls. */
    const chatApp = read(CHAT_APP);
    expect(chatApp).toMatch(/<template\s+v-if="hasPinnedNav"\s+#nav>/);
    expect(chatApp).toMatch(/:groups="pinnedNavGroups"/);

    const sidebar = read('src/components/layout/TCollapsibleSidebar.vue');
    expect(sidebar).toMatch(/name="nav"/);
    /* The seam only appears once something has scrolled under it. */
    expect(sidebar).toMatch(/is-detached/);
  });

  it('the new-chat entry takes its icon and label from props', () => {
    /* Both are what a consuming product renames first. A hardcoded glyph is
       easy to reintroduce because the default has to live somewhere. */
    const chatApp = read(CHAT_APP);
    expect(chatApp).toMatch(/newChatIcon\?: string/);
    expect(chatApp).toMatch(/icon: props\.newChatIcon/);
    expect(chatApp).toMatch(/label: newChatText\.value/);
  });

  it('the shell theme provider renders no element', () => {
    /* `NConfigProvider` renders a `<div class="n-config-provider">` unless it
       is `abstract`. Wrapping the shell in one broke the height chain: the div
       is `display:block` with no height, so `.t-chat-app` resolved to its
       content - measured 1467px inside a 711px viewport, composer and account
       bar pushed off screen. The theme reaches descendants either way, because
       provide/inject follows the component tree rather than the DOM, so
       `abstract` costs nothing and is the only correct form here. */
    const chatApp = read(CHAT_APP);
    expect(chatApp).toMatch(/<NConfigProvider\s+abstract\b/);
  });

  it('wired settings sections are hidden when nothing can fill them', () => {
    /* Security and Personalization render only against `accountClient` (or a
       consumer slot). Listed unconditionally they open an EMPTY pane, which
       reads as a broken page rather than a capability this deployment lacks -
       and an empty pane is exactly what a settings dialog should never show.
       The filter is easy to drop back to `computed(() => props.settingsSections)`
       while every other test stays green. */
    const chatApp = read(CHAT_APP);
    expect(chatApp).toMatch(/WIRED_SETTINGS_IDS/);
    /* Gated on the client OR a consumer slot - dropping the slot half would
       hide a section the consumer had filled themselves. */
    expect(chatApp).toMatch(/accountSettings !== null \|\| Boolean\(slots\[/);
  });

  it('every sidebar entry point routes through the shared handlers', () => {
    /* `view` is a v-model, so the shell owns routing. The failure mode is
       partial adoption: one surface routes and another emits raw, which shows
       up as "the rail works but the sidebar doesn't" (or the reverse) - two
       surfaces render the same entries, so a raw emit is easy to leave behind.
       `startNewChat` / `openThread` / `onNavSelect` are the only places
       allowed to raise these events. */
    const body = read(CHAT_APP);
    const handlers =
      /function (?:startNewChat|openThread|onNavSelect)\b[\s\S]*?\n}/g;
    const outsideHandlers = body.replace(handlers, '');

    expect(outsideHandlers).not.toMatch(/emit\(\s*'new-chat'/);
    expect(outsideHandlers).not.toMatch(/emit\(\s*'select-thread'/);
  });
});
