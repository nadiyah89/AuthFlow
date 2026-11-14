import type {
  ApiProblemDetails,
  AuthResponse,
  LoginRequest,
  RegisterRequest,
  UserProfile,
} from '../types/auth.types'

export class ApiError extends Error {
  public status: number
  public problem: ApiProblemDetails

  constructor(status: number, problem: ApiProblemDetails) {
    super(problem.detail || problem.title || `API error with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }

  public get validationErrors(): Record<string, string[]> | undefined {
    return this.problem.errors
  }
}

class ApiClient {
  private accessToken: string | null = null
  private refreshPromise: Promise<string | null> | null = null
  private onAuthFailureCallback: (() => void) | null = null

  public setAccessToken(token: string | null): void {
    this.accessToken = token
  }

  public getAccessToken(): string | null {
    return this.accessToken
  }

  public setOnAuthFailure(callback: () => void): void {
    this.onAuthFailureCallback = callback
  }

  private async parseResponseError(response: Response): Promise<ApiError> {
    const contentType = response.headers.get('content-type') || ''
    let problem: ApiProblemDetails

    if (contentType.includes('application/json') || contentType.includes('application/problem+json')) {
      try {
        problem = (await response.json()) as ApiProblemDetails
      } catch {
        problem = {
          status: response.status,
          title: response.statusText || 'Error',
          detail: 'Failed to parse error response from server.',
        }
      }
    } else {
      const text = await response.text().catch(() => '')
      problem = {
        status: response.status,
        title: response.statusText || 'Error',
        detail: text || 'An unexpected server error occurred.',
      }
    }

    return new ApiError(response.status, problem)
  }

  public async request<T>(
    endpoint: string,
    options: RequestInit = {},
    isRetry = false
  ): Promise<T> {
    const headers = new Headers(options.headers || {})

    // Set JSON content-type if not specified and body is present
    if (options.body && !headers.has('Content-Type') && typeof options.body === 'string') {
      headers.set('Content-Type', 'application/json')
    }

    // Attach Anti-CSRF security headers required by backend middleware
    headers.set('X-Requested-With', 'XMLHttpRequest')
    headers.set('X-AuthFlow-Client', 'Web')

    // Attach in-memory JWT Access Token if available
    if (this.accessToken && !headers.has('Authorization')) {
      headers.set('Authorization', `Bearer ${this.accessToken}`)
    }

    const config: RequestInit = {
      ...options,
      headers,
      credentials: 'include', // Always include HttpOnly cookies
    }

    let response: Response
    try {
      response = await fetch(endpoint, config)
    } catch (error) {
      throw new ApiError(0, {
        status: 0,
        title: 'Network Error',
        detail:
          error instanceof Error
            ? error.message
            : 'Unable to connect to the server. Please verify your network connection.',
      })
    }

    // Check for 401 Unauthorized (attempt silent refresh if not already retried)
    if (response.status === 401 && !isRetry) {
      // Do not attempt refresh on auth endpoints themselves to avoid infinite loops
      const isAuthEndpoint =
        endpoint.includes('/api/auth/login') ||
        endpoint.includes('/api/auth/register') ||
        endpoint.includes('/api/auth/refresh')

      if (!isAuthEndpoint) {
        try {
          const newAccessToken = await this.refreshTokenSingleFlight()
          if (newAccessToken) {
            // Replay original request with fresh refreshed token
            const retryHeaders = new Headers(options.headers || {})
            retryHeaders.set('Authorization', `Bearer ${newAccessToken}`)
            return this.request<T>(endpoint, { ...options, headers: retryHeaders }, true)
          }
        } catch {
          // Refresh failed; notify listener to clear auth state
          this.accessToken = null
          this.onAuthFailureCallback?.()
          throw await this.parseResponseError(response)
        }
      }
    }

    if (!response.ok) {
      throw await this.parseResponseError(response)
    }

    // For 204 No Content or empty bodies
    if (response.status === 204) {
      return {} as T
    }

    const contentType = response.headers.get('content-type') || ''
    if (contentType.includes('application/json')) {
      return (await response.json()) as T
    }

    return (await response.text()) as unknown as T
  }

  /**
   * Single-flight token refresh:
   * Guarantees that concurrent 401 requests share the exact same refresh promise
   * and never issue duplicate refresh cookie requests.
   */
  public async refreshTokenSingleFlight(): Promise<string | null> {
    if (!this.refreshPromise) {
      this.refreshPromise = (async () => {
        try {
          const result = await this.refresh()
          this.accessToken = result.accessToken
          return result.accessToken
        } catch (error) {
          this.accessToken = null
          this.onAuthFailureCallback?.()
          throw error
        } finally {
          this.refreshPromise = null
        }
      })()
    }

    return this.refreshPromise
  }

  // --- Auth Service API Methods ---

  public async register(data: RegisterRequest): Promise<AuthResponse> {
    const response = await this.request<AuthResponse>('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify(data),
    })
    this.setAccessToken(response.accessToken)
    return response
  }

  public async login(data: LoginRequest): Promise<AuthResponse> {
    const response = await this.request<AuthResponse>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify(data),
    })
    this.setAccessToken(response.accessToken)
    return response
  }

  public async refresh(): Promise<AuthResponse> {
    const response = await this.request<AuthResponse>('/api/auth/refresh', {
      method: 'POST',
    })
    this.setAccessToken(response.accessToken)
    return response
  }

  public async logout(): Promise<void> {
    try {
      await this.request('/api/auth/logout', {
        method: 'POST',
      })
    } finally {
      this.setAccessToken(null)
    }
  }

  public async getMe(): Promise<UserProfile> {
    return this.request<UserProfile>('/api/auth/me', {
      method: 'GET',
    })
  }
}

export const api = new ApiClient()
