import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from './AuthContext';
import { configureApiAuth } from '../api/client';

/**
 * Headless component that wires the auth context into the API client.
 * Must be rendered inside both <AuthProvider> and <BrowserRouter>.
 */
export function AuthApiWiring() {
  const { getAccessToken, logout } = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    configureApiAuth(getAccessToken, () => {
      logout().then(() => navigate('/login', { replace: true }));
    });
  }, [getAccessToken, logout, navigate]);

  return null;
}
