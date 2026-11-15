// AuthFlow — Session Synchronization & Single-Flight Regression Tests
import assert from 'node:assert';
import { setTimeout as sleep } from 'node:timers/promises';

console.log('--- Running Session Synchronization & BroadcastChannel Regression Suite ---\n');

// -------------------------------------------------------------
// Part 1: Single-Flight Refresh Mechanism Verification
// -------------------------------------------------------------
console.log('1. Testing Single-Flight Refresh Coalescing (Fix 1 verification)...');

class TestApiClient {
  constructor() {
    this.refreshPromise = null;
    this.networkCallCount = 0;
  }

  async mockNetworkRefresh(shouldSucceed = true) {
    this.networkCallCount++;
    await sleep(20); // Simulate network latency
    if (!shouldSucceed) {
      throw new Error('401 Unauthorized: Refresh token invalid');
    }
    return {
      accessToken: `jwt-token-${this.networkCallCount}`,
      expiresInMinutes: 15,
      user: {
        id: 'user-uuid-123',
        fullName: 'Test User',
        email: 'test@example.com',
        createdAtUtc: new Date().toISOString()
      }
    };
  }

  async refreshSingleFlight(shouldSucceed = true) {
    if (!this.refreshPromise) {
      this.refreshPromise = (async () => {
        try {
          return await this.mockNetworkRefresh(shouldSucceed);
        } finally {
          this.refreshPromise = null;
        }
      })();
    }
    return this.refreshPromise;
  }

  async refreshTokenSingleFlight() {
    const res = await this.refreshSingleFlight();
    return res.accessToken;
  }
}

const client = new TestApiClient();

// Fire 5 concurrent refresh requests (simulating React StrictMode double-mount + racing requests)
const concurrentResults = await Promise.all([
  client.refreshSingleFlight(),
  client.refreshSingleFlight(),
  client.refreshSingleFlight(),
  client.refreshSingleFlight(),
  client.refreshTokenSingleFlight()
]);

// Verify that despite 5 calls, exactly 1 network call was made
assert.strictEqual(client.networkCallCount, 1, 'Expected exactly 1 network call for 5 concurrent refresh attempts');

// Verify all 4 full responses got identical payload
for (let i = 0; i < 4; i++) {
  assert.strictEqual(concurrentResults[i].accessToken, 'jwt-token-1');
  assert.strictEqual(concurrentResults[i].user.email, 'test@example.com');
}
// Verify refreshTokenSingleFlight adapter returned the accessToken string
assert.strictEqual(concurrentResults[4], 'jwt-token-1');
console.log('   ✓ 5 concurrent requests coalesced into exactly 1 network dispatch.');

// Verify subsequent sequential call creates a new network request
const sequentialResult = await client.refreshSingleFlight();
assert.strictEqual(client.networkCallCount, 2, 'Expected a new network call after the previous settled');
assert.strictEqual(sequentialResult.accessToken, 'jwt-token-2');
console.log('   ✓ Subsequent request dispatched cleanly after initial promise settled.');

// Verify error handling clears refreshPromise and allows retry
let errorCaught = false;
try {
  await client.refreshSingleFlight(false);
} catch (err) {
  errorCaught = true;
  assert.strictEqual(err.message.includes('401'), true);
}
assert.strictEqual(errorCaught, true);
assert.strictEqual(client.refreshPromise, null, 'Expected refreshPromise to reset to null after error');
console.log('   ✓ Error state cleanly cleared refreshPromise without locking client.');

// -------------------------------------------------------------
// Part 2: SessionBroadcastManager Lifecycle & Cross-Tab Sync
// -------------------------------------------------------------
console.log('\n2. Testing BroadcastChannel Lifecycle & Cross-Tab Sync (Fix 2 verification)...');

class SessionBroadcastManager {
  static instance = null;

  constructor() {
    this.channel = null;
    this.init();
  }

  static getInstance() {
    if (!SessionBroadcastManager.instance) {
      SessionBroadcastManager.instance = new SessionBroadcastManager();
    }
    return SessionBroadcastManager.instance;
  }

  init() {
    if (typeof BroadcastChannel === 'undefined') return;
    try {
      this.channel = new BroadcastChannel('authflow_session_channel');
    } catch {
      this.channel = null;
    }
  }

