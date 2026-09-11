import { describe, it, expect, vi } from 'vitest'
import {
  useAdminFeatureDefinitionApi,
  useAdminFeatureUsageApi,
  useAdminFeatureValueApi,
} from '../../src/services/feature/api'
import { FeatureValueType } from '../../src/services/feature/types'

function mockClient() {
  return {
    get: vi.fn(async () => ({ success: true, code: 200, data: [] })),
    post: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    put: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
  }
}

describe('useAdminFeatureDefinitionApi', () => {
  it('getAll hits GET /admin/feature-definitions', async () => {
    const c = mockClient(); const api = useAdminFeatureDefinitionApi(c as never)
    await api.getAll()
    expect(c.get).toHaveBeenCalledWith('/admin/feature-definitions')
  })
  it('create posts the body', async () => {
    const c = mockClient(); const api = useAdminFeatureDefinitionApi(c as never)
    const body = { name: 'Orders.AdvancedSearch', valueType: FeatureValueType.Boolean }
    await api.create(body)
    expect(c.post).toHaveBeenCalledWith('/admin/feature-definitions', body)
  })
  it('update / delete address the id and encode it', async () => {
    const c = mockClient(); const api = useAdminFeatureDefinitionApi(c as never)
    await api.update('a/b', { valueType: FeatureValueType.String })
    expect(c.put).toHaveBeenCalledWith('/admin/feature-definitions/a%2Fb', { valueType: 'String' })
    await api.delete('a/b')
    expect(c.delete).toHaveBeenCalledWith('/admin/feature-definitions/a%2Fb')
  })
})

describe('useAdminFeatureValueApi', () => {
  it('getProviders hits GET /admin/feature-values/providers', async () => {
    const c = mockClient(); const api = useAdminFeatureValueApi(c as never)
    await api.getProviders()
    expect(c.get).toHaveBeenCalledWith('/admin/feature-values/providers')
  })
  it('getAll sends providerName and omits an absent key', async () => {
    const c = mockClient(); const api = useAdminFeatureValueApi(c as never)
    await api.getAll('Global')
    expect(c.get).toHaveBeenCalledWith('/admin/feature-values/all?providerName=Global')
  })
  it('getAll / getValues carry the provider key when given', async () => {
    const c = mockClient(); const api = useAdminFeatureValueApi(c as never)
    await api.getAll('Tenant', 't-1')
    expect(c.get).toHaveBeenCalledWith('/admin/feature-values/all?providerName=Tenant&providerKey=t-1')
    await api.getValues('Tenant', 't 1')
    // Keys are user input; the query string must stay well-formed.
    expect(c.get).toHaveBeenCalledWith('/admin/feature-values?providerName=Tenant&providerKey=t+1')
  })
  it('set / batchSet post to the value endpoints', async () => {
    const c = mockClient(); const api = useAdminFeatureValueApi(c as never)
    const single = { featureDefinitionId: 'd1', providerName: 'Global', value: 'true' }
    await api.set(single)
    expect(c.post).toHaveBeenCalledWith('/admin/feature-values', single)
    const batch = { providerName: 'Global', values: [{ featureDefinitionId: 'd1', value: 'true' }] }
    await api.batchSet(batch)
    expect(c.post).toHaveBeenCalledWith('/admin/feature-values/batch', batch)
  })
  it('delete addresses the value row id', async () => {
    const c = mockClient(); const api = useAdminFeatureValueApi(c as never)
    await api.delete('v1')
    expect(c.delete).toHaveBeenCalledWith('/admin/feature-values/v1')
  })
})

describe('useAdminFeatureUsageApi', () => {
  it('getStats sends featureName and the optional range', async () => {
    const c = mockClient(); const api = useAdminFeatureUsageApi(c as never)
    await api.getStats('F.A')
    expect(c.get).toHaveBeenCalledWith('/admin/feature/usage/stats?featureName=F.A')
    await api.getStats('F.A', { from: '2026-01-01T00:00:00.000Z', to: null })
    expect(c.get).toHaveBeenLastCalledWith(
      '/admin/feature/usage/stats?featureName=F.A&from=2026-01-01T00%3A00%3A00.000Z',
    )
  })
  it('getTrend defaults to daily and sends the period', async () => {
    const c = mockClient(); const api = useAdminFeatureUsageApi(c as never)
    await api.getTrend('F.A')
    expect(c.get).toHaveBeenCalledWith('/admin/feature/usage/trend?featureName=F.A&period=daily')
    await api.getTrend('F.A', 'weekly')
    expect(c.get).toHaveBeenLastCalledWith('/admin/feature/usage/trend?featureName=F.A&period=weekly')
  })
  it('getMostUsed defaults to top 10', async () => {
    const c = mockClient(); const api = useAdminFeatureUsageApi(c as never)
    await api.getMostUsed()
    expect(c.get).toHaveBeenCalledWith('/admin/feature/usage/most-used?top=10')
  })
  it('cleanup deletes with the retention window', async () => {
    const c = mockClient(); const api = useAdminFeatureUsageApi(c as never)
    await api.cleanup(30)
    expect(c.delete).toHaveBeenCalledWith('/admin/feature/usage/cleanup?retentionDays=30')
  })
})
