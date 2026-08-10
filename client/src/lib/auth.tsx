import {
  createContext,
  useContext,
  useState,
  useCallback,
  type ReactNode,
} from "react";
import { api, getToken, setToken, clearToken } from "./api";
import type { AuthResponse } from "./types";

interface AuthUser {
  userId: string;
  email: string;
}

interface AuthContextValue {
  user: AuthUser | null;
  token: string | null;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

// The JWT itself is the source of truth for "am I logged in" - decode the
// user info back out of it on load instead of a separate /auth/me round
// trip, so a page refresh doesn't flash a logged-out state while that
// request is in flight.
function decodeUserFromToken(token: string): AuthUser | null {
  try {
    const payload = JSON.parse(atob(token.split(".")[1]));
    return { userId: payload.sub, email: payload.email };
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setTokenState] = useState<string | null>(getToken());
  const [user, setUser] = useState<AuthUser | null>(() => {
    const existing = getToken();
    return existing ? decodeUserFromToken(existing) : null;
  });

  const applyAuth = useCallback((data: AuthResponse) => {
    setToken(data.token);
    setTokenState(data.token);
    setUser({ userId: data.userId, email: data.email });
  }, []);

  const login = useCallback(
    async (email: string, password: string) => {
      const { data } = await api.post<AuthResponse>("/auth/login", {
        email,
        password,
      });
      applyAuth(data);
    },
    [applyAuth],
  );

  const register = useCallback(
    async (email: string, password: string) => {
      const { data } = await api.post<AuthResponse>("/auth/register", {
        email,
        password,
      });
      applyAuth(data);
    },
    [applyAuth],
  );

  const logout = useCallback(() => {
    clearToken();
    setTokenState(null);
    setUser(null);
  }, []);

  return (
    <AuthContext.Provider value={{ user, token, login, register, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return ctx;
}
