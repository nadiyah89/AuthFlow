// AuthFlow Full End-to-End Automated Integration Verification
import { spawn } from 'node:child_process';
import { setTimeout as sleep } from 'node:timers/promises';

const BACKEND_PORT = 5202;
const FRONTEND_PORT = 5173;
const BACKEND_URL = `http://127.0.0.1:${BACKEND_PORT}`;
const FRONTEND_URL = `http://127.0.0.1:${FRONTEND_PORT}`;

console.log('--- Starting AuthFlow End-to-End Local Verification ---');

// 1. Start Backend API
console.log('1. Starting ASP.NET Core Web API on port 5202...');
const backendProc = spawn(
  'dotnet',
  ['run', '--project', 'D:\\AuthFlow\\backend\\src\\AuthFlow.Api\\AuthFlow.Api.csproj', '--no-build', '--urls', BACKEND_URL],
  { stdio: ['ignore', 'pipe', 'pipe'] }
);

backendProc.stdout.on('data', (d) => {
  const msg = d.toString();
  if (msg.includes('Now listening on')) {
    console.log(`   [Backend Ready]: ${msg.trim()}`);
  }
});

// 2. Start Frontend Vite Server
console.log('2. Starting Frontend Vite dev server on port 5173...');
const frontendProc = spawn(
  'cmd.exe',
  ['/c', 'npm', 'run', 'dev', '--', '--host', '127.0.0.1', '--port', FRONTEND_PORT.toString()],
  { cwd: 'D:\\AuthFlow\\frontend', stdio: ['ignore', 'pipe', 'pipe'] }
);

frontendProc.stdout.on('data', (d) => {
  const msg = d.toString();
  if (msg.includes('ready in') || msg.includes('Local:')) {
    console.log(`   [Frontend Ready]: ${msg.trim()}`);
  }
});

