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

function locationBlock(config, declaration) {
  const start = config.indexOf(`${declaration} {`)
  assert.notEqual(start, -1, `missing ${declaration}`)

  const end = config.indexOf('}', start)
  assert.notEqual(end, -1, `unterminated ${declaration}`)
  return config.slice(start, end + 1)
}

test('long-running API requests retain a five-minute proxy budget at both Nginx layers', () => {
  assert.equal(containerNginx.match(/proxy_read_timeout 300s;/g)?.length, 4)
  assert.equal(containerNginx.match(/proxy_send_timeout 300s;/g)?.length, 4)
  assert.equal(hostNginxExample.match(/proxy_read_timeout 300s;/g)?.length, 2)
  assert.equal(hostNginxExample.match(/proxy_send_timeout 300s;/g)?.length, 2)
})

test('external voice routes are exact, bounded, unbuffered, uncached, and ordered first', () => {
  const storyVoiceRoute = locationBlock(
    containerNginx,
    'location = /StoryVoice/api/external/v1/speech',
  )
  const rootRoute = locationBlock(
    containerNginx,
    'location = /api/external/v1/speech',
  )
  const outerRoute = locationBlock(
    hostNginxExample,
    'location = /StoryVoice/api/external/v1/speech',
  )
  const storyVoiceVariantGuard = locationBlock(
    containerNginx,
    'location ~* ^/StoryVoice/api/external/v1/speech/?$',
  )
  const rootVariantGuard = locationBlock(
    containerNginx,
    'location ~* ^/api/external/v1/speech/?$',
  )
  const outerVariantGuard = locationBlock(
    hostNginxExample,
    'location ~* ^/StoryVoice/api/external/v1/speech/?$',
  )
  const containerTooLarge = locationBlock(
    containerNginx,
    'location @external_voice_request_too_large',
  )
  const outerTooLarge = locationBlock(
    hostNginxExample,
    'location @storyvoice_external_voice_request_too_large',
  )

  for (const route of [storyVoiceRoute, rootRoute, outerRoute]) {
    assert.match(route, /access_log off;/)
    assert.match(route, /client_max_body_size 4k;/)
    assert.match(route, /client_body_buffer_size 4k;/)
    assert.match(route, /proxy_request_buffering on;/)
    assert.match(route, /proxy_buffering off;/)
    assert.match(route, /proxy_cache off;/)
    assert.match(route, /proxy_read_timeout 300s;/)
    assert.match(route, /proxy_send_timeout 300s;/)
    assert.match(route, /error_page 413 = @(?:storyvoice_)?external_voice_request_too_large;/)
    assert.doesNotMatch(route, /Access-Control-Allow-Origin\s+['"]?\*/i)
  }

  assert.ok(
    containerNginx.indexOf('location = /StoryVoice/api/external/v1/speech') <
      containerNginx.indexOf('location /StoryVoice/api/'),
  )
  assert.ok(
    containerNginx.indexOf('location = /api/external/v1/speech') <
      containerNginx.indexOf('location /api/'),
  )
  assert.ok(
    hostNginxExample.indexOf('location = /StoryVoice/api/external/v1/speech') <
      hostNginxExample.indexOf('location /StoryVoice/'),
  )

  for (const config of [containerNginx, hostNginxExample]) {
    assert.match(config, /default_type application\/problem\+json;/)
    assert.match(config, /"code":"request_too_large"/)
  }

  for (const guard of [storyVoiceVariantGuard, rootVariantGuard, outerVariantGuard]) {
    assert.match(guard, /access_log off;/)
    assert.match(guard, /client_max_body_size 4k;/)
    assert.match(guard, /client_body_buffer_size 4k;/)
    assert.match(guard, /default_type application\/problem\+json;/)
    assert.match(guard, /"code":"invalid_request"/)
    assert.doesNotMatch(guard, /proxy_pass/)
  }

  for (const tooLarge of [containerTooLarge, outerTooLarge]) {
    assert.match(tooLarge, /access_log off;/)
  }

  assert.doesNotMatch(containerNginx, /location \^~ \/StoryVoice\/api\//)
  assert.doesNotMatch(containerNginx, /location \^~ \/StoryVoice\/ \{/)
  assert.doesNotMatch(hostNginxExample, /location \^~ \/StoryVoice\/ \{/)
})
