import { afterEach, describe, expect, it, vi } from 'vitest';
import { mockFetch } from '../test/utils';
import { ApiError, api } from './client';

afterEach(() => vi.unstubAllGlobals());

describe('api client', () => {
  it('builds the query string from the filters that are set', async () => {
    const fetchMock = mockFetch(200, { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });

    await api.searchClaims({ status: 'Approved', search: 'water damage', policyNumber: '', page: 2 });

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/claims?status=Approved&search=water+damage&page=2',
      expect.anything(),
    );
  });

  it('turns a ProblemDetails response into an ApiError with field errors', async () => {
    mockFetch(400, {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { ClaimedAmount: ['Too high.'] },
    });

    const error = await api
      .createClaim({
        policyNumber: 'DEMO-POL-1001',
        category: 'Motor',
        incidentDate: '2026-11-01',
        claimedAmount: 1,
        description: 'Long enough text',
      })
      .catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    const apiError = error as ApiError;
    expect(apiError.status).toBe(400);
    expect(apiError.errorsFor('claimedAmount')).toEqual(['Too high.']);
    expect(apiError.errorsFor('description')).toEqual([]);
  });

  it('reports network failures as status 0', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));
    await expect(api.getPolicies()).rejects.toMatchObject({ status: 0, title: 'Network error' });
  });
});
