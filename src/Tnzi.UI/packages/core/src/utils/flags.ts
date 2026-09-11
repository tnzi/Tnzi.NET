/**
 * @tnzi/core/utils/flags
 *
 * Reading a `[Flags]` enum off the wire.
 *
 * ★ The backend registers a global `JsonStringEnumConverter`, so **every** enum
 * arrives as its member name, and a `[Flags]` enum with more than one bit set
 * arrives as a comma-separated list: `"ChangePassword, EnrollTotp"`. A value that
 * happens to equal a named composite arrives as that single name instead
 * (`"Obligations"`), and a value with bits the server has no name for arrives as
 * a plain number string.
 *
 * ★★ Why this module exists rather than living in each consumer: the natural
 * thing to write is `(dto.flags & SomeFlag) !== 0`, which is what the C# side
 * looks like and what a numeric TypeScript enum invites. Against a string it
 * coerces to `NaN`, and `NaN & anything` is `0` - so the test does not throw, does
 * not warn, and answers "no bits set" for every input. The failure is a feature
 * that silently never triggers. It was live in this framework's own admin user
 * list: invited accounts showed as "Locked" because the invitation badge could
 * never match.
 */

/**
 * A `[Flags]` enum as it actually arrives: the member name(s), or a number when a
 * caller or an older deployment sends the raw value.
 *
 * Declaring a DTO field as `Flags<MyEnum>` rather than `MyEnum` is the point - it
 * makes `&` a compile error instead of a silent zero.
 */
export type Flags<E extends number> = E | string;

/** The shape of a TypeScript numeric enum object, which carries reverse mappings. */
type FlagMembers = Record<string, string | number>;

function lookup(members: FlagMembers, name: string): number | null {
  const direct = members[name];
  if (typeof direct === 'number') return direct;

  // Tolerate a case difference: query strings and hand-written config drift, and
  // a flag that silently fails to match is the exact defect this module is for.
  const hit = Object.keys(members).find(
    (k) => typeof members[k] === 'number' && k.toLowerCase() === name.toLowerCase()
  );
  return hit === undefined ? null : (members[hit] as number);
}

/**
 * Turn a wire value into a bit mask.
 *
 * Accepts a number, a single member name, a comma-separated list (spaces
 * optional), a numeric string, and null/undefined (`0`).
 *
 * @remarks
 * ★ Names the enum does not know are **ignored**, not treated as an error. A
 * server that grows a new flag would otherwise break every older client at once,
 * and this is display-facing code. When "it owes something I cannot name" has to
 * be distinguished from "it owes nothing" - a gate rather than a badge - compare
 * {@link flagNames} against the names you got back and fail closed on the
 * difference.
 */
export function parseFlags(value: Flags<number> | null | undefined, members: FlagMembers): number {
  if (value === null || value === undefined) return 0;
  if (typeof value === 'number') return Number.isFinite(value) ? value : 0;

  let mask = 0;
  for (const raw of value.split(',')) {
    const name = raw.trim();
    if (name === '') continue;

    const known = lookup(members, name);
    if (known !== null) {
      mask |= known;
      continue;
    }
    // A bare number is legitimate: that is how the server spells bits it has no
    // name for, and how a caller may spell the value by hand.
    const numeric = Number(name);
    if (Number.isInteger(numeric)) mask |= numeric;
  }
  return mask;
}

/**
 * Is `flag` set in `value`? The replacement for `(value & flag) !== 0`.
 *
 * `flag` may itself be a composite (a member defined as `A | B`), in which case
 * this answers "any of those bits", matching the C# `HasFlag`-style intent used
 * across this codebase rather than `Enum.HasFlag`'s all-bits semantics.
 */
export function hasFlag(
  value: Flags<number> | null | undefined,
  flag: number,
  members: FlagMembers
): boolean {
  if (flag === 0) return parseFlags(value, members) === 0;
  return (parseFlags(value, members) & flag) !== 0;
}

/**
 * The member names set in `mask`, largest first, skipping the zero member and
 * composites already covered by the bits they contain.
 */
export function flagNames(mask: number, members: FlagMembers): string[] {
  const named = Object.keys(members)
    .filter((k) => typeof members[k] === 'number' && (members[k] as number) !== 0)
    .map((k) => ({ name: k, value: members[k] as number }))
    .sort((a, b) => b.value - a.value);

  const out: string[] = [];
  let remaining = mask;
  for (const { name, value } of named) {
    if (remaining !== 0 && (remaining & value) === value) {
      out.push(name);
      remaining &= ~value;
    }
  }
  return out;
}

/**
 * Render a mask back into the wire spelling (`"A, B"`), or the zero member's name
 * when nothing is set. Use it when sending a flags value back to the server.
 */
export function formatFlags(mask: number, members: FlagMembers): string {
  const names = flagNames(mask, members);
  if (names.length > 0) return names.join(', ');

  const zero = Object.keys(members).find((k) => members[k] === 0);
  return zero ?? '0';
}