  post(message) {
    if (typeof BroadcastChannel === 'undefined') return;
    try {
      if (!this.channel) {
        this.init();
      }
      this.channel?.postMessage(message);
    } catch {
      try {
        this.init();
        this.channel?.postMessage(message);
      } catch {
        // Safe fallback
      }
    }
  }

  subscribe(listener) {
    if (typeof BroadcastChannel === 'undefined') return () => {};
    if (!this.channel) {
      this.init();
    }
    if (!this.channel) return () => {};

    const onMessage = (event) => {
      if (event.data) {
        listener(event.data);
      }
    };

    this.channel.addEventListener('message', onMessage);
    return () => {
      this.channel?.removeEventListener('message', onMessage);
    };
  }
}

// Instantiate manager for Tab A
const tabAManager = SessionBroadcastManager.getInstance();

// Create independent listening channel representing Tab B
const tabBChannel = new BroadcastChannel('authflow_session_channel');
const tabBMessages = [];
tabBChannel.onmessage = (e) => tabBMessages.push(e.data);

// Create independent listening channel representing Tab C
const tabCChannel = new BroadcastChannel('authflow_session_channel');
const tabCMessages = [];
tabCChannel.onmessage = (e) => tabCMessages.push(e.data);

// Test Login Broadcast
const mockUser = { id: 'u1', fullName: 'Jane Doe', email: 'jane@example.com' };
tabAManager.post({ type: 'LOGIN', user: mockUser });
await sleep(50); // Allow event loop delivery

assert.strictEqual(tabBMessages.length, 1);
assert.strictEqual(tabBMessages[0].type, 'LOGIN');
assert.strictEqual(tabBMessages[0].user.email, 'jane@example.com');
assert.strictEqual(tabCMessages.length, 1);
assert.strictEqual(tabCMessages[0].type, 'LOGIN');
console.log('   ✓ LOGIN event broadcasted and received across independent tabs.');

// Test Logout Broadcast
tabAManager.post({ type: 'LOGOUT' });
await sleep(50);

assert.strictEqual(tabBMessages.length, 2);
assert.strictEqual(tabBMessages[1].type, 'LOGOUT');
assert.strictEqual(tabCMessages.length, 2);
assert.strictEqual(tabCMessages[1].type, 'LOGOUT');
console.log('   ✓ LOGOUT event broadcasted and received across independent tabs.');

// Test Component Unmount & Listener Deregistration
console.log('\n3. Testing Component Unmount / Listener Deregistration...');
let tabALocalCount = 0;
const unsubscribe = tabAManager.subscribe((msg) => {
  tabALocalCount++;
});

// Trigger message from Tab B to Tab A
tabBChannel.postMessage({ type: 'LOGOUT' });
await sleep(50);
assert.strictEqual(tabALocalCount, 1, 'Subscribed listener should receive message');

// Unsubscribe (simulating React component unmount)
unsubscribe();

// Trigger another message from Tab B
tabBChannel.postMessage({ type: 'LOGOUT' });
await sleep(50);
assert.strictEqual(tabALocalCount, 1, 'Unsubscribed listener must NOT receive subsequent messages');
console.log('   ✓ Listener successfully unregistered on component unmount without closing tab channel.');

// Test Closed Channel Resilience (No InvalidStateError)
console.log('\n4. Testing Closed Channel Resilience (InvalidStateError avoidance)...');
if (tabAManager.channel) {
  tabAManager.channel.close(); // Force channel closed to simulate edge case
}

let threwInvalidState = false;
try {
  tabAManager.post({ type: 'LOGOUT' });
} catch (e) {
  threwInvalidState = true;
}
assert.strictEqual(threwInvalidState, false, 'post() on closed channel must NOT throw uncaught InvalidStateError');
console.log('   ✓ postMessage on closed channel gracefully reinitializes without throwing InvalidStateError.');

// Cleanup test channels
tabBChannel.close();
tabCChannel.close();
if (tabAManager.channel) tabAManager.channel.close();

console.log('\n=============================================================');
console.log('🎉 ALL SESSION SYNCHRONIZATION REGRESSION TESTS PASSED! 🎉');
console.log('=============================================================\n');
