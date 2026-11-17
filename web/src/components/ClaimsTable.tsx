import { Link } from 'react-router';
import type { ClaimSummary } from '../api/types';
import { categoryLabel, formatDate, formatNok } from '../format';
import { StatusBadge } from './ui';

export function ClaimsTable({ claims, caption }: { claims: ClaimSummary[]; caption: string }) {
  if (claims.length === 0) {
    return <p className="empty">No claims match these filters.</p>;
  }

  return (
    <div className="table-wrap">
      <table className="table">
        <caption className="visually-hidden">{caption}</caption>
        <thead>
          <tr>
            <th scope="col">Reference</th>
            <th scope="col">Policy</th>
            <th scope="col">Category</th>
            <th scope="col">Incident</th>
            <th scope="col" className="num">
              Amount
            </th>
            <th scope="col">Status</th>
          </tr>
        </thead>
        <tbody>
          {claims.map((claim) => (
            <tr key={claim.reference}>
              <td>
                <Link to={`/claims/${claim.reference}`} className="mono">
                  {claim.reference}
                </Link>
              </td>
              <td className="mono">{claim.policyNumber}</td>
              <td>{categoryLabel(claim.category)}</td>
              <td>{formatDate(claim.incidentDate)}</td>
              <td className="num">{formatNok(claim.claimedAmount)}</td>
              <td>
                <StatusBadge status={claim.status} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
