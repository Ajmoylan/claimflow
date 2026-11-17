import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api } from '../api/client';
import {
  CLAIM_CATEGORIES,
  CLAIM_SORTS,
  CLAIM_STATUSES,
  type ClaimCategory,
  type ClaimQuery,
  type ClaimSort,
  type ClaimStatus,
} from '../api/types';
import { ClaimsTable } from '../components/ClaimsTable';
import { ErrorMessage, Loading, Pagination } from '../components/ui';
import { categoryLabel, statusLabel } from '../format';

const PAGE_SIZE = 15;

const sortLabels: Record<ClaimSort, string> = {
  Newest: 'Newest first',
  Oldest: 'Oldest first',
  IncidentDate: 'Incident date',
  AmountHighest: 'Highest amount',
};

const pick = <T extends string>(value: string | null, allowed: readonly T[]): T | undefined =>
  allowed.includes(value as T) ? (value as T) : undefined;

/** Reads the filters from the URL, so filtered views can be bookmarked, shared and navigated with Back. */
function useClaimQuery(): [ClaimQuery, (next: ClaimQuery) => void] {
  const [params, setParams] = useSearchParams();
  const query: ClaimQuery = {
    search: params.get('search') ?? undefined,
    status: pick<ClaimStatus>(params.get('status'), CLAIM_STATUSES),
    category: pick<ClaimCategory>(params.get('category'), CLAIM_CATEGORIES),
    policyNumber: params.get('policyNumber') ?? undefined,
    incidentFrom: params.get('incidentFrom') ?? undefined,
    incidentTo: params.get('incidentTo') ?? undefined,
    sort: pick<ClaimSort>(params.get('sort'), CLAIM_SORTS) ?? 'Newest',
    page: Math.max(1, Number(params.get('page')) || 1),
    pageSize: PAGE_SIZE,
  };

  const update = (next: ClaimQuery) => {
    const out = new URLSearchParams();
    for (const [key, value] of Object.entries(next)) {
      if (value !== undefined && value !== '' && key !== 'pageSize' && !(key === 'page' && value === 1)) {
        out.set(key, String(value));
      }
    }
    setParams(out);
  };

  return [query, update];
}

export function ClaimsPage() {
  const [query, setQuery] = useClaimQuery();
  const claims = useQuery({
    queryKey: ['claims', query],
    queryFn: () => api.searchClaims(query),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Claims</h1>
          <p className="muted">Search by reference, policy number or description, and filter by any field.</p>
        </div>
        <Link to="/claims/new" className="button">
          New claim
        </Link>
      </div>

      {/* Re-mount the form when the URL changes so its inputs always show the active filters. */}
      <Filters key={JSON.stringify(query)} query={query} onApply={setQuery} />

      <section className="card" aria-live="polite">
        {claims.isPending && <Loading />}
        {claims.isError && <ErrorMessage error={claims.error} onRetry={() => claims.refetch()} />}
        {claims.data && (
          <>
            <p className="result-count">
              {claims.data.totalCount} {claims.data.totalCount === 1 ? 'claim' : 'claims'}
              {claims.isFetching && ' · updating…'}
            </p>
            <ClaimsTable claims={claims.data.items} caption="Claims matching the filters" />
            <Pagination
              page={claims.data.page}
              totalPages={claims.data.totalPages}
              onChange={(page) => setQuery({ ...query, page })}
            />
          </>
        )}
      </section>
    </>
  );
}

function Filters({ query, onApply }: { query: ClaimQuery; onApply: (q: ClaimQuery) => void }) {
  const [draft, setDraft] = useState<ClaimQuery>(query);
  const set = (patch: Partial<ClaimQuery>) => setDraft((current) => ({ ...current, ...patch }));

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onApply({ ...draft, page: 1 });
  };

  return (
    <form className="card filters" onSubmit={submit} role="search" aria-label="Filter claims">
      <div className="field field-wide">
        <label htmlFor="search">Search</label>
        <input
          id="search"
          type="search"
          maxLength={100}
          placeholder="e.g. CLM-2026-D00007, DEMO-POL-1002 or water"
          value={draft.search ?? ''}
          onChange={(e) => set({ search: e.target.value || undefined })}
        />
      </div>
      <div className="field">
        <label htmlFor="status">Status</label>
        <select
          id="status"
          value={draft.status ?? ''}
          onChange={(e) => set({ status: (e.target.value || undefined) as ClaimStatus | undefined })}
        >
          <option value="">Any status</option>
          {CLAIM_STATUSES.map((s) => (
            <option key={s} value={s}>
              {statusLabel(s)}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor="category">Category</label>
        <select
          id="category"
          value={draft.category ?? ''}
          onChange={(e) => set({ category: (e.target.value || undefined) as ClaimCategory | undefined })}
        >
          <option value="">Any category</option>
          {CLAIM_CATEGORIES.map((c) => (
            <option key={c} value={c}>
              {categoryLabel(c)}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor="policyNumber">Policy number</label>
        <input
          id="policyNumber"
          maxLength={20}
          placeholder="DEMO-POL-1001"
          value={draft.policyNumber ?? ''}
          onChange={(e) => set({ policyNumber: e.target.value || undefined })}
        />
      </div>
      <div className="field">
        <label htmlFor="incidentFrom">Incident from</label>
        <input
          id="incidentFrom"
          type="date"
          value={draft.incidentFrom ?? ''}
          onChange={(e) => set({ incidentFrom: e.target.value || undefined })}
        />
      </div>
      <div className="field">
        <label htmlFor="incidentTo">Incident to</label>
        <input
          id="incidentTo"
          type="date"
          value={draft.incidentTo ?? ''}
          onChange={(e) => set({ incidentTo: e.target.value || undefined })}
        />
      </div>
      <div className="field">
        <label htmlFor="sort">Sort by</label>
        <select id="sort" value={draft.sort} onChange={(e) => set({ sort: e.target.value as ClaimSort })}>
          {CLAIM_SORTS.map((s) => (
            <option key={s} value={s}>
              {sortLabels[s]}
            </option>
          ))}
        </select>
      </div>
      <div className="filter-actions">
        <button type="submit" className="button">
          Apply filters
        </button>
        <button type="button" className="button button-secondary" onClick={() => onApply({ page: 1 })}>
          Clear
        </button>
      </div>
    </form>
  );
}
