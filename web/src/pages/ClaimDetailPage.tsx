import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { ApiError, api } from '../api/client';
import type { ClaimDetail, ClaimStatus } from '../api/types';
import { ErrorMessage, Loading, StatusBadge } from '../components/ui';
import { categoryLabel, formatDate, formatDateTime, formatNok, statusLabel } from '../format';

export function ClaimDetailPage() {
  const { reference = '' } = useParams();
  const claim = useQuery({ queryKey: ['claim', reference], queryFn: () => api.getClaim(reference) });

  if (claim.isPending) {
    return <Loading />;
  }
  if (claim.isError) {
    const notFound = claim.error instanceof ApiError && claim.error.status === 404;
    return (
      <>
        <ErrorMessage error={claim.error} onRetry={notFound ? undefined : () => claim.refetch()} />
        <Link to="/claims">← Back to claims</Link>
      </>
    );
  }

  const data = claim.data;
  return (
    <>
      <p>
        <Link to="/claims">← All claims</Link>
      </p>
      <div className="page-header">
        <div>
          <h1 className="mono">{data.reference}</h1>
          <p className="muted">
            {categoryLabel(data.category)} claim on policy <span className="mono">{data.policyNumber}</span>
          </p>
        </div>
        <StatusBadge status={data.status} />
      </div>

      <div className="two-col">
        <section className="card" aria-labelledby="details">
          <h2 id="details">Claim details</h2>
          <dl className="details">
            <dt>Policy holder</dt>
            <dd>{data.policyHolder}</dd>
            <dt>Incident date</dt>
            <dd>{formatDate(data.incidentDate)}</dd>
            <dt>Claimed amount</dt>
            <dd>{formatNok(data.claimedAmount)}</dd>
            <dt>Submitted</dt>
            <dd>{formatDateTime(data.createdAtUtc)}</dd>
            <dt>Last updated</dt>
            <dd>{formatDateTime(data.updatedAtUtc)}</dd>
            <dt>Origin</dt>
            <dd>{data.isDemoSeed ? 'Example claim (demo seed data)' : 'Created by a visitor'}</dd>
          </dl>
          <h3>Description</h3>
          <p className="description">{data.description}</p>
        </section>

        <WorkflowActions claim={data} />
      </div>

      <section className="card" aria-labelledby="history">
        <h2 id="history">Audit history</h2>
        <ol className="timeline">
          {data.history.map((entry, index) => (
            <li key={`${entry.changedAtUtc}-${index}`} className="timeline-item">
              <div className="timeline-marker" aria-hidden="true" />
              <div>
                <div className="timeline-title">
                  {entry.fromStatus ? (
                    <>
                      {statusLabel(entry.fromStatus)} → <strong>{statusLabel(entry.toStatus)}</strong>
                    </>
                  ) : (
                    <strong>{statusLabel(entry.toStatus)}</strong>
                  )}
                </div>
                <time className="muted" dateTime={entry.changedAtUtc}>
                  {formatDateTime(entry.changedAtUtc)}
                </time>
                {entry.reason && <p className="timeline-reason">{entry.reason}</p>}
              </div>
            </li>
          ))}
        </ol>
      </section>
    </>
  );
}

const actionLabels: Record<ClaimStatus, string> = {
  Submitted: 'Submit',
  UnderReview: 'Start review',
  Approved: 'Approve',
  Rejected: 'Reject',
};

/** Shows only the status changes the backend says are allowed next; the backend enforces them again on save. */
export function WorkflowActions({ claim }: { claim: ClaimDetail }) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState('');
  const [localError, setLocalError] = useState<string | null>(null);

  const transition = useMutation({
    mutationFn: (targetStatus: ClaimStatus) =>
      api.transitionClaim(claim.reference, { targetStatus, reason: reason.trim() || undefined }),
    onSuccess: (updated) => {
      queryClient.setQueryData(['claim', claim.reference], updated);
      void queryClient.invalidateQueries({ queryKey: ['claims'] });
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      setReason('');
    },
  });

  const run = (target: ClaimStatus) => {
    setLocalError(null);
    if (target === 'Rejected' && !reason.trim()) {
      setLocalError('Please give a reason for the rejection.');
      return;
    }
    transition.mutate(target);
  };

  const apiError = transition.error instanceof ApiError ? transition.error : null;
  const reasonError = localError ?? apiError?.errorsFor('reason')[0];

  return (
    <section className="card" aria-labelledby="workflow">
      <h2 id="workflow">Workflow</h2>
      <ol className="workflow-steps" aria-label="Claim workflow">
        {(
          ['Submitted', 'UnderReview', claim.status === 'Rejected' ? 'Rejected' : 'Approved'] as ClaimStatus[]
        ).map((step) => (
          <li key={step} className={step === claim.status ? 'current' : undefined}>
            {statusLabel(step)}
          </li>
        ))}
      </ol>

      {claim.allowedNextStatuses.length === 0 ? (
        <p className="muted">
          This claim is {statusLabel(claim.status).toLowerCase()}. No further changes are possible.
        </p>
      ) : (
        <>
          <div className="field">
            <label htmlFor="reason">
              Reason{' '}
              <span className="muted">
                {claim.allowedNextStatuses.includes('Rejected') ? '(required to reject)' : '(optional)'}
              </span>
            </label>
            <textarea
              id="reason"
              rows={3}
              maxLength={500}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              aria-invalid={reasonError ? true : undefined}
              aria-describedby={reasonError ? 'reason-error' : undefined}
            />
            {reasonError && (
              <p className="field-error" id="reason-error">
                {reasonError}
              </p>
            )}
          </div>
          <div className="actions">
            {claim.allowedNextStatuses.map((status) => (
              <button
                key={status}
                type="button"
                className={`button ${status === 'Rejected' ? 'button-danger' : ''}`}
                disabled={transition.isPending}
                onClick={() => run(status)}
              >
                {actionLabels[status]}
              </button>
            ))}
          </div>
          {apiError && !apiError.errorsFor('reason').length && <ErrorMessage error={apiError} />}
        </>
      )}
    </section>
  );
}
