import { QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter, Link, Route, Routes } from 'react-router';
import { createQueryClient } from './api/queryClient';
import { Layout } from './components/Layout';
import { ClaimDetailPage } from './pages/ClaimDetailPage';
import { ClaimsPage } from './pages/ClaimsPage';
import { DashboardPage } from './pages/DashboardPage';
import { NewClaimPage } from './pages/NewClaimPage';

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<DashboardPage />} />
        <Route path="claims" element={<ClaimsPage />} />
        <Route path="claims/new" element={<NewClaimPage />} />
        <Route path="claims/:reference" element={<ClaimDetailPage />} />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  );
}

function NotFound() {
  return (
    <div className="card">
      <h1>Page not found</h1>
      <p>
        <Link to="/">Go to the dashboard</Link>
      </p>
    </div>
  );
}

const queryClient = createQueryClient();

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AppRoutes />
      </BrowserRouter>
    </QueryClientProvider>
  );
}
