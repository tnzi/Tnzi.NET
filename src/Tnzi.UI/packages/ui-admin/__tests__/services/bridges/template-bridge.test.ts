import { describe, it, expect, vi } from 'vitest'
import { createTemplateBridge } from '../../../src/services/bridges/template-bridge'

// ---------------------------------------------------------------------------
// Mock factory helpers
// ---------------------------------------------------------------------------

function mockAdminTemplateApi() {
  return {
    getList: vi.fn(async () => ({
      items: [
        {
          id: 't1',
          templateName: 'welcome-email',
          module: 'Identity',
          category: 'user',
          defaultLayoutName: 'default-layout',
          isActive: true,
          description: 'Welcome email template',
          creationTime: '2026-01-01T00:00:00Z',
          lastModificationTime: null,
        },
      ],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
    })),
    getById: vi.fn(async () => ({
      id: 't1',
      templateName: 'welcome-email',
      module: 'Identity',
      category: 'user',
      subjectTemplate: 'Welcome {{name}}',
      contentTemplate: '<p>Hello {{name}}</p>',
      defaultLayoutName: 'default-layout',
      isActive: true,
      description: null,
      metadata: null,
      creationTime: '2026-01-01T00:00:00Z',
      lastModificationTime: null,
    })),
    getByName: vi.fn(async () => ({ id: 't1', templateName: 'welcome-email' })),
    create: vi.fn(async () => ({ id: 't2', templateName: 'new-template' })),
    update: vi.fn(async () => ({ id: 't1', templateName: 'updated-template' })),
    delete: vi.fn(async () => undefined),
    deleteBatch: vi.fn(async () => undefined),
    validate: vi.fn(async () => ({ isValid: true, errors: [] })),
    clone: vi.fn(async () => ({ id: 't3', templateName: 'welcome-email-copy' })),
    export: vi.fn(async () => '{}'),
    import: vi.fn(async () => ({ createdCount: 1, updatedCount: 0, skippedCount: 0, errors: [] })),
    getVariables: vi.fn(async () => ({
      templateId: 't1',
      templateName: 'welcome-email',
      subjectVariables: ['name'],
      contentVariables: ['name'],
      allVariables: ['name'],
    })),
    batchActivate: vi.fn(async () => 1),
    preview: vi.fn(async () => '<p>Hello John</p>'),
  }
}

