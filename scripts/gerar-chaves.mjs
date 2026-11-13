// Gera segredos para .env: segredo das centrais, chave AES-256 e par VAPID (Web Push).
import { generateKeyPairSync, randomBytes } from 'node:crypto'
const b64u = (b) => Buffer.from(b).toString('base64url')
const { publicKey, privateKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' })
const jwkPub = publicKey.export({ format: 'jwk' })
const jwkPriv = privateKey.export({ format: 'jwk' })
const pub = Buffer.concat([Buffer.from([4]), Buffer.from(jwkPub.x, 'base64url'), Buffer.from(jwkPub.y, 'base64url')])
console.log(`Centrais__SegredoMestre=${randomBytes(32).toString('base64url')}`)
console.log(`Dados__ChaveCifragem=${randomBytes(32).toString('base64')}`)
console.log(`WebPush__ChavePublica=${b64u(pub)}`)
console.log(`WebPush__ChavePrivada=${jwkPriv.d}`)
