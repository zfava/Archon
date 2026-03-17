import type { CommandPhase } from '../types';

const LABELS: Record<CommandPhase, string> = {
  idle: '',
  parsing: 'Parsing intent\u2026',
  planning: 'Generating goals\u2026',
  simulating: 'Simulating strategies\u2026',
  ready: 'Plan ready',
  executing: 'Executing\u2026',
  complete: 'Complete',
  error: 'Error',
};

export function PhaseIndicator({ phase }: { phase: CommandPhase }) {
  if (phase === 'idle') return null;

  const isActive = ['parsing', 'planning', 'simulating', 'executing'].includes(phase);

  return (
    <div className={`phase-indicator phase-${phase}`}>
      {isActive && <span className="phase-spinner" />}
      <span className="phase-label">{LABELS[phase]}</span>
    </div>
  );
}
