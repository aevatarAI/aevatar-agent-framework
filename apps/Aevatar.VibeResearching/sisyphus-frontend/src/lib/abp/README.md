# ABP API Client Library

Frontend API client for VibeResearching backend authentication and user management.

## Architecture

```
src/lib/abp/
├── index.ts        # Unified re-exports
├── config.ts       # Base configuration, token management, fetch helper
├── auth.ts         # Authentication (login/logout/register)
├── profile.ts      # User profile & avatar management (NEW)
├── users.ts        # User CRUD (admin)
├── roles.ts        # Role CRUD (admin)
└── permissions.ts  # Permission management
```

## Authentication Modes

The client supports **two authentication modes**:

### 1. Cookie Mode (Default - New Backend)

Uses session cookies for authentication. Simpler for SPAs.

```typescript
// .env
VITE_AUTH_MODE=cookie
```

**Endpoints:**
- `POST /api/auth/login` - Create session
- `POST /api/auth/logout` - Destroy session
- `GET /api/vibe/my-profile` - Get current user

### 2. Token Mode (Legacy - OpenIddict)

> ⚠️ **Warning**: Token mode uses Password Grant which is **NOT enabled** by default
> on the backend. The backend only supports Authorization Code + PKCE flow.
> Use Cookie mode (default) for production.

Uses JWT access tokens with refresh token flow.

```typescript
// .env
VITE_AUTH_MODE=token
```

**Endpoints:**
- `POST /connect/token` - Get access token (requires Password Grant enabled)
- `POST /connect/revocation` - Revoke token
- `GET /connect/userinfo` - Get user info

## Quick Start

```typescript
import { abpLogin, fetchCurrentUser, getMyProfile } from '@/lib/abp'

// Login
const result = await abpLogin({
  email: 'admin@example.com',
  password: 'P@ssw0rd',
  rememberMe: true,
})

if (result.success) {
  console.log('Logged in as:', result.user?.userName)
}

// Get full profile (with avatar)
const profile = await getMyProfile()
console.log('Display name:', profile?.displayName)
console.log('Avatar:', profile?.avatarUrl)
```

## API Reference

### Auth Module (`auth.ts`)

| Function | Description |
|----------|-------------|
| `abpLogin(input)` | Login with email/password |
| `abpLogout()` | Logout current session |
| `abpRegister(input)` | Register new account |
| `abpRefreshToken()` | Refresh access token (token mode) |
| `fetchCurrentUser()` | Get current user info |
| `fetchMyPermissions()` | Get current user's permissions |
| `isAuthenticated()` | Check if user is authenticated |
| `abpForgotPassword(email)` | Send password reset email |
| `abpResetPassword(...)` | Reset password with token |
| `abpVerifyEmail(...)` | Verify email address |

### Profile Module (`profile.ts`) - NEW

| Function | Description |
|----------|-------------|
| `getMyProfile()` | Get current user's full profile |
| `updateMyProfile(input)` | Update display name, bio |
| `changePassword(input)` | Change current password |
| `uploadProfilePicture(file)` | Upload avatar (max 2MB, JPG/PNG/WebP) |
| `getProfilePictureUrl(userId)` | Get avatar URL for any user |
| `deleteProfilePicture()` | Remove current avatar |

### Users Module (`users.ts`)

| Function | Description |
|----------|-------------|
| `getUsers(params?)` | List users (paginated) |
| `getUser(id)` | Get single user |
| `createUser(input)` | Create new user |
| `updateUser(id, input)` | Update user |
| `deleteUser(id)` | Delete user |
| `setUserPassword(id, password)` | Set user password |
| `toggleUserLock(id, locked)` | Lock/unlock user |
| `getUserStats()` | Get user statistics |
| `getAssignableRoles()` | Get roles that can be assigned |

### Roles Module (`roles.ts`)

