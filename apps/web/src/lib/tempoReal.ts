import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { obterToken } from './api'

let conexao: HubConnection | null = null
let tokenAtual: string | null = null

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
  return conexao
}

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
    const c = obterConexao()
    const manipulador = (d: unknown) => ref.current(d)
    c.on(evento, manipulador)
    const reconectou = () => { setConectado(true); refConectar.current?.(c) }
    c.onreconnected(reconectou)
    c.onclose(() => vivo && setConectado(false))
    void garantirConectado().then((x) => {
      if (!vivo) return
      setConectado(x.state === HubConnectionState.Connected)
      if (x.state === HubConnectionState.Connected) refConectar.current?.(x)
    })
    return () => { vivo = false; c.off(evento, manipulador) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [evento])
  return conectado
}