async function run() {
  try {
    console.log('3. Waiting for servers to initialize...');
    await sleep(6000);

    // Test 1: Frontend Serving
    console.log('\n--- Test 1: Frontend Serving ---');
    const feRes = await fetch(FRONTEND_URL);
    console.log(`   Frontend HTTP Status: ${feRes.status} (Expected 200)`);
    if (feRes.status !== 200) throw new Error('Frontend failed to serve 200 OK');
    const feHtml = await feRes.text();
    if (!feHtml.includes('<div id="root"></div>')) throw new Error('Root div missing in index.html');
    console.log('   ✓ Frontend index.html served successfully.');

    // Test 2: Vite Proxy to Backend Health
    console.log('\n--- Test 2: Vite Dev Proxy (/api/health) ---');
    const healthRes = await fetch(`${FRONTEND_URL}/api/health`);
    console.log(`   Vite Proxy /api/health Status: ${healthRes.status} (Expected 200)`);
    const healthJson = await healthRes.json();
    console.log(`   Health Response:`, healthJson);
    if (healthJson.status !== 'Healthy') throw new Error('Backend health reported unhealthy');
    console.log('   ✓ Vite dev proxy to ASP.NET Core backend is working seamlessly.');

    // Test 3: User Registration through Vite Proxy
    console.log('\n--- Test 3: User Registration through Frontend Proxy ---');
    const uniqueEmail = `e2e-${Date.now()}@example.com`;
    const password = 'P@ssword123!Secure';
    const regPayload = {
      fullName: 'E2E Test User',
      email: uniqueEmail,
      password: password,
      confirmPassword: password
    };

    const regRes = await fetch(`${FRONTEND_URL}/api/auth/register`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Requested-With': 'XMLHttpRequest',
        'X-AuthFlow-Client': 'Web'
      },
      body: JSON.stringify(regPayload)
    });

    console.log(`   Register Status: ${regRes.status} (Expected 201)`);
    if (regRes.status !== 201) {
      console.error(await regRes.text());
      throw new Error(`Register failed with status ${regRes.status}`);
    }

    const regData = await regRes.json();
    console.log(`   User Registered: ID=${regData.user.id}, Email=${regData.user.email}`);
    const accessToken1 = regData.accessToken;
    if (!accessToken1) throw new Error('Missing access token in register response');

    // Extract cookie from Set-Cookie header
    const rawSetCookie = regRes.headers.get('set-cookie');
    console.log(`   Set-Cookie Header: ${rawSetCookie}`);
    if (!rawSetCookie || !rawSetCookie.includes('refreshToken=')) {
      throw new Error('Set-Cookie header missing or does not contain refreshToken');
    }
    const cookieValue = rawSetCookie.split(';')[0]; // "refreshToken=..."
    console.log('   ✓ Registration succeeded, returned JWT access token and HttpOnly refresh cookie.');

    // Test 4: Protected Endpoint Access (GET /api/auth/me)
    console.log('\n--- Test 4: Access Protected Profile (GET /api/auth/me) ---');
    const meRes = await fetch(`${FRONTEND_URL}/api/auth/me`, {
      headers: {
        'Authorization': `Bearer ${accessToken1}`,
        'X-Requested-With': 'XMLHttpRequest',
        'X-AuthFlow-Client': 'Web'
      }
    });

    console.log(`   GET /me Status: ${meRes.status} (Expected 200)`);
    const meData = await meRes.json();
    console.log(`   Profile Retrieved: Name="${meData.fullName}", Email="${meData.email}"`);
    if (meData.email !== uniqueEmail) throw new Error('Email mismatch on protected profile endpoint');
    console.log('   ✓ Protected profile accessed with in-memory Bearer token.');

    // Test 5: Session Restoration / Token Rotation via Proxy
    console.log('\n--- Test 5: Simulate Page Reload / Token Rotation (POST /api/auth/refresh) ---');
    const refreshRes = await fetch(`${FRONTEND_URL}/api/auth/refresh`, {
      method: 'POST',
      headers: {
        'Cookie': cookieValue,
        'Origin': FRONTEND_URL,
        'X-Requested-With': 'XMLHttpRequest',
        'X-AuthFlow-Client': 'Web'
      }
    });

    console.log(`   POST /refresh Status: ${refreshRes.status} (Expected 200)`);
    if (refreshRes.status !== 200) {
      console.error(await refreshRes.text());
      throw new Error('Refresh failed');
    }
    const refreshData = await refreshRes.json();
    const accessToken2 = refreshData.accessToken;
    const rotatedCookie = refreshRes.headers.get('set-cookie');
    console.log(`   New Access Token Generated: ${accessToken2.substring(0, 25)}...`);
    console.log(`   New Rotated Cookie Received: ${rotatedCookie?.split(';')[0]}`);
    if (!accessToken2 || accessToken2 === accessToken1) {
      throw new Error('Expected new rotated access token');
    }
    console.log('   ✓ Silent session restoration and token rotation succeeded.');

    // Test 6: Access Protected Profile with New Access Token
    console.log('\n--- Test 6: Access /api/auth/me with Rotated Token ---');
    const meRes2 = await fetch(`${FRONTEND_URL}/api/auth/me`, {
      headers: {
        'Authorization': `Bearer ${accessToken2}`,
        'X-Requested-With': 'XMLHttpRequest',
        'X-AuthFlow-Client': 'Web'
      }
    });
    console.log(`   GET /me Status with Rotated Token: ${meRes2.status} (Expected 200)`);
    if (meRes2.status !== 200) throw new Error('Failed to use rotated access token');
    console.log('   ✓ Rotated access token verified.');

    // Test 7: Duplicate Email Protection
    console.log('\n--- Test 7: Duplicate Registration Protection ---');
    const dupRes = await fetch(`${FRONTEND_URL}/api/auth/register`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Requested-With': 'XMLHttpRequest',
        'X-AuthFlow-Client': 'Web'
      },
      body: JSON.stringify(regPayload)
    });
    console.log(`   Duplicate Register Status: ${dupRes.status} (Expected 409)`);
    const dupProblem = await dupRes.json();
    console.log(`   ProblemDetails:`, dupProblem);
    if (dupRes.status !== 409) throw new Error('Duplicate email was not rejected with 409');
    console.log('   ✓ Duplicate email rejected with 409 Conflict.');

    // Test 8: Invalid Credentials Login
    console.log('\n--- Test 8: Login with Invalid Password ---');
    const badLoginRes = await fetch(`${FRONTEND_URL}/api/auth/login`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Requested-With': 'XMLHttpRequest',
        'X-AuthFlow-Client': 'Web'
      },
      body: JSON.stringify({ email: uniqueEmail, password: 'WrongPassword123!' })
    });
    console.log(`   Bad Login Status: ${badLoginRes.status} (Expected 401)`);
    if (badLoginRes.status !== 401) throw new Error('Bad login was not rejected with 401');
    console.log('   ✓ Invalid credentials rejected with 401 Unauthorized.');

    // Test 9: Valid Login
    console.log('\n--- Test 9: Valid User Login ---');
    const loginRes = await fetch(`${FRONTEND_URL}/api/auth/login`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Requested-With': 'XMLHttpRequest',
        'X-AuthFlow-Client': 'Web'
      },
      body: JSON.stringify({ email: uniqueEmail, password: password })
    });
    console.log(`   Login Status: ${loginRes.status} (Expected 200)`);
    if (loginRes.status !== 200) throw new Error('Login failed');
    const loginData = await loginRes.json();
    const loginCookie = loginRes.headers.get('set-cookie');
    console.log(`   Login Succeeded for: ${loginData.user.fullName}`);
    console.log('   ✓ Login endpoint succeeded.');

    // Test 10: Logout and Revocation
    console.log('\n--- Test 10: User Logout and Cookie Clearance ---');
    const activeCookie = loginCookie ? loginCookie.split(';')[0] : cookieValue;
    const logoutRes = await fetch(`${FRONTEND_URL}/api/auth/logout`, {
      method: 'POST',
      headers: {
        'Cookie': activeCookie,
        'Origin': FRONTEND_URL,
        'X-Requested-With': 'XMLHttpRequest',
        'X-AuthFlow-Client': 'Web'
      }
    });
    console.log(`   Logout Status: ${logoutRes.status} (Expected 200)`);
    const logoutCookie = logoutRes.headers.get('set-cookie');
    console.log(`   Clear Cookie Header: ${logoutCookie}`);
    if (logoutCookie && !logoutCookie.includes('expires=')) {
      console.log('   Warning: Expected expired cookie date');
    }
    console.log('   ✓ Logout completed.');

    // Test 11: Refresh after Logout
    console.log('\n--- Test 11: Attempt Refresh with Revoked Token (Expect 401) ---');
    const postLogoutRefresh = await fetch(`${FRONTEND_URL}/api/auth/refresh`, {
      method: 'POST',
      headers: {
        'Cookie': activeCookie,
        'Origin': FRONTEND_URL,
        'X-Requested-With': 'XMLHttpRequest',
        'X-AuthFlow-Client': 'Web'
      }
    });
    console.log(`   Post-Logout Refresh Status: ${postLogoutRefresh.status} (Expected 401)`);
    if (postLogoutRefresh.status !== 401) throw new Error('Revoked session was allowed to refresh!');
    console.log('   ✓ Logged out token correctly rejected upon refresh attempt.');

    console.log('\n======================================================');
    console.log('🎉 ALL 11 END-TO-END FLOW VERIFICATION STEPS PASSED! 🎉');
    console.log('======================================================\n');
  } finally {
    console.log('Stopping test servers...');
    backendProc.kill();
    frontendProc.kill();
  }
}

run().catch((err) => {
  console.error('E2E Verification Error:', err);
  backendProc.kill();
  frontendProc.kill();
  process.exit(1);
});
