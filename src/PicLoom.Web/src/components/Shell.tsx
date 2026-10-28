import type { ComponentType, ReactNode } from 'react';
import { useIntl } from 'react-intl';
import { GroupsIcon, SettingsIcon, TasksIcon, TemplatesIcon } from './Icons';

export type PageKey = 'tasks' | 'templates' | 'groups' | 'settings';

interface NavItem {
  key: PageKey;
  labelId: string;
  icon: ComponentType<{ className?: string }>;
}

const NAV_ITEMS: readonly NavItem[] = [
  { key: 'tasks', labelId: 'nav.tasks', icon: TasksIcon },
  { key: 'templates', labelId: 'nav.templates', icon: TemplatesIcon },
  { key: 'groups', labelId: 'nav.groups', icon: GroupsIcon },
  { key: 'settings', labelId: 'nav.settings', icon: SettingsIcon },
];

interface ShellProps {
  active: PageKey;
  onNavigate: (page: PageKey) => void;
  children: ReactNode;
}

export function Shell({ active, onNavigate, children }: ShellProps) {
  const intl = useIntl();

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main">
        {intl.formatMessage({ id: 'app.skipToContent' })}
      </a>
      <aside className="sidebar" aria-label={intl.formatMessage({ id: 'app.navLabel' })}>
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">
            P
          </span>
          <span className="brand-text">
            <span className="brand-name">{intl.formatMessage({ id: 'app.name' })}</span>
            <span className="brand-tagline">{intl.formatMessage({ id: 'app.tagline' })}</span>
          </span>
        </div>

        <nav className="nav">
          {NAV_ITEMS.map((item) => {
            const Icon = item.icon;
            const isActive = item.key === active;
            return (
              <button
                key={item.key}
                type="button"
                className={`nav-item${isActive ? ' is-active' : ''}`}
                aria-current={isActive ? 'page' : undefined}
                onClick={() => onNavigate(item.key)}
              >
                <Icon className="nav-icon" />
                <span>{intl.formatMessage({ id: item.labelId })}</span>
              </button>
            );
          })}
        </nav>

        <div className="sidebar-footer">
          <span className="mode-badge">{intl.formatMessage({ id: 'app.modeBadge' })}</span>
          <span className="footer-note">{intl.formatMessage({ id: 'app.footerNote' })}</span>
        </div>
      </aside>

      <main id="main" className="main" tabIndex={-1}>
        {children}
      </main>
    </div>
  );
}

interface PageHeaderProps {
  title: string;
  subtitle?: string;
  meta?: string;
  actions?: ReactNode;
}

export function PageHeader({ title, subtitle, meta, actions }: PageHeaderProps) {
  return (
    <header className="page-header">
      <div className="page-heading">
        <div className="page-title-row">
          <h1>{title}</h1>
          {meta ? <span className="page-meta">{meta}</span> : null}
        </div>
        {subtitle ? <p className="page-subtitle">{subtitle}</p> : null}
      </div>
      {actions ? <div className="page-actions">{actions}</div> : null}
    </header>
  );
}
