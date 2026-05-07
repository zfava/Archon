// ArchonAI Load Test: Agent Execution Stress
// Validates end-to-end workflow completion rate and time-to-first-result.
// 10 VUs sustained for 3 minutes. Each VU posts an objective, polls for completion.
// Threshold: 90% of workflows complete within 60 seconds.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';
import { BASE_URL, API_VERSION, TEST_USERS } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import { errorRate, uniqueId, parseBody } from '../lib/helpers.js';
import { slaThresholds } from '../lib/thresholds.js';

// Custom metrics
const workflowCompletionTime = new Trend('agent_task_duration', true);
const timeToFirstResult = new Trend('agent_time_to_first_result', true);
const workflowCompletionRate = new Rate('agent_workflow_completion_rate');
const workflowsSubmitted = new Counter('agent_workflows_submitted');
const workflowsCompleted = new Counter('agent_workflows_completed');

export const options = {
  scenarios: {
    agent_stress: {
      executor: 'constant-vus',
      vus: 10,
      duration: '3m',
    },
  },
  thresholds: {
    ...slaThresholds,
    agent_task_duration: ['p(95)<30000'],               // agent tasks < 30s p95
    agent_workflow_completion_rate: ['rate>0.90'],       // 90% complete within timeout
    agent_time_to_first_result: ['p(95)<15000'],        // first result < 15s p95
  },
};

const POLL_INTERVAL_SEC = 2;
const MAX_POLL_ATTEMPTS = 30; // 30 * 2s = 60s max wait
const WORKFLOW_TYPES = ['revenue_analysis', 'operational_review', 'customer_health', 'growth_forecast'];

export function setup() {
  registerUser(TEST_USERS.operator);
  const session = loginUser(TEST_USERS.operator);
  return { token: session.token };
}

export default function (data) {
  const headers = authHeaders(data.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}`;

  group('agent: submit and poll workflow', () => {
    // Step 1: Submit an objective/workflow
    const workflowType = WORKFLOW_TYPES[Math.floor(Math.random() * WORKFLOW_TYPES.length)];
    const objectiveId = uniqueId('obj');

    const payload = JSON.stringify({
      objectiveId,
      type: workflowType,
      description: `Stress test workflow: ${workflowType}`,
      parameters: {
        depth: 'standard',
        timeRange: '30d',
        priority: 'normal',
      },
    });

    const submitRes = http.post(`${baseApi}/workflows`, payload, {
      headers,
      tags: { name: 'submit_workflow' },
    });

    const submitted = check(submitRes, {
      'submit workflow: 200/201/202': (r) => [200, 201, 202].includes(r.status),
    });

    if (!submitted) {
      errorRate.add(true);
      workflowCompletionRate.add(false);
      return;
    }

    workflowsSubmitted.add(1);

    const submitBody = parseBody(submitRes);
    const workflowId = submitBody && (submitBody.id || submitBody.workflowId || objectiveId);
    const startTime = Date.now();
    let firstResultTime = null;
    let completed = false;

    // Step 2: Poll for completion every 2 seconds
    for (let attempt = 0; attempt < MAX_POLL_ATTEMPTS; attempt++) {
      sleep(POLL_INTERVAL_SEC);

      const pollRes = http.get(`${baseApi}/workflows/${workflowId}`, {
        headers,
        tags: { name: 'poll_workflow' },
      });

      if (pollRes.status >= 500) {
        errorRate.add(true);
        continue;
      }

      const pollBody = parseBody(pollRes);
      if (!pollBody) continue;

      const status = pollBody.status || pollBody.state;

      // Track time-to-first-result
      if (firstResultTime === null && status && status !== 'pending' && status !== 'queued') {
        firstResultTime = Date.now() - startTime;
        timeToFirstResult.add(firstResultTime);
      }

      // Check for terminal states
      if (['completed', 'succeeded', 'done', 'failed', 'error'].includes(status)) {
        const elapsed = Date.now() - startTime;
        workflowCompletionTime.add(elapsed);

        if (['completed', 'succeeded', 'done'].includes(status)) {
          completed = true;
          workflowsCompleted.add(1);
        }
        break;
      }
    }

    // If we never got a first result timestamp, record now
    if (firstResultTime === null) {
      timeToFirstResult.add(Date.now() - startTime);
    }

    workflowCompletionRate.add(completed);

    if (!completed) {
      const elapsed = Date.now() - startTime;
      workflowCompletionTime.add(elapsed);
      console.warn(`Workflow ${workflowId} did not complete within ${elapsed}ms`);
    }
  });
}

export function teardown(data) {
  console.log('Agent execution stress test complete.');
}
