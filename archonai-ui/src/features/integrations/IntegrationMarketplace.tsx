import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { useIntegrationMarketplace } from './hooks/useIntegrationMarketplace';
import { ConnectorCard } from './components/ConnectorCard';
import { CategoryFilter } from './components/CategoryFilter';
import { StatusPanel } from './components/StatusPanel';
import './integrations.css';

export function IntegrationMarketplace() {
  const {
    state,
    filtered,
    stats,
    connect,
    disconnect,
    setCategory,
    setSearch,
  } = useIntegrationMarketplace();

  const categoryCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const c of state.connectors) {
      counts[c.category] = (counts[c.category] ?? 0) + 1;
    }
    return counts;
  }, [state.connectors]);

  if (state.loading) {
    return (
      <div className="im-container">
        <div className="im-loading">
          <div className="phase-spinner" />
          <span>Loading integrations...</span>
        </div>
      </div>
    );
  }

  return (
    <div className="im-container">
      {/* Header */}
      <header className="im-header">
        <div className="im-header-left">
          <Link to="/" className="sg-back-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
            Console
          </Link>
          <h1 className="im-page-title">Integration Marketplace</h1>
        </div>
        <div className="im-header-right">
          <StatusPanel connected={stats.connected} errored={stats.errored} total={stats.total} />
        </div>
      </header>

      {/* Toolbar */}
      <div className="im-toolbar">
        <div className="im-search-wrap">
          <svg className="im-search-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="11" cy="11" r="8" />
            <path d="M21 21l-4.35-4.35" />
          </svg>
          <input
            className="im-search"
            type="text"
            placeholder="Search connectors..."
            value={state.searchQuery}
            onChange={e => setSearch(e.target.value)}
          />
        </div>
        <CategoryFilter
          active={state.activeCategory}
          onChange={setCategory}
          counts={categoryCounts}
        />
      </div>

      {/* Error banner */}
      {state.error && (
        <div className="im-error-banner">
          <span>{state.error}</span>
        </div>
      )}

      {/* Grid */}
      <div className="im-content">
        {filtered.length === 0 ? (
          <div className="im-empty">
            <p>No connectors match your filter.</p>
          </div>
        ) : (
          <div className="im-grid">
            {filtered.map(c => (
              <ConnectorCard
                key={c.id}
                connector={c}
                onConnect={connect}
                onDisconnect={disconnect}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
