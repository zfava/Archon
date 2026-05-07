import type { CommandPhase } from '../types';

const LABELS: Record<CommandPhase, string> = {
  idle: '',
  parsing: 'Parsing\u2026',
  planning: 'Planning\u2026',
  simulating: 'Simulating\u2026',
  ready: 'Plan ready',
  executing: 'Executing\u2026',
  complete: 'Complete',
  error: 'Error',
};

const PHASE_ORDER: CommandPhase[] = ['parsing', 'planning', 'simulating', 'ready', 'executing'];

function getStepState(phase: CommandPhase, stepPhase: CommandPhase): 'done' | 'active' | 'pending' {
  const currentIdx = PHASE_ORDER.indexOf(phase);
  const stepIdx = PHASE_ORDER.indexOf(stepPhase);

  if (phase === 'complete') return 'done';
  if (phase === 'error') return stepIdx <= currentIdx ? 'done' : 'pending';
  if (stepIdx < currentIdx) return 'done';
  if (stepIdx === currentIdx) return 'active';
  return 'pending';
}

export function PhaseIndicator({ phase }: { phase: CommandPhase }) {
  if (phase === 'idle') return null;

  const isActive = ['parsing', 'planning', 'simulating', 'executing'].includes(phase);

  return (
    <div className={`phase-indicator phase-${phase}`}>
      <div className="phase-stepper">
        {PHASE_ORDER.map((stepPhase) => {
          const state = getStepState(phase, stepPhase);
          return (
            <span
              key={stepPhase}
              className={`phase-step ${state === 'done' ? 'phase-step--done' : ''} ${state === 'active' ? 'phase-step--active' : ''}`}
            />
          );
        })}
      </div>
      {isActive && <span className="phase-spinner" />}
      <span className="phase-label">{LABELS[phase]}</span>
    </div>
  );
}