| Function | Description |
|----------|-------------|
| `getRoles()` | List all roles |
| `getRole(id)` | Get single role |
| `createRole(input)` | Create new role |
| `updateRole(id, input)` | Update role |
| `deleteRole(id)` | Delete role |
| `getRolePermissions(roleId)` | Get role's permissions |
| `updateRolePermissions(roleId, permissions)` | Update role permissions |

### Permissions Module (`permissions.ts`)

| Function | Description |
|----------|-------------|
| `getMyPermissions()` | Get current user's granted permissions |
| `hasPermission(name)` | Check if user has permission |
| `hasAnyPermission(names)` | Check if user has any of permissions |
| `hasAllPermissions(names)` | Check if user has all permissions |
| `getPermissions(provider, key)` | Get permissions for role/user |
| `updatePermissions(provider, key, list)` | Update permissions |
| `grantAllPermissions(provider, key)` | Grant all permissions |
| `revokeAllPermissions(provider, key)` | Revoke all permissions |

## Environment Variables

```bash
# API Base URL
# - Empty (default): Use Vite proxy (recommended for development)
# - Set only for production cross-origin deployment
# Vite proxy handles: /api/*, /connect/*, /.well-known/*
VITE_API_BASE_URL=

# Auth Mode: 'cookie' (default) or 'token'
VITE_AUTH_MODE=cookie

# OpenIddict Client (token mode only)
VITE_ABP_CLIENT_ID=VibeResearching_React

# Enable mock auth for offline development
VITE_MOCK_AUTH=false

# OAuth (disabled by default)
VITE_ENABLE_OAUTH=false
```

## Backend Endpoints

### Auth Endpoints
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/auth/login` | Cookie login |
| POST | `/api/auth/logout` | Cookie logout |
| GET | `/api/account/my-permissions` | Get user permissions |

### Profile Endpoints
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/vibe/my-profile` | Get profile |
| PUT | `/api/vibe/my-profile` | Update profile |
| POST | `/api/vibe/change-password` | Change password |
| PUT | `/api/account/profile-picture` | Upload avatar |
| GET | `/api/account/profile-picture/{userId}` | Get avatar (public) |
| DELETE | `/api/account/profile-picture` | Delete avatar |

### Identity Endpoints (Admin)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/identity/users` | List users |
| POST | `/api/identity/users` | Create user |
| PUT | `/api/identity/users/{id}` | Update user |
| DELETE | `/api/identity/users/{id}` | Delete user |
| GET | `/api/identity/roles` | List roles |
| POST | `/api/identity/roles` | Create role |
| PUT | `/api/identity/roles/{id}` | Update role |
| DELETE | `/api/identity/roles/{id}` | Delete role |

### Permission Endpoints
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/permission-management/permissions` | Get permissions |
| PUT | `/api/permission-management/permissions` | Update permissions |

## Session Permissions

Common permissions for VibeResearching:

```typescript
const SESSION_PERMISSIONS = {
  VIEW: 'VibeResearching.Sessions.View',
  CREATE: 'VibeResearching.Sessions.Create',
  PAUSE: 'VibeResearching.Sessions.Pause',
  RESUME: 'VibeResearching.Sessions.Resume',
  TERMINATE: 'VibeResearching.Sessions.Terminate',
  DELETE: 'VibeResearching.Sessions.Delete',
}
```

## Error Handling

```typescript
import { abpLogin } from '@/lib/abp'

const result = await abpLogin({ email, password })

if (!result.success) {
  switch (result.error) {
    case 'Invalid username or password.':
      // Show login error
      break
    case 'Account is locked. Please try again later.':
      // Show locked message
      break
    case 'Login not allowed. Please verify your email.':
      // Redirect to verification
      break
  }
}
```

## Mock Mode

For development without backend:

```bash
VITE_MOCK_AUTH=true
```

Default mock users:
- `admin@sisyphus.ai` / `Admin@123` (admin role)
- `admin` / `1q2w3E*` (admin role)
