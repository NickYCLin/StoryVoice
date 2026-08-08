import test from 'node:test'
import assert from 'node:assert/strict'
import { validateCompanionToken } from '../companion-token.mjs'

const validToken = `svc_${'A'.repeat(43)}`

test('accepts the exact digest-backed companion token shape', () => {
  assert.equal(validateCompanionToken(`  ${validToken}  `), validToken)
})

test('rejects missing, truncated, or unexpected companion tokens', () => {
  assert.throws(() => validateCompanionToken(''), /連線金鑰/)
  assert.throws(() => validateCompanionToken('svc_short'), /連線金鑰/)
  assert.throws(() => validateCompanionToken(`other_${'A'.repeat(43)}`), /連線金鑰/)
})
