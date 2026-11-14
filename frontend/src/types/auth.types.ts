export interface UserProfile {
  id: string;
  fullName: string;
  email: string;
  createdAtUtc: string;
}

export interface AuthResponse {
  accessToken: string;
  expiresInMinutes: number;
  user: UserProfile;
}

export interface RegisterRequest {
  fullName: string;
  email: string;
  password: string;
  confirmPassword: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface ApiProblemDetails {
  status: number;
  title: string;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
}

export interface AuthContextType {
  user: UserProfile | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  error: string | null;
  register: (data: RegisterRequest) => Promise<void>;
  login: (data: LoginRequest) => Promise<void>;
  logout: () => Promise<void>;
  refreshUser: () => Promise<UserProfile | null>;
  clearError: () => void;
}
