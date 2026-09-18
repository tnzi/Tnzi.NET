import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

// The profile endpoint binds UpdateProfileDto, which deliberately has no
// roleIds / organizationId / email / phoneNumber (see core services/identity/types.ts).
// Typing a self-service call as the admin UpdateUserDto makes those fields
// expressible again: the call typechecks, the server drops them, the store
// overwrites the user with the unchanged profile and the app reports saved.
// UpdateUserDto belongs to admin user management only.

const SRC = join(import.meta.dirname, '../src')

function walk(dir: string, out: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) walk(full, out)
    else if (/\.(ts|vue)$/.test(name)) out.push(full)
  }
  return out
}

describe('profile updates use UpdateProfileDto', () => {
  it('no mobile source imports the admin UpdateUserDto', () => {
    const offenders = walk(SRC)
      .filter((file) => /\bUpdateUserDto\b/.test(readFileSync(file, 'utf8')))
      .map((file) => relative(SRC, file))
    expect(offenders).toEqual([])
  })
})
