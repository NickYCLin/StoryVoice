import { deflateRawSync } from 'node:zlib'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const companionRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')

export const COMPANION_FILES = [
  'manifest.json',
  'popup.html',
  'popup.css',
  'popup.js',
  'extractor.js',
  'page-bridge.js',
  'content.js',
  'storyvoice-origin.mjs',
  'companion-token.mjs',
]

const crcTable = Array.from({ length: 256 }, (_, index) => {
  let value = index
  for (let bit = 0; bit < 8; bit += 1) {
    value = (value & 1) !== 0 ? (value >>> 1) ^ 0xedb88320 : value >>> 1
  }
  return value >>> 0
})

function crc32(buffer) {
  let crc = 0xffffffff
  for (const byte of buffer) crc = (crc >>> 8) ^ crcTable[(crc ^ byte) & 0xff]
  return (crc ^ 0xffffffff) >>> 0
}

function localHeader({ name, crc, compressedSize, size }) {
  const header = Buffer.alloc(30)
  header.writeUInt32LE(0x04034b50, 0)
  header.writeUInt16LE(20, 4)
  header.writeUInt16LE(0x0800, 6)
  header.writeUInt16LE(8, 8)
  header.writeUInt16LE(0, 10)
  header.writeUInt16LE(0x0021, 12)
  header.writeUInt32LE(crc, 14)
  header.writeUInt32LE(compressedSize, 18)
  header.writeUInt32LE(size, 22)
  header.writeUInt16LE(name.length, 26)
  header.writeUInt16LE(0, 28)
  return header
}

function centralHeader({ name, crc, compressedSize, size, offset }) {
  const header = Buffer.alloc(46)
  header.writeUInt32LE(0x02014b50, 0)
  header.writeUInt16LE(0x0314, 4)
  header.writeUInt16LE(20, 6)
  header.writeUInt16LE(0x0800, 8)
  header.writeUInt16LE(8, 10)
  header.writeUInt16LE(0, 12)
  header.writeUInt16LE(0x0021, 14)
  header.writeUInt32LE(crc, 16)
  header.writeUInt32LE(compressedSize, 20)
  header.writeUInt32LE(size, 24)
  header.writeUInt16LE(name.length, 28)
  header.writeUInt16LE(0, 30)
  header.writeUInt16LE(0, 32)
  header.writeUInt16LE(0, 34)
  header.writeUInt16LE(0, 36)
  header.writeUInt32LE(0o100644 * 0x10000, 38)
  header.writeUInt32LE(offset, 42)
  return header
}

export async function packageCompanion(outputPath) {
  const localParts = []
  const centralParts = []
  let localOffset = 0

  for (const fileName of COMPANION_FILES) {
    const name = Buffer.from(fileName, 'utf8')
    const source = await readFile(resolve(companionRoot, fileName))
    const compressed = deflateRawSync(source, { level: 9 })
    const entry = {
      name,
      crc: crc32(source),
      compressedSize: compressed.length,
      size: source.length,
      offset: localOffset,
    }
    const header = localHeader(entry)
    localParts.push(header, name, compressed)
    centralParts.push(centralHeader(entry), name)
    localOffset += header.length + name.length + compressed.length
  }

  const centralDirectory = Buffer.concat(centralParts)
  const end = Buffer.alloc(22)
  end.writeUInt32LE(0x06054b50, 0)
  end.writeUInt16LE(0, 4)
  end.writeUInt16LE(0, 6)
  end.writeUInt16LE(COMPANION_FILES.length, 8)
  end.writeUInt16LE(COMPANION_FILES.length, 10)
  end.writeUInt32LE(centralDirectory.length, 12)
  end.writeUInt32LE(localOffset, 16)
  end.writeUInt16LE(0, 20)

  await mkdir(dirname(outputPath), { recursive: true })
  await writeFile(outputPath, Buffer.concat([...localParts, centralDirectory, end]))
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const outputPath = resolve(process.argv[2] ?? 'dist/storyvoice-books-companion.zip')
  await packageCompanion(outputPath)
  console.log(outputPath)
}