function mockAdminLayoutApi() {
  return {
    getList: vi.fn(async () => ({
      items: [
        {
          id: 'l1',
          layoutName: 'default-layout',
          module: 'Identity',
          category: 'user',
          isActive: true,
          isDefault: true,
          description: 'Default email layout',
          creationTime: '2026-01-01T00:00:00Z',
          lastModificationTime: null,
        },
      ],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
    })),
    getById: vi.fn(async () => ({
      id: 'l1',
      layoutName: 'default-layout',
      module: 'Identity',
      category: 'user',
      layoutContent: '<html>{{body}}</html>',
      isActive: true,
      isDefault: true,
      description: null,
      metadata: null,
      creationTime: '2026-01-01T00:00:00Z',
      lastModificationTime: null,
    })),
    getByName: vi.fn(async () => ({ id: 'l1', layoutName: 'default-layout' })),
    getDefault: vi.fn(async () => ({ id: 'l1', layoutName: 'default-layout' })),
    create: vi.fn(async () => ({ id: 'l2', layoutName: 'new-layout' })),
    update: vi.fn(async () => ({ id: 'l1', layoutName: 'updated-layout' })),
    delete: vi.fn(async () => undefined),
    deleteBatch: vi.fn(async () => undefined),
    clone: vi.fn(async () => ({ id: 'l3', layoutName: 'default-layout-copy' })),
    export: vi.fn(async () => '{}'),
    import: vi.fn(async () => ({ createdCount: 1, updatedCount: 0, skippedCount: 0, errors: [] })),
    batchActivate: vi.fn(async () => 1),
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('template-bridge', () => {
  it('exposes templates / layouts sub-contracts', () => {
    const bridge = createTemplateBridge({
      adminTemplateApi: mockAdminTemplateApi() as never,
      adminLayoutApi: mockAdminLayoutApi() as never,
    })
    expect(typeof bridge.templates.fetch).toBe('function')
    expect(typeof bridge.layouts.fetch).toBe('function')
  })

  // ---- templates sub-contract ----

  it('templates.fetch calls adminTemplateApi.getList and returns paged items', async () => {
    const adminTemplateApi = mockAdminTemplateApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: adminTemplateApi as never,
      adminLayoutApi: mockAdminLayoutApi() as never,
    })
    const result = await bridge.templates.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(adminTemplateApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
    expect(result.totalCount).toBe(1)
    expect(result.items[0].templateName).toBe('welcome-email')
  })

  it('templates.create calls adminTemplateApi.create', async () => {
    const adminTemplateApi = mockAdminTemplateApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: adminTemplateApi as never,
      adminLayoutApi: mockAdminLayoutApi() as never,
    })
    const result = await bridge.templates.create({ templateName: 'new-template' } as never)
    expect(adminTemplateApi.create).toHaveBeenCalled()
    expect((result as { templateName: string }).templateName).toBe('new-template')
  })

  it('templates.update calls adminTemplateApi.update', async () => {
    const adminTemplateApi = mockAdminTemplateApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: adminTemplateApi as never,
      adminLayoutApi: mockAdminLayoutApi() as never,
    })
    await bridge.templates.update('t1', { templateName: 'updated-template' } as never)
    expect(adminTemplateApi.update).toHaveBeenCalledWith('t1', expect.anything())
  })

  it('templates.delete single calls adminTemplateApi.delete', async () => {
    const adminTemplateApi = mockAdminTemplateApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: adminTemplateApi as never,
      adminLayoutApi: mockAdminLayoutApi() as never,
    })
    await bridge.templates.delete(['t1'])
    expect(adminTemplateApi.delete).toHaveBeenCalledWith('t1')
    expect(adminTemplateApi.deleteBatch).not.toHaveBeenCalled()
  })

  it('templates.delete batch calls adminTemplateApi.deleteBatch', async () => {
    const adminTemplateApi = mockAdminTemplateApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: adminTemplateApi as never,
      adminLayoutApi: mockAdminLayoutApi() as never,
    })
    await bridge.templates.delete(['t1', 't2'])
    expect(adminTemplateApi.deleteBatch).toHaveBeenCalledWith(['t1', 't2'])
  })

  it('templates.render fetches template then calls preview', async () => {
    const adminTemplateApi = mockAdminTemplateApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: adminTemplateApi as never,
      adminLayoutApi: mockAdminLayoutApi() as never,
    })
    const rendered = await bridge.templates.render('t1', { name: 'John' })
    expect(adminTemplateApi.getById).toHaveBeenCalledWith('t1')
    expect(adminTemplateApi.preview).toHaveBeenCalledWith(
      expect.objectContaining({ content: '<p>Hello {{name}}</p>', model: { name: 'John' } }),
    )
    expect(rendered).toBe('<p>Hello John</p>')
  })

  it('templates.clone fetches template then clones with auto-generated name', async () => {
    const adminTemplateApi = mockAdminTemplateApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: adminTemplateApi as never,
      adminLayoutApi: mockAdminLayoutApi() as never,
    })
    const cloned = await bridge.templates.clone('t1')
    expect(adminTemplateApi.getById).toHaveBeenCalledWith('t1')
    expect(adminTemplateApi.clone).toHaveBeenCalledWith('t1', 'welcome-email-copy')
    expect((cloned as { templateName: string }).templateName).toBe('welcome-email-copy')
  })

  // ---- layouts sub-contract ----

  it('layouts.fetch calls adminLayoutApi.getList and returns paged items', async () => {
    const adminLayoutApi = mockAdminLayoutApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: mockAdminTemplateApi() as never,
      adminLayoutApi: adminLayoutApi as never,
    })
    const result = await bridge.layouts.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(adminLayoutApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
    expect(result.totalCount).toBe(1)
    expect(result.items[0].layoutName).toBe('default-layout')
  })

  it('layouts.create calls adminLayoutApi.create', async () => {
    const adminLayoutApi = mockAdminLayoutApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: mockAdminTemplateApi() as never,
      adminLayoutApi: adminLayoutApi as never,
    })
    await bridge.layouts.create({ layoutName: 'new-layout' } as never)
    expect(adminLayoutApi.create).toHaveBeenCalled()
  })

  it('layouts.delete single calls adminLayoutApi.delete', async () => {
    const adminLayoutApi = mockAdminLayoutApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: mockAdminTemplateApi() as never,
      adminLayoutApi: adminLayoutApi as never,
    })
    await bridge.layouts.delete(['l1'])
    expect(adminLayoutApi.delete).toHaveBeenCalledWith('l1')
    expect(adminLayoutApi.deleteBatch).not.toHaveBeenCalled()
  })

  it('layouts.delete batch calls adminLayoutApi.deleteBatch', async () => {
    const adminLayoutApi = mockAdminLayoutApi()
    const bridge = createTemplateBridge({
      adminTemplateApi: mockAdminTemplateApi() as never,
      adminLayoutApi: adminLayoutApi as never,
    })
    await bridge.layouts.delete(['l1', 'l2'])
    expect(adminLayoutApi.deleteBatch).toHaveBeenCalledWith(['l1', 'l2'])
  })

  // ---- no-deps fallback ----

  it('bridge with no deps returns stub contracts that reject on call', async () => {
    const bridge = createTemplateBridge()
    await expect(bridge.templates.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow()
    await expect(bridge.layouts.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })).rejects.toThrow()
    await expect(bridge.templates.render('t1', {})).rejects.toThrow()
    await expect(bridge.templates.clone('t1')).rejects.toThrow()
  })
})
