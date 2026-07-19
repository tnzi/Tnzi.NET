import { describe, it, expect } from 'vitest'
import { createAuditHumanizer } from '../../src/headless/createAuditHumanizer'
import { EntityChangeType, type AuditEntityEntryDto } from '@tnzi/core/services/audit'

describe('createAuditHumanizer', () => {
  const h = createAuditHumanizer()

  it('derives the action from functionName', () => {
    expect(h.action('Identity.Update').label).toBe('Updated')
    expect(h.action('Client.CreateAsync').label).toBe('Created')
    expect(h.action('Matter.DeleteAsync').tone).toBe('error')
  })

  it('derives the action from operationType when functionName is absent', () => {
    expect(h.action(null, EntityChangeType.Added).label).toBe('Created')
    expect(h.action(null, EntityChangeType.Modified).label).toBe('Updated')
  })

  it('humanises an unknown function-name suffix', () => {
    expect(h.action('Foo.SomethingWeird').label).toBe('Something Weird')
  })

  it('friendly entity label (falls back to spaced CLR name; override wins)', () => {
    expect(h.entity('StaffProfile')).toBe('Staff Profile')
    const h2 = createAuditHumanizer({ entityLabels: { StaffProfile: 'Staff member' } })
    expect(h2.entity('StaffProfile')).toBe('Staff member')
  })

  it('formats values: empty → em-dash, booleans, shortened GUID, plain passthrough', () => {
    expect(h.value('')).toBe('—')
    expect(h.value(null)).toBe('—')
    expect(h.value('true')).toBe('Yes')
    expect(h.value('false')).toBe('No')
    expect(h.value('3f2504e0-4f89-41d3-9a0c-0305e82c3301')).toBe('3f2504e0…')
    expect(h.value('plain text')).toBe('plain text')
  })

  it('hides bookkeeping columns from property entries', () => {
    const entry = {
      id: 'e',
      operationType: EntityChangeType.Modified,
      creationTime: '',
      propertyEntries: [
        { id: '1', propertyName: 'Name', originalValue: 'a', newValue: 'b' },
        { id: '2', propertyName: 'ConcurrencyStamp', originalValue: 'x', newValue: 'y' },
        { id: '3', propertyName: 'LastModificationTime', originalValue: null, newValue: '2026-01-01T00:00:00Z' },
      ],
    } as unknown as AuditEntityEntryDto
    expect(h.visibleProps(entry).map((p) => p.propertyName)).toEqual(['Name'])
    expect(h.isHidden('ConcurrencyStamp')).toBe(true)
    expect(h.isHidden('Name')).toBe(false)
  })

  it('formatValue override takes precedence, then falls through', () => {
    const h3 = createAuditHumanizer({ formatValue: (raw) => (raw === 'X' ? 'custom' : undefined) })
    expect(h3.value('X')).toBe('custom')
    expect(h3.value('')).toBe('—')
  })
})
