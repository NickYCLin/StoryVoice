import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { inflateRawSync } from 'node:zlib'

import { COMPANION_FILES, packageCompanion } from '../scripts/package.mjs'

const EXPECTED_COMPANION_FILES = [
  'manifest.json',
  'popup.html',
  'popup.css',
  'popup.js',
  'extractor.js',
  'content.js',
  'storyvoice-origin.mjs',
  'companion-token.mjs',
]

function crc32(buffer) {
  let crc = 0xffffffff
  for (const byte of buffer) {
    crc ^= byte
    for (let bit = 0; bit < 8; bit += 1) {
      crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0)
    }
  }
  return (crc ^ 0xffffffff) >>> 0
}

function inspectZip(buffer) {
  assert.ok(buffer.length >= 22)
  const endOffset = buffer.length - 22
  assert.equal(buffer.readUInt32LE(endOffset), 0x06054b50)
  assert.equal(buffer.readUInt16LE(endOffset + 4), 0)
  assert.equal(buffer.readUInt16LE(endOffset + 6), 0)

  const diskEntries = buffer.readUInt16LE(endOffset + 8)
  const totalEntries = buffer.readUInt16LE(endOffset + 10)
  const centralSize = buffer.readUInt32LE(endOffset + 12)
  const centralOffset = buffer.readUInt32LE(endOffset + 16)
  const commentLength = buffer.readUInt16LE(endOffset + 20)
  assert.equal(diskEntries, totalEntries)
  assert.equal(commentLength, 0)
  assert.equal(centralOffset + centralSize, endOffset)

  const entries = []
  let offset = centralOffset
  for (let index = 0; index < totalEntries; index += 1) {
    assert.equal(buffer.readUInt32LE(offset), 0x02014b50)
    const flags = buffer.readUInt16LE(offset + 8)
    const method = buffer.readUInt16LE(offset + 10)
    const expectedCrc = buffer.readUInt32LE(offset + 16)
    const compressedSize = buffer.readUInt32LE(offset + 20)
    const uncompressedSize = buffer.readUInt32LE(offset + 24)
    const fileNameLength = buffer.readUInt16LE(offset + 28)
    const extraLength = buffer.readUInt16LE(offset + 30)
    const commentLength = buffer.readUInt16LE(offset + 32)
    const localOffset = buffer.readUInt32LE(offset + 42)
    const name = buffer.subarray(offset + 46, offset + 46 + fileNameLength).toString('utf8')

    assert.equal(flags & 1, 0, `${name} must not be encrypted`)
    assert.equal(method, 8, `${name} must use deflate`)
    assert.ok(localOffset < centralOffset)
    assert.equal(buffer.readUInt32LE(localOffset), 0x04034b50)
    assert.equal(buffer.readUInt16LE(localOffset + 6), flags)
    assert.equal(buffer.readUInt16LE(localOffset + 8), method)
    assert.equal(buffer.readUInt32LE(localOffset + 14), expectedCrc)
    assert.equal(buffer.readUInt32LE(localOffset + 18), compressedSize)
    assert.equal(buffer.readUInt32LE(localOffset + 22), uncompressedSize)

    const localNameLength = buffer.readUInt16LE(localOffset + 26)
    const localExtraLength = buffer.readUInt16LE(localOffset + 28)
    const localName = buffer.subarray(localOffset + 30, localOffset + 30 + localNameLength).toString('utf8')
    assert.equal(localName, name)

    const dataOffset = localOffset + 30 + localNameLength + localExtraLength
    const compressed = buffer.subarray(dataOffset, dataOffset + compressedSize)
    assert.equal(compressed.length, compressedSize)
    const uncompressed = inflateRawSync(compressed)
    assert.equal(uncompressed.length, uncompressedSize)
    assert.equal(crc32(uncompressed), expectedCrc)

    entries.push({ name, dataOffset, compressedSize })
    offset += 46 + fileNameLength + extraLength + commentLength
  }
  assert.equal(offset, centralOffset + centralSize)
  return entries
}

test('Companion ZIP 只包含 Chrome 執行所需檔案', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'storyvoice-companion-'))
  const outputPath = join(directory, 'storyvoice-books-companion.zip')
  const secondOutputPath = join(directory, 'storyvoice-books-companion-second.zip')

  try {
    await packageCompanion(outputPath)
    await packageCompanion(secondOutputPath)
    const [archive, secondArchive] = await Promise.all([
      readFile(outputPath),
      readFile(secondOutputPath),
    ])

    const entries = inspectZip(archive)
    assert.deepEqual(secondArchive, archive)
    assert.deepEqual(COMPANION_FILES, EXPECTED_COMPANION_FILES)
    assert.deepEqual(entries.map(({ name }) => name), EXPECTED_COMPANION_FILES)
    assert.ok(archive.length > 1_000)
    assert.ok(archive.length < 1_000_000)

    const brokenEnd = Buffer.from(archive)
    brokenEnd.writeUInt32LE(0, brokenEnd.length - 22)
    assert.throws(() => inspectZip(brokenEnd))

    const brokenPayload = Buffer.from(archive)
    const firstEntry = entries[0]
    brokenPayload[firstEntry.dataOffset + Math.floor(firstEntry.compressedSize / 2)] ^= 1
    assert.throws(() => inspectZip(brokenPayload))
  } finally {
    await rm(directory, { recursive: true, force: true })
  }
})

test('Manifest、npm package 與 lockfile 使用同一版本', async () => {
  const [manifest, packageJson, packageLock] = await Promise.all([
    readFile(new URL('../manifest.json', import.meta.url), 'utf8').then(JSON.parse),
    readFile(new URL('../package.json', import.meta.url), 'utf8').then(JSON.parse),
    readFile(new URL('../package-lock.json', import.meta.url), 'utf8').then(JSON.parse),
  ])

  assert.equal(manifest.version, packageJson.version)
  assert.equal(packageLock.version, packageJson.version)
  assert.equal(packageLock.packages[''].version, packageJson.version)
})
