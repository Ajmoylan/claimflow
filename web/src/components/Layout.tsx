import { NavLink, Outlet } from 'react-router';

export function Layout() {
  return (
    <div className="app">
      <div className="demo-banner" role="note">
        <strong>Fictional demo.</strong> CLAIMFLOW is an independent portfolio project by Alex Moylan. It is
        not an official If Forsikring product and is not affiliated with any insurer. All policies, claims and
        people are synthetic, and the data resets every night.
      </div>

      <header className="site-header">
        <div className="container header-inner">
          <NavLink to="/" className="brand" aria-label="CLAIMFLOW home">
            <span className="brand-mark" aria-hidden="true">
              ✓
            </span>
            CLAIMFLOW
          </NavLink>
          <nav aria-label="Main">
            <ul className="nav-list">
              <li>
                <NavLink to="/" end>
                  Dashboard
                </NavLink>
              </li>
              <li>
                <NavLink to="/claims" end>
                  Claims
                </NavLink>
              </li>
              <li>
                <NavLink to="/claims/new" className="nav-cta">
                  New claim
                </NavLink>
              </li>
              <li>
                <a href="/docs" target="_blank" rel="noreferrer">
                  API docs
                </a>
              </li>
            </ul>
          </nav>
        </div>
      </header>

      <main className="container main" id="main">
        <Outlet />
      </main>

      <footer className="site-footer">
        <div className="container">
          Built with ASP.NET Core, EF Core and React ·{' '}
          <a href="https://github.com/Ajmoylan/claimflow" target="_blank" rel="noreferrer">
            Source code
          </a>{' '}
          · Independent fictional project, not affiliated with If Forsikring.
        </div>
      </footer>
    </div>
  );
}
