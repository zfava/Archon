import { useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from './AuthContext';
import './login.css';

export function LoginPage() {
  const { login, register } = useAuth();
  const navigate = useNavigate();

  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [orgName, setOrgName] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      let success: boolean;
      if (mode === 'login') {
        success = await login(email, password);
        if (!success) setError('Invalid email or password.');
      } else {
        if (!orgName.trim()) { setError('Organization name is required.'); setLoading(false); return; }
        if (!displayName.trim()) { setError('Display name is required.'); setLoading(false); return; }
        if (password.length < 8) { setError('Password must be at least 8 characters.'); setLoading(false); return; }
        success = await register(orgName, email, password, displayName);
        if (!success) setError('Email already registered or registration failed.');
      }

      if (success) navigate('/', { replace: true });
    } catch {
      setError('An unexpected error occurred.');
    } finally {
      setLoading(false);
    }
  }, [mode, email, password, displayName, orgName, login, register, navigate]);

  return (
    <div className="login-root">
      <div className="login-card">
        <div className="login-header">
          <h1 className="login-title">ArchonAI</h1>
          <p className="login-subtitle">Enterprise Operations Intelligence</p>
        </div>

        <div className="login-tabs">
          <button
            className={`login-tab ${mode === 'login' ? 'login-tab--active' : ''}`}
            onClick={() => { setMode('login'); setError(''); }}
          >
            Sign In
          </button>
          <button
            className={`login-tab ${mode === 'register' ? 'login-tab--active' : ''}`}
            onClick={() => { setMode('register'); setError(''); }}
          >
            Create Account
          </button>
        </div>

        <form className="login-form" onSubmit={handleSubmit}>
          {mode === 'register' && (
            <>
              <label className="login-label">
                Organization Name
                <input
                  className="login-input"
                  type="text"
                  value={orgName}
                  onChange={e => setOrgName(e.target.value)}
                  placeholder="Acme Corp"
                  required
                  autoComplete="organization"
                />
              </label>
              <label className="login-label">
                Your Name
                <input
                  className="login-input"
                  type="text"
                  value={displayName}
                  onChange={e => setDisplayName(e.target.value)}
                  placeholder="Jane Smith"
                  required
                  autoComplete="name"
                />
              </label>
            </>
          )}

          <label className="login-label">
            Email
            <input
              className="login-input"
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              placeholder="you@company.com"
              required
              autoComplete="email"
            />
          </label>

          <label className="login-label">
            Password
            <input
              className="login-input"
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              placeholder={mode === 'register' ? 'Min 8 characters' : 'Enter password'}
              required
              autoComplete={mode === 'register' ? 'new-password' : 'current-password'}
            />
          </label>

          {error && <p className="login-error">{error}</p>}

          <button className="login-submit" type="submit" disabled={loading}>
            {loading
              ? 'Please wait...'
              : mode === 'login'
                ? 'Sign In'
                : 'Create Account'}
          </button>
        </form>
      </div>
    </div>
  );
}
