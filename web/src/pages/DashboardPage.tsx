import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { api } from '../api/client';
import { CLAIM_CATEGORIES, CLAIM_STATUSES } from '../api/types';
import { ClaimsTable } from '../components/ClaimsTable';
import { ErrorMessage, Loading, StatCard, StatusBadge } from '../components/ui';
import { categoryLabel, formatDateTime, formatNok, formatPercent } from '../format';

export function DashboardPage() {
  const dashboard = useQuery({ queryKey: ['dashboard'], queryFn: api.getDashboard });

  if (dashboard.isPending) {
    return <Loading label="Loading dashboard… (the demo database may take a few seconds to wake up)" />;
  }
  if (dashboard.isError) {
    return <ErrorMessage error={dashboard.error} onRetry={() => dashboard.refetch()} />;
  }

  const data = dashboard.data;
  const maxCategory = Math.max(1, ...Object.values(data.countByCategory));

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Claims dashboard</h1>
          <p className="muted">An overview of every claim in the demo, by status and category.</p>
        </div>
        <Link to="/claims/new" className="button">
          Submit a claim
        </Link>
      </div>

      <section className="stats-grid" aria-label="Key figures">
        <StatCard label="Total claims" value={data.totalClaims} />
        <StatCard label="Open claims" value={data.openClaims} hint="Submitted or under review" />
        <StatCard
          label="Approval rate"
          value={formatPercent(data.approvalRate)}
          hint="Approved out of decided claims"
        />
        <StatCard label="Total claimed" value={formatNok(data.totalClaimedAmount)} />
        <StatCard label="Total approved" value={formatNok(data.totalApprovedAmount)} />
      </section>

      <div className="two-col">
        <section className="card" aria-labelledby="by-status">
          <h2 id="by-status">By status</h2>
          <ul className="status-list">
            {CLAIM_STATUSES.map((status) => (
              <li key={status}>
                <Link to={`/claims?status=${status}`} className="status-row">
                  <StatusBadge status={status} />
                  <span className="status-count">{data.countByStatus[status]}</span>
                </Link>
              </li>
            ))}
          </ul>
        </section>

        <section className="card" aria-labelledby="by-category">
          <h2 id="by-category">By category</h2>
          <ul className="bar-list">
            {CLAIM_CATEGORIES.map((category) => (
              <li key={category}>
                <span className="bar-label">{categoryLabel(category)}</span>
                <span className="bar-track" aria-hidden="true">
                  <span
                    className="bar-fill"
                    style={{ width: `${(data.countByCategory[category] / maxCategory) * 100}%` }}
                  />
                </span>
                <span className="bar-value">{data.countByCategory[category]}</span>
              </li>
            ))}
          </ul>
        </section>
      </div>

      <section className="card" aria-labelledby="recent">
        <div className="section-header">
          <h2 id="recent">Most recent claims</h2>
          <Link to="/claims">View all claims →</Link>
        </div>
        <ClaimsTable claims={data.recentClaims} caption="Most recent claims" />
      </section>

      <section className="card demo-info" aria-labelledby="demo-info">
        <h2 id="demo-info">About this demo</h2>
        <p>
          Visitors have created <strong>{data.demo.visitorClaims}</strong> of at most{' '}
          <strong>{data.demo.maxVisitorClaims}</strong> claims since the last reset. The demo data is restored
          automatically every night at 03:00 Oslo time. The next reset is at{' '}
          <strong>{formatDateTime(data.demo.nextResetAtUtc)}</strong> (your local time).
        </p>
      </section>
    </>
  );
}
