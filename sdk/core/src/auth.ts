// ---------------------------------------------------------------------------
// Auth — session token management
// Games never handle credentials directly. The parent portal issues a
// short-lived session token that the SDK includes in every API request.
// ---------------------------------------------------------------------------

export interface TokenStore {
  getToken(): string | null;
  setToken(token: string): void;
  clearToken(): void;
}

/**
 * In-memory token store. Platform-specific implementations (e.g. localStorage
 * for web, SecureStore for React Native) should implement this interface.
 */
export class MemoryTokenStore implements TokenStore {
  private token: string | null = null;

  getToken(): string | null {
    return this.token;
  }

  setToken(token: string): void {
    this.token = token;
  }

  clearToken(): void {
    this.token = null;
  }
}

export function buildAuthHeader(token: string): Record<string, string> {
  return { Authorization: `Bearer ${token}` };
}
