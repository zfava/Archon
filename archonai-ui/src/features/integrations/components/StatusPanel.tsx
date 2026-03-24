interface Props {
  connected: number;
  errored: number;
  total: number;
}

export function StatusPanel({ connected, errored, total }: Props) {
  return (
    <div className="im-status-panel">
      <div className="im-status-stat">
        <span className="im-status-num im-status-num--connected">{connected}</span>
        <span className="im-status-lbl">Connected</span>
      </div>
      <div className="im-status-divider" />
      <div className="im-status-stat">
        <span className={`im-status-num ${errored > 0 ? 'im-status-num--error' : ''}`}>{errored}</span>
        <span className="im-status-lbl">Errors</span>
      </div>
      <div className="im-status-divider" />
      <div className="im-status-stat">
        <span className="im-status-num">{total}</span>
        <span className="im-status-lbl">Available</span>
      </div>
    </div>
  );
}
