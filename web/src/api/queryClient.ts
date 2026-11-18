import { QueryClient } from '@tanstack/react-query';
import { ApiError } from './client';

/** Retries after a failed read: at most two (three attempts in all). */
export const MAX_QUERY_RETRIES = 2;

/**
 * Retry reads that may succeed on a second try: network errors and 5xx responses, e.g. while the demo's
 * serverless database resumes from a pause (the server also retries the database for about a minute).
 * Never retry 4xx responses: validation errors, "not found" and rate limits will not change by retrying.
 */
export function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  if (failureCount >= MAX_QUERY_RETRIES) {
    return false;
  }

  return !(error instanceof ApiError) || error.status === 0 || error.status >= 500;
}

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15_000,
        refetchOnWindowFocus: false,
        retry: shouldRetryQuery,
      },
    },
  });
}
