import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const containerNginx = readFileSync(
  new URL('../../../docker/nginx.conf', import.meta.url),
  'utf8',
)
const hostNginxExample = readFileSync(
  new URL('../../../deploy/nginx-storyvoice-location.conf.example', import.meta.url),
  'utf8',
)

test('long-running API requests retain a five-minute proxy budget at both Nginx layers', () => {
  assert.equal(containerNginx.match(/proxy_read_timeout 300s;/g)?.length, 2)
  assert.equal(containerNginx.match(/proxy_send_timeout 300s;/g)?.length, 2)
  assert.match(hostNginxExample, /proxy_read_timeout 300s;/)
  assert.match(hostNginxExample, /proxy_send_timeout 300s;/)
})
