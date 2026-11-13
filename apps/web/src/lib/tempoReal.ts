import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { EVENTO_TOKEN, obterToken } from './api'

let conexao: HubConnection | null = null
let tokenAtual: string | null = null
// Manipuladores registrados: ao trocar de conexão (entrar/sair da conta) são religados na nova.
const registrados = new Map<string, Set<(d: unknown) => void>>()
const aoTrocar = new Set<() => void>()

export function obterConexao(): HubConnection {
  const token = obterToken()
  if (conexao && tokenAtual === token) return conexao
  void conexao?.stop()
  tokenAtual = token
  conexao = new HubConnectionBuilder()
    .withUrl('/hubs/operacao', { accessTokenFactory: () => obterToken() ?? '' })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
    .configureLogging(LogLevel.Warning)
    .build()
  for (const [evento, hs] of registrados) for (const h of hs) conexao.on(evento, h)
  return conexao
}

if (typeof window !== 'undefined') window.addEventListener(EVENTO_TOKEN, () => {
  if (!conexao || tokenAtual === obterToken()) return
  obterConexao()
  void garantirConectado()
  for (const f of aoTrocar) f()
})

export async function garantirConectado() {
  const c = obterConexao()
  if (c.state === HubConnectionState.Disconnected) {
    try { await c.start() } catch { /* sem rede: reconecta depois */ }
  }
  return c
}

/** Executa `aoReceber` quando o evento chega pelo SignalR (atualizações próximas do tempo real). */
export function useEvento(evento: string, aoReceber: (dados: unknown) => void, aoConectar?: (c: HubConnection) => void) {
  const [conectado, setConectado] = useState(false)
  const ref = useRef(aoReceber)
  const refConectar = useRef(aoConectar)
  ref.current = aoReceber
  refConectar.current = aoConectar
  useEffect(() => {
    let vivo = true
    const manipulador = (d: unknown) => ref.current(d)
    if (!registrados.has(evento)) registrados.set(evento, new Set())
    registrados.get(evento)!.add(manipulador)
    const ligar = () => {
      const c = obterConexao()
      c.off(evento, manipulador); c.on(evento, manipulador)
      c.onreconnected(() => { setConectado(true); refConectar.current?.(c) })
      c.onclose(() => vivo && setConectado(false))
      void garantirConectado().then((x) => {
        if (!vivo || x !== conexao) return
        setConectado(x.state === HubConnectionState.Connected)
        if (x.state === HubConnectionState.Connected) refConectar.current?.(x)
      })
    }
    ligar()
    aoTrocar.add(ligar)
    return () => { vivo = false; aoTrocar.delete(ligar); registrados.get(evento)?.delete(manipulador); conexao?.off(evento, manipulador) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [evento])
  return conectado
}
