import axios from "axios";

export const API_URL = import.meta.env.VITE_API_URL as string;

export const api = axios.create({ baseURL: API_URL });

const TOKEN_KEY = "dropbox_token";

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY);
}

api.interceptors.request.use((config) => {
  const token = getToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// A 401 means the token is missing/expired - drop it and let the app's
// auth guard redirect to /login on next render, rather than leaving a
// dead token around that will just fail again on every future request.
api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      clearToken();
    }
    return Promise.reject(error);
  },
);
