// Client Error Parsing & Resilience Verification Test
import assert from 'node:assert';

console.log('--- Running Client Error Resilience Verification ---');

function sanitizeErrorMessage(message) {
  if (!message) return undefined;
  const trimmed = message.trim();
  if (trimmed.startsWith('<') || trimmed.includes('<!DOCTYPE') || trimmed.includes('<html')) {
    return 'The server is currently unavailable or returned an unreadable response. Please try again.';
  }
  const stripped = trimmed.replace(/<[^>]*>?/gm, '').trim();
  return stripped.length > 0 ? stripped : undefined;
}

function getStatusFallbackMessage(status, statusText) {
  switch (status) {
    case 429:
      return 'Too many requests. Please slow down and try again later.';
    case 502:
      return 'The backend server is starting up or temporarily unreachable. Please retry in a few moments.';
    case 503:
      return 'The service is temporarily unavailable. Please try again shortly.';
    case 504:
      return 'The server took too long to respond. Please try again in a few moments.';
    case 500:
      return 'An unexpected server error occurred. Please try again later.';
    default:
      return statusText || 'An unexpected error occurred. Please try again.';
  }
}

async function parseResponseError(response) {
  const contentType = response.headers.get('content-type') || '';
  let problem;

  if (contentType.includes('application/json') || contentType.includes('application/problem+json')) {
    try {
      problem = await response.json();
    } catch {
      problem = {
        status: response.status,
        title: response.statusText || 'Error',
        detail: 'Failed to parse response from server.',
      };
    }
  } else {
    const rawText = (await response.text().catch(() => '')).trim();
    const isHtml =
      contentType.includes('text/html') ||
      rawText.startsWith('<') ||
      rawText.includes('<!DOCTYPE') ||
      rawText.includes('<html');

    const detail = isHtml || !rawText
      ? getStatusFallbackMessage(response.status, response.statusText)
      : rawText.length > 250
        ? getStatusFallbackMessage(response.status, response.statusText)
        : rawText;

    problem = {
      status: response.status,
      title: response.statusText || 'Error',
      detail,
    };
  }

  const cleanDetail = sanitizeErrorMessage(problem.detail);
  const cleanTitle = sanitizeErrorMessage(problem.title);
  const message = cleanDetail || cleanTitle || `Request failed with status ${response.status}`;

  return { status: response.status, problem, message };
}

// Test 1: Render Cold-Start / Gateway 502 Bad Gateway with HTML
console.log('1. Testing 502 Bad Gateway HTML Response (Render cold start simulation)...');
const res502 = new Response('<!DOCTYPE html><html><head><title>502 Bad Gateway</title></head><body><h1>Bad Gateway</h1><p>Render edge gateway is waiting for container.</p></body></html>', {
  status: 502,
  statusText: 'Bad Gateway',
  headers: { 'Content-Type': 'text/html; charset=utf-8' }
});

const err502 = await parseResponseError(res502);
assert.strictEqual(err502.status, 502);
assert.strictEqual(err502.message.includes('<'), false, 'Error message must NOT contain any HTML tags or brackets');
assert.strictEqual(err502.message.includes('<!DOCTYPE'), false, 'Error message must NOT contain <!DOCTYPE');
assert.strictEqual(err502.message, 'The backend server is starting up or temporarily unreachable. Please retry in a few moments.');
console.log('   ✓ 502 HTML correctly sanitized to readable cold-start notification.');

// Test 2: Gateway 504 Gateway Timeout with HTML
console.log('2. Testing 504 Gateway Timeout HTML Response...');
const res504 = new Response('<!DOCTYPE html><html><body><h1>504 Gateway Timeout</h1></body></html>', {
  status: 504,
  statusText: 'Gateway Timeout',
  headers: { 'Content-Type': 'text/html' }
});

const err504 = await parseResponseError(res504);
assert.strictEqual(err504.status, 504);
assert.strictEqual(err504.message.includes('<'), false);
assert.strictEqual(err504.message, 'The server took too long to respond. Please try again in a few moments.');
console.log('   ✓ 504 HTML correctly sanitized to timeout notification.');

// Test 3: Malformed JSON with application/json header
console.log('3. Testing Malformed JSON Response...');
const resMalformed = new Response('{"status": 500, "unclosed_json', {
  status: 500,
  statusText: 'Internal Server Error',
  headers: { 'Content-Type': 'application/json' }
});

const errMalformed = await parseResponseError(resMalformed);
assert.strictEqual(errMalformed.status, 500);
assert.strictEqual(errMalformed.message, 'Failed to parse response from server.');
console.log('   ✓ Malformed JSON handled gracefully without crashing.');

// Test 4: RFC 7807 ProblemDetails (Duplicate Email 409)
console.log('4. Testing RFC 7807 ProblemDetails (409 Conflict)...');
const res409 = new Response(JSON.stringify({
  status: 409,
  title: 'Duplicate Email',
  detail: 'An account with this email address already exists.',
  instance: '/api/auth/register'
}), {
  status: 409,
  statusText: 'Conflict',
  headers: { 'Content-Type': 'application/problem+json' }
});

const err409 = await parseResponseError(res409);
assert.strictEqual(err409.status, 409);
assert.strictEqual(err409.message, 'An account with this email address already exists.');
assert.strictEqual(err409.problem.title, 'Duplicate Email');
console.log('   ✓ RFC 7807 ProblemDetails preserved with full accuracy.');

// Test 5: ASP.NET Core Validation ProblemDetails (400 Bad Request)
console.log('5. Testing ASP.NET Core Validation Errors (400 Bad Request)...');
const res400 = new Response(JSON.stringify({
  status: 400,
  title: 'One or more validation errors occurred.',
  errors: {
    Email: ['Please enter a valid email address.'],
    Password: ['Password must be at least 8 characters long.']
  }
}), {
  status: 400,
  statusText: 'Bad Request',
  headers: { 'Content-Type': 'application/problem+json' }
});

const err400 = await parseResponseError(res400);
assert.strictEqual(err400.status, 400);
assert.strictEqual(err400.problem.errors.Email[0], 'Please enter a valid email address.');
console.log('   ✓ Validation field errors preserved for form mapping.');

console.log('\n--- ALL CLIENT ERROR RESILIENCE CHECKS PASSED SUCCESSFULLY! ---\n');
