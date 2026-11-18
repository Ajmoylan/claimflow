import { describe, expect, it } from 'vitest';
import { ApiError } from './client';
import { MAX_QUERY_RETRIES, shouldRetryQuery } from './queryClient';

describe('shouldRetryQuery', () => {
  it('retries server errors and network failures, such as a database that is still waking up', () => {
    expect(shouldRetryQuery(0, new ApiError(500, 'An unexpected error occurred'))).toBe(true);
    expect(shouldRetryQuery(1, new ApiError(503, 'Service Unavailable'))).toBe(true);
    expect(shouldRetryQuery(0, new ApiError(0, 'Network error'))).toBe(true);
    expect(shouldRetryQuery(0, new TypeError('Failed to fetch'))).toBe(true);
  });

  it('gives up after two retries', () => {
    expect(shouldRetryQuery(MAX_QUERY_RETRIES, new ApiError(500, 'An unexpected error occurred'))).toBe(
      false,
    );
  });

  it('never retries client errors, including the rate limit', () => {
    expect(shouldRetryQuery(0, new ApiError(400, 'Bad request'))).toBe(false);
    expect(shouldRetryQuery(0, new ApiError(404, 'Claim not found'))).toBe(false);
    expect(shouldRetryQuery(0, new ApiError(429, 'Too many requests'))).toBe(false);
  });
});
